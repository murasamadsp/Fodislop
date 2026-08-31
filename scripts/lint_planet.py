"""Визуальный линтер планеты: измеряет то, на что жалуются глазами.

Существует потому, что «мыло», «масло» и «клякса» — это не вкусовые претензии,
а измеримые свойства картинки, и без измерения их правка превращается в
угадывание: правишь одно, ломаешь другое, и заметно это только на следующем
скриншоте.

Что меряется и почему именно так:

    РЕЗКОСТЬ. Средний модуль лапласиана, нормированный на разброс яркости.
    Нормировка обязательна: без неё тёмная картинка всегда «мягче» светлой, и
    метрика мерила бы экспозицию, а не резкость.

    СПЕКТРАЛЬНЫЙ ПОТОЛОК. Доля Найквиста, выше которой в картинке уже нет
    энергии. У честно отрисованного кадра спектр доходит до края; у
    растянутого — обрывается там, где кончалось исходное разрешение. Это и
    отличает «мыло от растяжения» от «мыла от гладкой поверхности», которые на
    глаз выглядят одинаково, а лечатся противоположно.

    ТЕКСЕЛЬНЫЙ БЮДЖЕТ. Сколько текселей карты приходится на пиксель экрана.
    Меньше единицы — карта увеличивается, и никакая правка шейдера резкости не
    вернёт.

    ФОРМА РИФТОВ. Изопериметрическое отношение 4*pi*A/P^2: у круга единица, у
    длинной линии почти ноль. Ровно этим отличается клякса от разлома.

    ЛОКАЛЬНЫЙ КОНТРАСТ. Разброс яркости внутри мелких блоков освещённой части.
    Масляная поверхность — это большой перепад по всему диску при почти нулевом
    внутри блока: блик размазан одним пятном вместо множества мелких.

    КВАНТОВАНИЕ КАРТ. Число различных уровней в канале. Именно эта проверка
    поймала бы, что карта нормалей несёт шесть уровней из 255.

Запуск:
    python3 scripts/lint_planet.py
"""

from __future__ import annotations

import sys

import numpy as np
from PIL import Image

sys.path.insert(0, "scripts")
import preview_planet  # noqa: E402

TEXTURE_DIR = "Assets/Textures/UI"

# Порог по каждой метрике и сторона, с которой он считается нарушенным.
# Числа не абсолютные: они отражают, где начинается видимая глазом деградация
# на этой сцене, и подлежат правке вместе с ней.
THRESHOLDS = {
    "sharpness_wide": (0.055, "min"),
    "sharpness_zoom": (0.045, "min"),
    "spectral_cutoff": (0.55, "min"),
    "texels_per_pixel": (1.0, "min"),
    "rift_elongation": (0.30, "max"),
    "local_contrast": (0.030, "min"),
    "normal_levels_x": (128, "min"),
    "normal_levels_y": (128, "min"),
    "roughness_levels": (64, "min"),

    # Бюджет видеопамяти. Планета живёт только в главном меню, но пока она
    # загружена — эти мегабайты заняты, и молча расти им нельзя.
    "vram_maps_mb": (140.0, "max"),
    "vram_targets_mb": (60.0, "max"),

    # Пиксели одного статичного рендера. На кадр планета не стоит ничего:
    # кадр пересчитывается только при смене кадрирования. Но во время подлёта
    # он считается каждый кадр, и вот этот размер там и платится.
    "static_render_mpx": (6.0, "max"),
}

# Совпадает с MaxTargetSize и форматом целей из MenuSceneryController.
RENDER_TARGET_SIDE = 3072
RENDER_TARGET_ASPECT = 0.55


def luma(rgb: np.ndarray) -> np.ndarray:
    return np.sum(rgb.astype(np.float64) / 255.0 * np.array([0.2126, 0.7152, 0.0722]), axis=-1)


def sharpness(image: np.ndarray, mask: np.ndarray) -> float:
    """Средний модуль лапласиана, нормированный на разброс яркости."""
    y = luma(image)
    laplacian = (
        -4.0 * y
        + np.roll(y, 1, 0) + np.roll(y, -1, 0)
        + np.roll(y, 1, 1) + np.roll(y, -1, 1))

    inner = mask & np.roll(mask, 1, 0) & np.roll(mask, -1, 0) & np.roll(mask, 1, 1) & np.roll(mask, -1, 1)
    if inner.sum() < 100:
        return 0.0

    spread = y[inner].std()
    return float(np.abs(laplacian[inner]).mean() / max(spread, 1e-6))


def spectral_cutoff(image: np.ndarray, mask: np.ndarray) -> float:
    """Доля Найквиста, до которой в картинке ещё есть энергия.

    Берётся квадратный кусок внутри диска: окно по всему кадру внесло бы в
    спектр край силуэта, а он — самая сильная высокая частота в картинке и
    забил бы собой то, что меряем.
    """
    rows = np.where(mask.any(axis=1))[0]
    cols = np.where(mask.any(axis=0))[0]
    if rows.size == 0 or cols.size == 0:
        return 0.0

    cy = (rows[0] + rows[-1]) // 2
    cx = (cols[0] + cols[-1]) // 2
    half = min(rows[-1] - rows[0], cols[-1] - cols[0]) // 4
    if half < 32:
        return 0.0

    patch = luma(image[cy - half:cy + half, cx - half:cx + half])
    patch = patch - patch.mean()

    # Окно Ханна: без него разрыв на краях куска даёт крест в спектре и
    # ложную энергию на всех частотах.
    window = np.hanning(patch.shape[0])[:, None] * np.hanning(patch.shape[1])[None, :]
    spectrum = np.abs(np.fft.fftshift(np.fft.fft2(patch * window)))

    h, w = spectrum.shape
    yy, xx = np.mgrid[0:h, 0:w]
    radius = np.hypot(yy - h / 2, xx - w / 2).astype(np.int64)
    nyquist = min(h, w) // 2

    profile = np.bincount(radius.ravel(), spectrum.ravel()) / np.maximum(np.bincount(radius.ravel()), 1)
    profile = profile[:nyquist]
    if profile.size < 8:
        return 0.0

    # Потолок — там, где энергия падает ниже сотой доли от низкочастотной.
    reference = profile[1:5].mean()
    above = np.where(profile > reference * 0.01)[0]
    return float(above.max() / nyquist) if above.size else 0.0


def local_contrast(image: np.ndarray, mask: np.ndarray, block: int = 12) -> float:
    """Средний разброс яркости внутри блоков — противоположность масляности."""
    y = luma(image)
    h, w = y.shape
    h -= h % block
    w -= w % block

    tiles = y[:h, :w].reshape(h // block, block, w // block, block)
    covered = mask[:h, :w].reshape(h // block, block, w // block, block).all(axis=(1, 3))

    deviations = tiles.std(axis=(1, 3))
    return float(deviations[covered].mean()) if covered.any() else 0.0


def rift_elongation(packed: np.ndarray, threshold: int = 24) -> float:
    """4*pi*A/P^2 по маске свечения: единица — круги, около нуля — линии."""
    mask = packed[..., 1] > threshold
    area = int(mask.sum())
    if area < 64:
        return 1.0

    # Периметр — число текселей маски, у которых есть сосед вне её.
    neighbours = (
        np.roll(mask, 1, 0) & np.roll(mask, -1, 0)
        & np.roll(mask, 1, 1) & np.roll(mask, -1, 1))
    perimeter = int((mask & ~neighbours).sum())
    if perimeter == 0:
        return 1.0

    return float(min(4.0 * np.pi * area / (perimeter * perimeter), 1.0))


def texels_per_pixel(render_size: int, zoom: float, map_width: int) -> float:
    """Сколько текселей карты приходится на пиксель кадра.

    Видимая доля сферы считается по угловому размеру планеты с текущей
    дистанции: с конечного расстояния видно меньше полусферы, и брать половину
    было бы завышением.
    """
    distance = preview_planet.CAMERA_DISTANCE
    visible_half_angle = np.arccos(1.0 / distance)
    visible_fraction = visible_half_angle / np.pi

    texels_across = map_width * visible_fraction / max(zoom, 1e-6)
    return float(texels_across / render_size)


def check(name: str, value: float, unit: str = "") -> bool:
    limit, side = THRESHOLDS[name]
    ok = value >= limit if side == "min" else value <= limit
    arrow = ">=" if side == "min" else "<="
    print(f"  {'OK  ' if ok else 'ПЛОХО'}  {name:18s} {value:8.4f}{unit}  (нужно {arrow} {limit})")
    return ok


def main() -> None:
    size = 900
    failures = 0

    print("Рендер общего плана...")
    wide = preview_planet.render(size, 1.0)
    print("Рендер приближения 3.2x...")
    zoomed = preview_planet.render(size, 3.2)

    # Фон исключается из всех метрик: его ровная заливка занизила бы и
    # резкость, и контраст, а его край — задрал бы спектр.
    background = np.array([0.02, 0.03, 0.06])
    background_byte = np.rint(background * 255.0)
    wide_mask = np.any(np.abs(wide - background_byte) > 2, axis=-1)
    zoom_mask = np.ones(zoomed.shape[:2], dtype=bool)

    normal = np.asarray(Image.open(f"{TEXTURE_DIR}/planet_normal.png"))
    packed = np.asarray(Image.open(f"{TEXTURE_DIR}/planet_packed.png"))
    map_width = normal.shape[1]

    print(f"\nКарты {map_width}x{normal.shape[0]}\n")

    print("Резкость и разрешение:")
    failures += not check("sharpness_wide", sharpness(wide, wide_mask))
    failures += not check("sharpness_zoom", sharpness(zoomed, zoom_mask))
    failures += not check("spectral_cutoff", spectral_cutoff(zoomed, zoom_mask), " от Найквиста")
    failures += not check("texels_per_pixel", texels_per_pixel(size, 3.2, map_width), " текс/пикс")

    print("\nПоверхность:")
    failures += not check("local_contrast", local_contrast(zoomed, zoom_mask))

    print("\nРифты:")
    failures += not check("rift_elongation", rift_elongation(packed))

    print("\nКвантование карт:")
    failures += not check("normal_levels_x", float(len(np.unique(normal[..., 0]))), " уровней")
    failures += not check("normal_levels_y", float(len(np.unique(normal[..., 1]))), " уровней")
    failures += not check("roughness_levels", float(len(np.unique(packed[..., 0]))), " уровней")

    print("\nБюджет GPU:")

    # BC7 — байт на тексель; мипы добавляют треть.
    maps_bytes = sum(
        np.asarray(Image.open(f"{TEXTURE_DIR}/{name}.png")).shape[0]
        * np.asarray(Image.open(f"{TEXTURE_DIR}/{name}.png")).shape[1]
        * 1.3333
        for name in ("planet_albedo", "planet_normal", "planet_packed"))
    failures += not check("vram_maps_mb", maps_bytes / (1024 * 1024), " МБ")

    target_h = int(RENDER_TARGET_SIDE * RENDER_TARGET_ASPECT)
    # Цвет с глубиной у камеры плюс отдельная цель под результат блита.
    targets_bytes = RENDER_TARGET_SIDE * target_h * (4 + 2) + RENDER_TARGET_SIDE * target_h * 4
    failures += not check("vram_targets_mb", targets_bytes / (1024 * 1024), " МБ")
    failures += not check(
        "static_render_mpx", RENDER_TARGET_SIDE * target_h / 1e6, " млн пикс")

    print(f"\n{'ВСЁ ЧИСТО' if failures == 0 else f'НАРУШЕНИЙ: {failures}'}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
