"""Офлайн-превью планеты: та же математика, что в PlanetSurface.shader.

Нужен для того, чтобы дефекты вида искались здесь, а не круговыми заходами
через запуск Unity и скриншот. Всё, что здесь считается, обязано совпадать с
фрагментным шейдером — иначе превью врёт и хуже, чем ничего.

Совпадает намеренно:
    развёртка направления в equirect, обёрнутый диффуз, GGX, сумеречный член,
    смешивание облаков, свечение рифтов, ACES-подобная кривая.

Не совпадает и совпадать не может:
    выбор мипа и анизотропия (здесь честная билинейная выборка по одному
    уровню), сглаживание краёв, атмосферная оболочка. Значит, шов от выбора
    мипа тут не воспроизведётся — зато воспроизведётся любой шов, живущий в
    самих картах или в развёртке.

Запуск:
    python3 scripts/preview_planet.py [размер]
"""

from __future__ import annotations

import sys

import numpy as np
from PIL import Image

TEXTURE_DIR = "Assets/Textures/UI"
OUTPUT_PATH = "planet_preview.png"

# Значения читаются из материала, а не задаются здесь заново: разъехавшись, они
# сделали бы превью бесполезным ровно тогда, когда оно нужнее всего.
MATERIAL_PATH = "Assets/Materials/PlanetSurface.mat"
ATMOSPHERE_PATH = "Assets/Materials/PlanetAtmosphere.mat"
SCENE_PATH = "Assets/Scenes/MainMenu.unity"

# Дистанция в РАДИУСАХ планеты, а не в единицах сцены: сфера здесь единичная.
# В сцене радиус 1.5, и обзорная дистанция теперь выводится из доли ширины
# кадра — при 16:9 это 4.35 единицы, то есть 2.90 радиуса. Прежнее значение
# 6.9 брало единицы сцены за радиусы и завышало дистанцию втрое с лишним:
# перспективное сокращение у лимба выходило слабее, чем в игре.
CAMERA_DISTANCE = 2.90
CAMERA_FOV = 36.0
FRAME_MARGIN = 1.14


def scene_planet_rotation() -> np.ndarray:
    """Поворот PlanetSurface из сцены, как матрица объект->мир.

    Читается из сцены, а не задаётся здесь константой: шейдер разворачивает
    направление в UV в пространстве ОБЪЕКТА, поэтому при ненулевом повороте к
    камере обращена другая часть карты. Превью с нулевым поворотом показывало
    бы верную модель освещения и неверную географию — то есть ровно ту сторону
    планеты, которой на экране нет.
    """
    lines = open(SCENE_PATH).read().splitlines()
    for i, line in enumerate(lines):
        if line.strip() == "m_Name: PlanetSurface":
            for back in range(i, max(i - 400, 0), -1):
                if "m_LocalRotation:" in lines[back]:
                    body = lines[back].split("{", 1)[1].rstrip("}")
                    q = dict(piece.split(":", 1) for piece in body.replace(" ", "").split(","))
                    x, y, z, w = (float(q[k]) for k in "xyzw")
                    return np.array([
                        [1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
                        [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
                        [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)],
                    ])
    return np.eye(3)


def load_material(path: str) -> dict[str, object]:
    """Плоский разбор m_Floats и m_Colors из .mat. Полный YAML тут не нужен."""
    values: dict[str, object] = {}
    for raw in open(path):
        line = raw.strip()
        if not line.startswith("- _"):
            continue
        name, _, rest = line[2:].partition(":")
        rest = rest.strip()
        if rest.startswith("{"):
            parts = dict(
                piece.split(":", 1)
                for piece in rest.strip("{}").replace(" ", "").split(",")
                if ":" in piece
            )
            if {"r", "g", "b"} <= parts.keys():
                values[name] = np.array([float(parts["r"]), float(parts["g"]), float(parts["b"])])
        else:
            try:
                values[name] = float(rest)
            except ValueError:
                pass
    return values


def sample_equirect(texture: np.ndarray, uv: np.ndarray) -> np.ndarray:
    """Билинейная выборка. По долготе заворачивается, по широте зажимается."""
    height, width = texture.shape[:2]

    x = uv[..., 0] * width - 0.5
    y = uv[..., 1] * height - 0.5

    x0 = np.floor(x).astype(np.int64)
    y0 = np.floor(y).astype(np.int64)
    fx = (x - x0)[..., None]
    fy = (y - y0)[..., None]

    x0m = np.mod(x0, width)
    x1m = np.mod(x0 + 1, width)
    y0m = np.clip(y0, 0, height - 1)
    y1m = np.clip(y0 + 1, 0, height - 1)

    top = texture[y0m, x0m] * (1 - fx) + texture[y0m, x1m] * fx
    bottom = texture[y1m, x0m] * (1 - fx) + texture[y1m, x1m] * fx
    return top * (1 - fy) + bottom * fy


def srgb_to_linear(c: np.ndarray) -> np.ndarray:
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def smoothstep(edge0: float, edge1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def render(size: int, zoom: float = 1.0) -> np.ndarray:
    mat = load_material(MATERIAL_PATH)

    albedo_map = srgb_to_linear(
        np.asarray(Image.open(f"{TEXTURE_DIR}/planet_albedo.png"), dtype=np.float64) / 255.0)
    normal_map = np.asarray(Image.open(f"{TEXTURE_DIR}/planet_normal.png"), dtype=np.float64) / 255.0
    packed_map = np.asarray(Image.open(f"{TEXTURE_DIR}/planet_packed.png"), dtype=np.float64) / 255.0

    # Перспективная камера с параметрами из MenuSceneryController: расстояние
    # 6.9 радиуса, поле зрения 36 градусов. Ортографическая проекция здесь не
    # годится: с конечного расстояния видно меньше полусферы, и терминатор,
    # лежащий за центром диска, оказывается ближе к лимбу, чем при
    # параллельных лучах. Именно на этом превью расходилось с Unity.
    camera = np.array([0.0, 0.0, -CAMERA_DISTANCE])
    # Кадр по угловому размеру планеты, а не по полному полю зрения камеры:
    # в сцене диск занимает почти весь элемент, а полный кадр показывал бы его
    # мелким пятном посреди пустоты. На перспективу это не влияет — она задана
    # расстоянием, а не рамкой.
    half_extent = np.tan(np.arcsin(1.0 / CAMERA_DISTANCE) * FRAME_MARGIN) * CAMERA_DISTANCE / zoom

    axis = np.linspace(-half_extent, half_extent, size)
    sx, sy = np.meshgrid(axis, -axis)
    ray = np.stack((sx, sy, np.full_like(sx, CAMERA_DISTANCE)), axis=-1)
    ray /= np.linalg.norm(ray, axis=-1, keepdims=True)

    # Пересечение луча с единичной сферой в начале координат.
    b = np.sum(ray * camera, axis=-1)
    discriminant = b * b - (np.dot(camera, camera) - 1.0)
    disc = discriminant >= 0.0
    distance = -b - np.sqrt(np.clip(discriminant, 0.0, None))

    hit = camera + ray * distance[..., None]
    dirs = hit / np.maximum(np.linalg.norm(hit, axis=-1, keepdims=True), 1e-9)

    # Направление в пространстве объекта: именно им шейдер адресует карты.
    rotation = scene_planet_rotation()
    dirs_os = dirs @ rotation

    uv = np.stack(
        (
            np.arctan2(dirs_os[..., 2], dirs_os[..., 0]) * (0.5 / np.pi) + 0.5,
            np.arcsin(np.clip(dirs_os[..., 1], -1.0, 1.0)) / np.pi + 0.5,
        ),
        axis=-1,
    )

    albedo = sample_equirect(albedo_map, uv)
    packed = sample_equirect(packed_map, uv)
    normal_ts = sample_equirect(normal_map, uv) * 2.0 - 1.0

    roughness = mat["_RoughnessMin"] + (mat["_RoughnessMax"] - mat["_RoughnessMin"]) * packed[..., 0]
    rift = packed[..., 1]
    cloud_coverage = packed[..., 2]

    # Базис строится в пространстве объекта и переносится в мир — так же, как
    # в шейдере. Построенный сразу в мире, он развернул бы рельеф относительно
    # света ровно на поворот планеты.
    up = np.where(np.abs(dirs_os[..., 1:2]) < 0.99, np.array([0.0, 1.0, 0.0]), np.array([1.0, 0.0, 0.0]))
    tangent = np.cross(up, dirs_os) @ rotation.T
    tangent /= np.maximum(np.linalg.norm(tangent, axis=-1, keepdims=True), 1e-9)
    bitangent = np.cross(dirs, tangent)

    strength = mat["_NormalStrength"]
    normal = (tangent * normal_ts[..., 0:1] * strength
              + bitangent * normal_ts[..., 1:2] * strength
              + dirs)
    normal /= np.maximum(np.linalg.norm(normal, axis=-1, keepdims=True), 1e-9)

    light = mat["_SunDirWS"] / np.linalg.norm(mat["_SunDirWS"])

    # Направление на камеру считается на каждый пиксель, а не берётся общим:
    # при поле зрения 36 градусов оно заметно разворачивается через кадр, и от
    # него зависят и блик, и толщина атмосферы у лимба.
    view = -ray
    half = light + view
    half /= np.maximum(np.linalg.norm(half, axis=-1, keepdims=True), 1e-9)

    ndl = np.sum(normal * light, axis=-1)
    ndv = np.clip(np.sum(normal * view, axis=-1), 0.0, 1.0)
    ndh = np.clip(np.sum(normal * half, axis=-1), 0.0, 1.0)
    vdh = np.clip(np.sum(view * half, axis=-1), 0.0, 1.0)

    alpha = roughness * roughness
    alpha_sq = alpha * alpha
    denom = ndh * ndh * (alpha_sq - 1.0) + 1.0
    distribution = alpha_sq / np.maximum(np.pi * denom * denom, 1e-4)
    k = (roughness + 1.0) ** 2 * 0.125
    ndl_pos = np.clip(ndl, 0.0, 1.0)
    geometry = (ndl_pos / np.maximum(ndl_pos * (1 - k) + k, 1e-4)) * (ndv / np.maximum(ndv * (1 - k) + k, 1e-4))
    fresnel = (0.04 + 0.96 * (1.0 - vdh) ** 5)[..., None]
    specular = (distribution * geometry / np.maximum(4.0 * ndl_pos * ndv, 1e-4))[..., None] * fresnel

    cloud = (smoothstep(mat["_CloudCoverage"],
                        mat["_CloudCoverage"] + mat["_CloudSoftness"],
                        cloud_coverage) * mat["_CloudOpacity"])[..., None]
    albedo = albedo * (1 - cloud) + mat["_CloudColor"] * cloud
    rift = rift * (1.0 - cloud[..., 0])

    wrap = mat.get("_TerminatorSoftness", 0.0)
    wrapped = np.clip((ndl + wrap) / (1.0 + wrap), 0.0, 1.0)

    sun = mat["_SunColor"] * mat["_SunIntensity"]
    diffuse = (albedo / np.pi) * (1.0 - fresnel) * wrapped[..., None]
    direct = (diffuse + specular) * sun

    twilight = np.clip(1.0 - np.abs(ndl), 0.0, 1.0) ** 1.8 * np.clip(ndl + 0.55, 0.0, 1.0)
    scatter = albedo * mat["_TwilightColor"] * twilight[..., None] * mat["_TwilightIntensity"]

    emission = mat["_MagmaColor"] * (rift * mat["_MagmaIntensity"])[..., None]
    ambient = albedo * mat["_NightAmbient"]

    color = (direct + scatter + emission + ambient) * mat.get("_Exposure", 1.0)
    mapped = (color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14)
    mapped = np.clip(mapped, 0.0, 1.0)

    # Оболочка атмосферы поверх диска и вокруг него. Аддитивная, поэтому
    # считается по всей площади внутри своего силуэта, а не только по планете.
    atmo = load_material(ATMOSPHERE_PATH)
    ratio = atmo["_RadiusRatio"]
    shell_radius = 1.0 / ratio

    shell_b = np.sum(ray * camera, axis=-1)
    shell_disc = shell_b * shell_b - (np.dot(camera, camera) - shell_radius * shell_radius)
    shell = shell_disc >= 0.0
    shell_hit = camera + ray * (-shell_b - np.sqrt(np.clip(shell_disc, 0.0, None)))[..., None]
    shell_dirs = shell_hit / np.maximum(np.linalg.norm(shell_hit, axis=-1, keepdims=True), 1e-9)

    shell_ndv = np.abs(np.sum(shell_dirs * view, axis=-1))
    impact = np.sqrt(np.clip(1.0 - shell_ndv * shell_ndv, 0.0, None))
    outer_half = np.sqrt(np.clip(1.0 - impact * impact, 0.0, None))
    inner_half = np.sqrt(np.clip(ratio * ratio - impact * impact, 0.0, None))
    max_chord = max(np.sqrt(max(1.0 - ratio * ratio, 0.0)), 1e-4)
    rim = np.clip((outer_half - inner_half) / max_chord, 0.0, 1.0) ** atmo["_RimPower"]

    shell_ndl = np.sum(shell_dirs * light, axis=-1)
    sun_amount = np.maximum(
        np.clip((shell_ndl + atmo["_SunWrap"]) / (1.0 + atmo["_SunWrap"]), 0.0, 1.0),
        atmo["_NightFloor"])
    forward = np.clip(np.sum(view * -light, axis=-1), 0.0, 1.0) ** 6 * atmo["_ForwardScatter"]

    tint = (atmo["_AtmosphereColor"]
            + (atmo["_HorizonColor"] - atmo["_AtmosphereColor"]) * np.clip(rim * 1.4, 0.0, 1.0)[..., None])
    glow = tint * (rim * sun_amount * atmo["_Density"] * (1.0 + forward))[..., None]

    background = np.array([0.02, 0.03, 0.06])
    out = np.where(disc[..., None], mapped, background)
    out = out + np.where(shell[..., None], glow, 0.0)

    return np.clip(np.rint(np.clip(out, 0.0, 1.0) * 255.0), 0, 255).astype(np.uint8)


def main() -> None:
    size = int(sys.argv[1]) if len(sys.argv) > 1 else 900

    # Второй аргумент — приближение. Нужен, чтобы проверять резкость на том же
    # масштабе, на котором она и разваливается: на общем плане мыла не видно.
    zoom = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0

    Image.fromarray(render(size, zoom), mode="RGB").save(OUTPUT_PATH)
    print(f"записано {OUTPUT_PATH} ({size}x{size}, приближение {zoom:g}x)")


if __name__ == "__main__":
    main()
