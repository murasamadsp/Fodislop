"""Офлайн-запекание карт планеты главного меню в equirect-текстуры.

Весь шум считается здесь, один раз, а не в шейдере каждый кадр. Шейдер
поверхности после этого умеет ровно одно: прочитать три карты и посветить.

Шум берётся от трёхмерного направления, а не от UV. Это принципиально: у
equirect-развёртки левый и правый край — одна и та же долгота, и любая функция
от UV даст на ней видимый шов. Функция от направления совпадает на стыке сама,
без подгонки, и по той же причине не расходится у полюсов.

Запуск:
    python3 scripts/generate_planet_maps.py
"""

from __future__ import annotations

import os

import numpy as np
from PIL import Image

OUTPUT_DIR = "Assets/Textures/UI"
WIDTH = 8192
HEIGHT = 4096

# --- Параметры поверхности ------------------------------------------------
# Значения перенесены из материала PlanetSurface.mat, чтобы запечённая картинка
# совпадала с тем видом, который уже был признан правильным.

CONTINENT_SCALE = 3.0
WARP_STRENGTH = 0.50
RIDGE_SCALE = 11.0
MOUNTAIN_HEIGHT = 0.28
DETAIL_SCALE = 140.0
DETAIL_STRENGTH = 0.20

# Ещё более мелкое зерно, ради резкости на приближении. При карте 4096 на 360
# градусов один тексель — около сотой доли градуса, и на подлёте экранный
# пиксель приходится примерно на тексель. Всё, что мельче макрорельефа, до
# этого масштаба просто не доживало: прежняя деталь давала 2% размаха высоты и
# в нормаль практически не попадала, отчего поверхность и читалась гладкой
# замазкой.
GRAIN_SCALE = 390.0
GRAIN_STRENGTH = 0.014

# Разброс шероховатости по зерну. Ровная шероховатость собирает блик в одно
# широкое пятно — ровно тот масляный отлив, от которого уходим; поломанная
# рассыпает его на множество мелких, и поверхность читается сухой.
ROUGHNESS_GRAIN = 0.13

# Разломы: мало октав и крупный масштаб.
#
# На четырёх октавах гребень ridged-шума распадается, и порог по нему даёт не
# линии, а сыпь равномерных точек по всему шару — планета получается в кори.
# Две октавы оставляют гребень длинным и связным, и порог режет из него уже
# протяжённые разломы.
CRACK_SCALE = 6.0
CRACK_OCTAVES = 2
CRACK_THRESHOLD = 0.845

# Рифты открыты не везде. Крупная маска оставляет несколько активных областей,
# между которыми кора холодная: равномерно раскиданные по глобусу разломы
# читаются как текстурный шум, а не как геология.
CRACK_REGION_SCALE = 1.6
CRACK_REGION_THRESHOLD = 0.12

CLOUD_SCALE = 2.6
CLOUD_WARP = 0.55
CLOUD_BANDS = 3.5
CLOUD_BAND_STRENGTH = 0.18
CLOUD_COVERAGE_BIAS = -0.04

CRATER_SCALE_MAJOR = 7.0
CRATER_DENSITY_MAJOR = 0.30
CRATER_DEPTH_MAJOR = 0.070

CRATER_SCALE_MINOR = 21.0
CRATER_DENSITY_MINOR = 0.38
CRATER_DEPTH_MINOR = 0.022

BASIN_LEVEL = 0.42
PEAK_LEVEL = 0.80

# Цвета в линейном пространстве.
#
# Разброс светлоты широкий намеренно: на узком ряду вся кора после свода
# экспозиции выходит одним тоном, и рельеф читается только штрихами по
# гребням.
BASALT_COLOR = np.array([0.088, 0.076, 0.064])
REGOLITH_COLOR = np.array([0.246, 0.190, 0.112])
CRUST_COLOR = np.array([0.445, 0.382, 0.238])
PEAK_COLOR = np.array([0.480, 0.430, 0.340])

ROUGHNESS_FLAT = 0.88
ROUGHNESS_STEEP = 0.66

# Наклон в единицах квантиля градиента, до которого растягивается карта.
# Единица означает наклон 45 градусов у самых крутых мест — верхняя половина
# 8-битного диапазона занята, ступенек квантования не видно.
NORMAL_SPAN = 1.15


# --- Шумовая база ---------------------------------------------------------
# Порт PlanetNoise.hlsl. Хеш тот же самый (pcg3d): обычный
# frac(sin(dot(p, k)) * 43758.5) на мелких октавах начинает повторяться и
# выдаёт вместо камня размазанную замазку — ровно та проблема, из-за которой
# hlsl-версия в своё время и переехала на pcg3d.


def _pcg3d(x: np.ndarray, y: np.ndarray, z: np.ndarray) -> np.ndarray:
    """pcg3d над целочисленной решёткой. Возвращает (..., 3) uint32."""
    v = np.stack((x, y, z), axis=-1).astype(np.uint32)

    v = v * np.uint32(1664525) + np.uint32(1013904223)
    v[..., 0] += v[..., 1] * v[..., 2]
    v[..., 1] += v[..., 2] * v[..., 0]
    v[..., 2] += v[..., 0] * v[..., 1]
    v ^= v >> np.uint32(16)
    v[..., 0] += v[..., 1] * v[..., 2]
    v[..., 1] += v[..., 2] * v[..., 0]
    v[..., 2] += v[..., 0] * v[..., 1]

    return v


def _hash_gradient(ix: np.ndarray, iy: np.ndarray, iz: np.ndarray) -> np.ndarray:
    h = _pcg3d(ix, iy, iz)
    return h.astype(np.float64) * (2.0 / 4294967295.0) - 1.0


def gradient_noise(p: np.ndarray) -> np.ndarray:
    """Градиентный шум по трём измерениям, выход примерно [-1, 1]."""
    i = np.floor(p)
    f = p - i

    # Квинтическое сглаживание: непрерывна и первая производная, и вторая.
    # На кубическом варианте стыки ячеек видны как сетка на нормали.
    u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0)

    ix = i[..., 0].astype(np.int64)
    iy = i[..., 1].astype(np.int64)
    iz = i[..., 2].astype(np.int64)

    total = np.zeros(p.shape[:-1], dtype=np.float64)
    for corner in range(8):
        ox = (corner >> 2) & 1
        oy = (corner >> 1) & 1
        oz = corner & 1

        g = _hash_gradient(ix + ox, iy + oy, iz + oz)
        d = f - np.array([ox, oy, oz], dtype=np.float64)

        wx = u[..., 0] if ox else 1.0 - u[..., 0]
        wy = u[..., 1] if oy else 1.0 - u[..., 1]
        wz = u[..., 2] if oz else 1.0 - u[..., 2]

        total += (wx * wy * wz) * np.sum(g * d, axis=-1)

    return np.clip(total * 1.4 * 0.5 + 0.5, 0.0, 1.0) * 2.0 - 1.0


# Смещение и нецелая лакунарность на каждой октаве. При точном шаге 2.0
# экстремумы соседних октав садятся на одни и те же узлы решётки, и в
# результате по всей карте проступает слабая, но хорошо читаемая сетка.
_LACUNARITY = 2.037
_FBM_OFFSET = np.array([19.31, 7.53, 13.77])
_RIDGE_OFFSET = np.array([5.17, 11.93, 3.71])


def fbm(p: np.ndarray, octaves: int) -> np.ndarray:
    """Обычный fBm, выход примерно [-1, 1]."""
    p = p.copy()
    total = np.zeros(p.shape[:-1], dtype=np.float64)
    amplitude = 0.5
    norm = 0.0

    for _ in range(octaves):
        total += amplitude * gradient_noise(p)
        norm += amplitude
        amplitude *= 0.5
        p = p * _LACUNARITY + _FBM_OFFSET

    return total / max(norm, 1e-4)


def ridged_fbm(p: np.ndarray, octaves: int) -> np.ndarray:
    """Ridged multifractal, выход [0, 1] с резкими гребнями у единицы.

    Обычный fBm умеет только округлые комья. Связные горные цепи и сети
    разломов даёт именно эта форма.
    """
    p = p.copy()
    total = np.zeros(p.shape[:-1], dtype=np.float64)
    amplitude = 0.5
    norm = 0.0
    prev = np.ones(p.shape[:-1], dtype=np.float64)

    for _ in range(octaves):
        r = 1.0 - np.abs(gradient_noise(p))
        r *= r

        # Вес октавы по предыдущей: детали садятся на уже существующие гребни,
        # а не рассыпаются равномерно. Без этого цепь читается как шум.
        r *= prev
        prev = np.clip(r * 2.0, 0.0, 1.0)

        total += amplitude * r
        norm += amplitude
        amplitude *= 0.5
        p = p * _LACUNARITY + _RIDGE_OFFSET

    return np.clip(total / max(norm, 1e-4), 0.0, 1.0)


# --- Поля поверхности -----------------------------------------------------


def elevation_base(dirs: np.ndarray) -> np.ndarray:
    """Тектоническая высота.

    Доменный варп здесь несущий, а не украшение: неискажённый fBm даёт
    изотропные кляксы, похожие на облака, сколько октав в него ни клади.
    Искажение точки выборки другим fBm вытягивает их в ветвящиеся провинции,
    которые и читаются как кора.
    """
    c = dirs * CONTINENT_SCALE

    warp = np.stack(
        (
            gradient_noise(c + np.array([17.1, 3.2, 8.9])),
            gradient_noise(c + np.array([43.7, 21.4, 2.6])),
            gradient_noise(c + np.array([91.3, 12.8, 33.1])),
        ),
        axis=-1,
    )

    continents = fbm(c + warp * WARP_STRENGTH, 5)
    elev = np.clip(continents * 0.5 + 0.5, 0.0, 1.0)

    # Хребты поднимаются только на уже приподнятой коре — тогда горы образуют
    # пояса вдоль границ провинций, а не крапят котловины.
    uplift = _smoothstep(0.40, 0.78, elev)
    ranges = ridged_fbm(dirs * RIDGE_SCALE, 5)

    detail = fbm(dirs * DETAIL_SCALE, 3) * DETAIL_STRENGTH * 0.22
    detail = detail + fbm(dirs * GRAIN_SCALE, 2) * GRAIN_STRENGTH

    # Два размера ударов: редкие крупные бассейны и частая мелкая оспина.
    # Один масштаб дал бы поле одинаковых лунок — самый узнаваемый признак
    # процедурной поверхности.
    craters = (
        crater_field(dirs, CRATER_SCALE_MAJOR, CRATER_DENSITY_MAJOR, 17) * CRATER_DEPTH_MAJOR
        + crater_field(dirs, CRATER_SCALE_MINOR, CRATER_DENSITY_MINOR, 613) * CRATER_DEPTH_MINOR)

    return elev + ranges * uplift * MOUNTAIN_HEIGHT + detail + craters


def _cell_hash(cell: np.ndarray, salt: int) -> np.ndarray:
    """Три числа в [0, 1) на ячейку решётки."""
    h = _pcg3d(
        cell[..., 0].astype(np.int64) + salt,
        cell[..., 1].astype(np.int64) + salt,
        cell[..., 2].astype(np.int64) + salt,
    )
    return h.astype(np.float64) / 4294967295.0


def _crater_profile(x: np.ndarray) -> np.ndarray:
    """Профиль кратера по расстоянию от центра в долях радиуса.

    Чаша, поднятый вал и выброс за ним. Валом всё и держится: без него
    углубление читается как тёмное пятно, а не как удар, — по краю кратера
    свет ловит именно кольцевой гребень.
    """
    bowl = -(1.0 - np.clip(x / 0.74, 0.0, 1.0) ** 2)
    rim = np.exp(-(((x - 0.90) / 0.14) ** 2)) * 0.85
    ejecta = np.exp(-(((x - 1.20) / 0.40) ** 2)) * 0.16
    return np.where(x < 1.9, bowl + rim + ejecta, 0.0)


def crater_field(dirs: np.ndarray, scale: float, density: float, salt: int) -> np.ndarray:
    """Слой ударных кратеров на решётке с одним центром в ячейке.

    Кратеры добавляются в высоту, а не в альбедо: тогда они сами проявятся и в
    нормали, и в наклоне, а через наклон — в раскладке пород. Нарисованные
    прямо в цвете, они были бы плоскими кольцами и разъехались бы с рельефом
    на любой правке освещения.
    """
    p = dirs * scale
    base = np.floor(p)

    total = np.zeros(p.shape[:-1], dtype=np.float64)
    for ox in (-1, 0, 1):
        for oy in (-1, 0, 1):
            for oz in (-1, 0, 1):
                cell = base + np.array([ox, oy, oz], dtype=np.float64)

                jitter = _cell_hash(cell, salt)
                shape = _cell_hash(cell, salt + 977)

                # Не в каждой ячейке кратер, и радиусы разные: равномерная
                # сетка одинаковых лунок читается как узор, а не как поверхность.
                present = shape[..., 0] < density
                radius = 0.22 + shape[..., 1] * 0.40
                weight = 0.35 + shape[..., 2] * 0.65

                delta = p - (cell + jitter)
                distance = np.linalg.norm(delta, axis=-1) / radius

                total += np.where(present, _crater_profile(distance) * weight, 0.0)

    return total


def fault_field(dirs: np.ndarray) -> np.ndarray:
    """Сеть разломов: и сами рифты, и подсветка породы рядом с ними."""
    return ridged_fbm(dirs * CRACK_SCALE, CRACK_OCTAVES)


def crack_region(dirs: np.ndarray) -> np.ndarray:
    """Крупная маска активных областей — где кора вообще вскрыта."""
    region = fbm(dirs * CRACK_REGION_SCALE, 2)
    return _smoothstep(CRACK_REGION_THRESHOLD, CRACK_REGION_THRESHOLD + 0.30, region)


def cloud_field(dirs: np.ndarray) -> np.ndarray:
    """Покрытие облачной палубы.

    Доменный варп даёт вихревую структуру, зональные полосы — кориолисовы
    пояса, высокочастотные волокна — перистые полосы.
    """
    p = dirs * CLOUD_SCALE

    warp = np.stack(
        (
            gradient_noise(p + np.array([11.3, 5.1, 27.7])),
            gradient_noise(p + np.array([47.9, 63.2, 8.4])),
            gradient_noise(p + np.array([83.1, 19.6, 51.3])),
        ),
        axis=-1,
    )

    coverage = fbm(p + warp * CLOUD_WARP, 4)

    wobble = gradient_noise(p * 0.5) * 0.25
    coverage += np.sin((dirs[..., 1] + wobble) * CLOUD_BANDS * np.pi + 1.1) * CLOUD_BAND_STRENGTH
    coverage += gradient_noise(p * 3.2 + warp * 0.3) * 0.12

    return np.clip(coverage * 0.5 + 0.5 + CLOUD_COVERAGE_BIAS, 0.0, 1.0)


def _smoothstep(edge0: float, edge1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _mix(a, b, t):
    """lerp, где t — карта, а a и b могут быть как картами, так и цветами."""
    t = t[..., None] if np.ndim(t) == 2 and np.ndim(a) >= 1 and np.shape(a)[-1:] == (3,) else t
    return a + (b - a) * t


# --- Развёртка ------------------------------------------------------------


def equirect_directions(
    width: int, height: int, row_start: int = 0, row_end: int | None = None
) -> tuple[np.ndarray, np.ndarray]:
    """Единичные направления для центров текселей равнопромежуточной развёртки.

    Считает не всю карту, а полосу строк [row_start, row_end): на 4K полный
    набор промежуточных массивов занимает единицы гигабайт, и полосами это
    сводится к десяткам мегабайт без изменения результата — направление
    каждого текселя не зависит от того, с какими соседями его посчитали.

    Возвращает также широту: она нужна отдельно, потому что горизонтальный шаг
    в текселях соответствует всё меньшей дуге по мере приближения к полюсу, и
    без этой поправки градиент высоты у полюсов раздувается.
    """
    if row_end is None:
        row_end = height

    u = (np.arange(width, dtype=np.float64) + 0.5) / width
    v = (np.arange(row_start, row_end, dtype=np.float64) + 0.5) / height

    longitude = (u - 0.5) * 2.0 * np.pi
    latitude = (0.5 - v) * np.pi

    lon, lat = np.meshgrid(longitude, latitude)
    cos_lat = np.cos(lat)

    dirs = np.stack(
        (cos_lat * np.cos(lon), np.sin(lat), cos_lat * np.sin(lon)),
        axis=-1,
    )
    return dirs, lat


def tangent_gradients(
    elevation: np.ndarray,
    latitude: np.ndarray,
    map_width: int,
    map_height: int,
) -> tuple[np.ndarray, np.ndarray]:
    """Производные высоты по касательным осям, приведённые к разрешению.

    Долгота замкнута, и разность по ней берётся через np.roll, а не через
    np.gradient. np.gradient на краях массива переходит на одностороннюю
    разность — то есть ровно на стыке 0 и W-1 считает наклон по половине шага и
    по соседу не с той стороны. Поля шума на этом стыке непрерывны, а карта
    нормалей — нет, и через наклон разрыв протекает дальше в альбедо и
    шероховатость.

    Производная по долготе делится на cos(широты): один тексель у экватора и
    один у полюса — разные расстояния по поверхности, и без деления рельеф у
    полюсов вытягивается в вертикальные полосы.

    Множители по разрешению делают результат от него независимым: на вдвое
    более мелкой сетке разность на тексель вдвое меньше, и без поправки та же
    планета давала бы разный рельеф на разных размерах карты.

    Размеры передаются аргументами, а не берутся из elevation.shape. Функция
    вызывается на ПОЛОСЕ: ширина у полосы полная, а высота — сто с небольшим
    строк вместо тысяч, и взятая из формы поправка занижала производную по
    широте примерно в тридцать раз. В карте нормалей это выглядело как 39
    различных уровней по Y против 251 по X — то есть почти пустой канал.
    """
    d_lon = (np.roll(elevation, -1, axis=1) - np.roll(elevation, 1, axis=1)) * 0.5
    d_lat = np.gradient(elevation, axis=0)

    # У самих полюсов cos обращается в ноль. Зажим на 1e-2 формально спасал от
    # бесконечности, но оставлял у полюсов производные в сотню раз больше
    # обычных: общий масштаб нормировки задавался ими, и на весь остальной шар
    # приходилась узкая часть диапазона — по Y карта нормалей использовала 49
    # уровней из 255 против 246 по X. 0.25 отсекает вырождение, не трогая
    # ничего южнее 75 градусов.
    metric = np.maximum(np.cos(latitude), 0.25)

    return (
        (d_lon / metric) * (map_width / 512.0),
        d_lat * (map_height / 256.0),
    )


def surface_normal(dx: np.ndarray, dy: np.ndarray, grad_hi: float) -> np.ndarray:
    """Нормаль в касательном пространстве, растянутая на весь 8-битный диапазон.

    Без нормировки на grad_hi здесь была главная поломка карты. Наклон на шаре
    такого радиуса лежит в районе тысячных, нормаль отклоняется от вертикали на
    сотые доли, и после кодирования n*0.5+0.5 на все отклонения приходится
    порядка шести уровней из 255. Карта нормалей выходила почти пустой, а
    единственным, что в ней читалось, — ступеньки квантования: шар покрывался
    плоскими многогранными фасетками, которые легко принять за грани меша.

    Деление на квантиль градиента приводит типовой наклон к единице, и
    диапазон используется целиком. Видимую силу рельефа задаёт уже
    _NormalStrength в шейдере, а не амплитуда, случайно получившаяся из шума.
    """
    scale = NORMAL_SPAN / max(grad_hi, 1e-9)
    normal = np.stack((-dx * scale, dy * scale, np.ones_like(dx)), axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return normal


# --- Сборка карт ----------------------------------------------------------


def build_albedo(elevation: np.ndarray, slope: np.ndarray, province: np.ndarray,
                 rift: np.ndarray, hue: np.ndarray, polar: np.ndarray) -> np.ndarray:
    """Породы раскладываются по наклону и высоте, а не по отдельному шуму.

    Один и тот же шум, разведённый на цвет, шероховатость и свечение, — это и
    есть то, что выдаёт процедурную кашу: все три канала меняются синхронно, и
    поверхность читается как крашеный пластик. Здесь цвет следует за рельефом.

    Высота и наклон приходят уже нормированными в 0..1 по фактическому
    распределению поля (см. probe_terrain_range). Абсолютные пороги здесь не
    работают: у наклона почти вся масса прижата к нулю, и заданный числом
    порог либо не срабатывает нигде, либо срабатывает везде — в первом случае
    шар выходит одноцветным.
    """
    albedo = _mix(CRUST_COLOR, REGOLITH_COLOR, _smoothstep(0.22, 0.66, slope))
    albedo = _mix(albedo, BASALT_COLOR, _smoothstep(0.58, 1.00, slope))

    basin = 1.0 - _smoothstep(BASIN_LEVEL - 0.14, BASIN_LEVEL + 0.14, elevation)
    albedo = _mix(albedo, CRUST_COLOR * 0.58, basin * (1.0 - _smoothstep(0.45, 0.80, slope)))

    albedo = _mix(albedo, PEAK_COLOR, _smoothstep(PEAK_LEVEL - 0.18, PEAK_LEVEL + 0.20, elevation))

    # Крупные провинции: разные области коры отличаются по светлоте и теплу.
    # Без этого даже правильно разложенные породы дают ровное поле — с орбиты
    # читается именно разница масштаба в тысячи километров, а не зерно.
    # Порода вдоль разлома темнее: это провал в коре, и днём он должен
    # читаться трещиной. Без затемнения рифт существует только как аддитивное
    # свечение — на освещённой стороне оно тонет в ярком песке, и от разломов
    # остаются одни горящие кляксы у самого терминатора.
    albedo = albedo * (1.0 - rift[..., None] * 0.72)

    tint = 0.74 + province[..., None] * 0.48
    albedo = albedo * tint

    # Сдвиг к составу породы. Смешивается по светлоте самого альбедо, а не
    # подменяет его: иначе рельеф, набранный выше по высоте и наклону, стёрся
    # бы плоской заливкой цвета.
    weights = np.array([0.2126, 0.7152, 0.0722])
    luminance = np.sum(albedo * weights, axis=-1, keepdims=True)

    composition = _mix(OXIDE_COLOR, EVAPORITE_COLOR, hue)
    composition_luminance = np.sum(composition * weights, axis=-1, keepdims=True)
    composition = composition * (luminance / np.maximum(composition_luminance, 1e-5))

    albedo = _mix(albedo, composition, HUE_STRENGTH)

    # Шапки кладутся последними: лёд лежит поверх любой породы.
    albedo = _mix(albedo, POLAR_COLOR, polar * 0.88)

    return np.clip(albedo, 0.0, 1.0)


def build_emission(dirs: np.ndarray, elevation: np.ndarray, fault: np.ndarray) -> np.ndarray:
    """Маска рифтов.

    Рифты открываются в низинах: на гребнях кора толще, и разлом до магмы
    оттуда не достаёт. Разрыв линии вдоль её длины не даёт сети выглядеть
    нарисованной одним росчерком.
    """
    # Гребень ridged-шума — это связная кривая, и порог по нему уже даёт
    # линию. Дальше её нельзя ни возводить в квадрат, ни умножать на маски,
    # уходящие в ноль: и то и другое режет линию на куски, и вместо разлома
    # остаются отдельные яркие точки. Поэтому маски ниже только меняют яркость
    # вдоль линии, не обнуляя её.
    crack = _smoothstep(CRACK_THRESHOLD, 1.0, fault)

    # Подъём в степень поджимает линию к ядру: трещина, а не дымный след.
    # Степень безопасна там, где обнуляющая маска нет — она нигде не превращает
    # ненулевое значение в ноль и потому не рвёт трассу на куски.
    crack = np.power(crack, 1.55)

    # Глубже в котловинах кора тоньше, и разлом светит сильнее. Но и на
    # возвышенности он не гаснет полностью — иначе трасса обрывается там, где
    # пересекает хребет.
    depth = 1.0 - _smoothstep(BASIN_LEVEL - 0.06, BASIN_LEVEL + 0.24, elevation)
    depth = 0.34 + depth * 0.66

    # Неоднородность вдоль длины: местами разлом раскалён, местами почти остыл.
    breakup = np.clip(fbm(dirs * (CRACK_SCALE * 2.2), 3) * 0.5 + 0.5, 0.0, 1.0)
    breakup = 0.40 + _smoothstep(0.28, 0.92, breakup) * 0.60

    # А вот область — единственная маска, которой обнулять можно и нужно: за
    # пределами активных зон кора действительно холодная.
    return np.clip(crack * depth * breakup * crack_region(dirs), 0.0, 1.0)


PROVINCE_SCALE = 1.9

# Второе, независимое поле — под цвет. Отдельное от светлоты намеренно: если
# оттенок ведёт то же поле, что и яркость, светлые области всегда одного тона,
# и планета читается как один материал под разным светом, а не как разные
# породы.
HUE_SCALE = 1.35

# Крайние породы по составу: окисленная (ржавая) и выветренная соляная.
OXIDE_COLOR = np.array([0.390, 0.132, 0.048])
EVAPORITE_COLOR = np.array([0.505, 0.462, 0.330])
HUE_STRENGTH = 0.42

# Полярные шапки. Край рвётся шумом: ровная широтная граница читается
# нарисованной линией, а не льдом.
POLAR_LATITUDE = 1.40
POLAR_EDGE = 0.21
POLAR_NOISE_SCALE = 7.0
POLAR_COLOR = np.array([0.620, 0.640, 0.660])


def province_field(dirs: np.ndarray) -> np.ndarray:
    """Крупные области коры. Только для светлоты, не для рельефа."""
    return np.clip(fbm(dirs * PROVINCE_SCALE, 3) * 0.5 + 0.5, 0.0, 1.0)


def hue_field(dirs: np.ndarray) -> np.ndarray:
    """Состав породы: 0 — окисленная, 1 — выветренная соляная."""
    return _smoothstep(0.30, 0.70, np.clip(fbm(dirs * HUE_SCALE, 3) * 0.5 + 0.5, 0.0, 1.0))


def polar_cap(dirs: np.ndarray, latitude: np.ndarray) -> np.ndarray:
    """Маска шапок с рваным краем."""
    edge = fbm(dirs * POLAR_NOISE_SCALE, 3) * 0.16
    return _smoothstep(POLAR_LATITUDE - POLAR_EDGE, POLAR_LATITUDE, np.abs(latitude) + edge)


def probe_terrain_range() -> dict[str, float]:
    """Границы нормировки, снятые на грубой сетке.

    Поля непрерывны и от разрешения не зависят: градиенты приведены к
    разрешению в tangent_gradients, высота — функция направления. Поэтому
    квантили с сетки 512x256 верны и для 4K, и полный проход ради одних только
    границ гнать не нужно.
    """
    dirs, latitude = equirect_directions(512, 256)
    elevation = elevation_base(dirs)
    dx, dy = tangent_gradients(elevation, latitude, 512, 256)
    magnitude = np.hypot(dx, dy)

    # Края отрезаются квантилями, а не min/max: одиночный выброс на пике или у
    # полюса растянул бы шкалу так, что вся остальная карта села бы в
    # несколько процентов диапазона — ровно из-за этого шар выходил одноцветным.
    return {
        "elev_lo": float(np.quantile(elevation, 0.02)),
        "elev_hi": float(np.quantile(elevation, 0.98)),
        "grad_hi": float(np.quantile(magnitude, 0.99)),
        "slope_lo": float(np.quantile(magnitude, 0.05)),
        "slope_hi": float(np.quantile(magnitude, 0.995)),
    }


def _normalize(x: np.ndarray, lo: float, hi: float) -> np.ndarray:
    return np.clip((x - lo) / max(hi - lo, 1e-6), 0.0, 1.0)


def build_roughness(slope: np.ndarray, emission: np.ndarray, grain: np.ndarray) -> np.ndarray:
    """Плоское — пыль, крутое — голая порода, рифты чуть глаже от расплава.

    Диапазон сознательно узкий и весь в матовой половине: именно низкая
    шероховатость превращает планету в миску с маслом.
    """
    roughness = _mix(ROUGHNESS_FLAT, ROUGHNESS_STEEP, _smoothstep(0.25, 0.80, slope))
    roughness = roughness + (grain - 0.5) * ROUGHNESS_GRAIN
    return np.clip(roughness - emission * 0.10, 0.55, 0.95)


def _to_bytes(data: np.ndarray) -> np.ndarray:
    return np.clip(np.rint(data * 255.0), 0, 255).astype(np.uint8)


def _linear_to_srgb(c: np.ndarray) -> np.ndarray:
    """Альбедо пишется в sRGB: Unity импортирует его как sRGB-текстуру."""
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0.0), 1.0 / 2.4) - 0.055)


BAND_ROWS = 128


def main() -> None:
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print("Снимаю границы нормировки на грубой сетке...")
    stats = probe_terrain_range()
    print(f"  высота {stats['elev_lo']:.3f}..{stats['elev_hi']:.3f}"
          f"  наклон {stats['slope_lo']:.5f}..{stats['slope_hi']:.5f}"
          f"  масштаб градиента {stats['grad_hi']:.5f}")

    print(f"Сетка {WIDTH}x{HEIGHT}, считаю поля полосами по {BAND_ROWS} строк...")

    albedo_out = np.empty((HEIGHT, WIDTH, 3), dtype=np.uint8)
    normal_out = np.empty((HEIGHT, WIDTH, 3), dtype=np.uint8)
    packed_out = np.empty((HEIGHT, WIDTH, 3), dtype=np.uint8)

    elev_lo, elev_hi = np.inf, -np.inf
    rough_lo, rough_hi = np.inf, -np.inf
    rift_texels = 0
    cloud_texels = 0

    for start in range(0, HEIGHT, BAND_ROWS):
        end = min(start + BAND_ROWS, HEIGHT)

        # Полоса считается с запасом в одну строку сверху и снизу: np.gradient
        # на краю переходит на односторонюю разность, и без запаса на каждом
        # стыке полос осталась бы горизонтальная линия в карте нормалей.
        pad_start = max(start - 1, 0)
        pad_end = min(end + 1, HEIGHT)
        head = start - pad_start
        tail = head + (end - start)

        dirs, latitude = equirect_directions(WIDTH, HEIGHT, pad_start, pad_end)

        elevation = elevation_base(dirs)
        dx, dy = tangent_gradients(elevation, latitude, WIDTH, HEIGHT)
        normal = surface_normal(dx, dy, stats["grad_hi"])

        # Наклон берётся из тех же производных, что и нормаль. Отдельный проход
        # конечных разностей со временем разошёлся бы с ней, и маска пород
        # перестала бы совпадать с рельефом.
        slope = _normalize(np.hypot(dx, dy), stats["slope_lo"], stats["slope_hi"])
        elev_n = _normalize(elevation, stats["elev_lo"], stats["elev_hi"])

        fault = fault_field(dirs)
        emission = build_emission(dirs, elev_n, fault)
        polar = polar_cap(dirs, latitude)
        albedo = build_albedo(
            elev_n, slope, province_field(dirs), emission, hue_field(dirs), polar)

        # Лёд глаже породы, но не зеркало: мокрая шапка вернула бы ровно тот
        # масляный блик, ради ухода от которого вся шероховатость и зажата.
        grain = np.clip(fbm(dirs * GRAIN_SCALE, 2) * 0.5 + 0.5, 0.0, 1.0)
        roughness = np.clip(build_roughness(slope, emission, grain) - polar * 0.16, 0.5, 0.95)
        clouds = cloud_field(dirs)

        albedo_out[start:end] = _to_bytes(_linear_to_srgb(albedo[head:tail]))
        normal_out[start:end] = _to_bytes(normal[head:tail] * 0.5 + 0.5)
        packed_out[start:end] = _to_bytes(
            np.stack((roughness[head:tail], emission[head:tail], clouds[head:tail]), axis=-1)
        )

        elev_lo = min(elev_lo, float(elevation[head:tail].min()))
        elev_hi = max(elev_hi, float(elevation[head:tail].max()))
        rough_lo = min(rough_lo, float(roughness[head:tail].min()))
        rough_hi = max(rough_hi, float(roughness[head:tail].max()))
        rift_texels += int((emission[head:tail] > 0.02).sum())
        cloud_texels += int((clouds[head:tail] > 0.5).sum())

        print(f"  строки {start:5d}..{end:5d}")

    total = WIDTH * HEIGHT
    print(f"  высота        {elev_lo:.3f} .. {elev_hi:.3f}")
    print(f"  шероховатость {rough_lo:.3f} .. {rough_hi:.3f}")
    print(f"  рифты         покрытие {rift_texels / total * 100:.2f}%")
    print(f"  облака        покрытие {cloud_texels / total * 100:.2f}%")

    outputs = (
        ("planet_albedo.png", albedo_out),
        ("planet_normal.png", normal_out),
        ("planet_packed.png", packed_out),
    )
    for name, data in outputs:
        path = os.path.join(OUTPUT_DIR, name)
        Image.fromarray(data, mode="RGB").save(path)
        print(f"  записано {path} ({os.path.getsize(path) / 1024 / 1024:.2f} МБ)")


if __name__ == "__main__":
    main()
