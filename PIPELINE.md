# PIPELINE.md — Графический пайплайн Fodinae

Графика Fodinae — это 2D-рендеринг на Unity URP с процедурным террейном, глобальным освещением Radiance Cascades на GPU Compute и кастомным пост-процессингом.

---

## 1. Схема движения кадра

```text
[1. Сеть / Чанки]  →  MapStorage (32x32 чанки, RLE-кэш)
       ↓
[2. CPU Геометрия] →  TerrainCellCache → Precalculator → BackgroundFloodFill → TerrainMeshBuilder
       ↓
[3. Растеризация]  →  Pass "LightingMaterialField" → _MaterialField (окклюзия) + _EmissionField (свет)
       ↓
[4. GPU Освещение] →  WorldLighting.compute (Normals → Cascades → Unified Resolve & Composite)
       ↓
[5. Отрисовка]     →  Terrain.shader (сэмплирует _WorldLightTexture) + Роботы + Сущности
       ↓
[6. Пост-процесс]  →  PostProcessRendererFeature → PostProcess.compute (Bloom → ACES ToneMap → Grain)
       ↓
[7. Интерфейс]     →  UI Toolkit (UIDocument, ScreenToPanel координаты)
```

---

## 2. Домены пайплайна

### 1. Данные мира (World & Streaming)

- **Файлы:** `MapStorage.cs`, `WorldLayer.cs`
- **Задача:** Хранение клеток мира в 32×32 чанках с RLE-сжатием на диске (`.mapb`) и в RAM.

### 2. Сборка меша (Terrain Mesh)

- **Файлы:** `TerrainRenderer.cs`, `TerrainCellCache.cs`, `TerrainMeshBuilder.cs`, `BackgroundFloodFill.cs`
- **Задача:**
  1. Кэш квантуется шагом 8 вокруг камеры.
  2. `TerrainPrecalculator` считает 47-битный авто-тайлинг и рельеф.
  3. `BackgroundFloodFill` волновым алгоритмом строит заднюю стену в пустотах.
  4. `TerrainMeshBuilder` собирает один меш с 7 UV-каналами (~45–50k вершин).

### 3. Растеризация полей (Material & Emission)

- **Файлы:** `Terrain.shader` (Pass `LightingMaterialField`)
- **Задача:** Рендерит геометрию в две вспомогательные текстуры:
  - `_MaterialField` (RGBA): окклюзия (A) и альбедо (RGB).
  - `_EmissionField` (RGBA): самосвечение блоков + динамические источники роботов.

### 4. GPU Radiance Cascades (Global Illumination)

- **Файлы:** `TerrariaLightingEngine.cs`, `WorldLighting.compute`
- **Задача:** Расчет 2D глобального освещения чистым физическим пайплайном:
  1. `SolveAutomaticNormals` — вектор нормалей по градиенту плотности (с Y-flip на Metal).
  2. `SolveCascade` — иерархический лучевой марш (каскады 0..3) со слиянием радиальных интервалов и естественным физическим затенением.
  3. `ResolveAndComposite` — однопроходная сборка финальной текстуры `_WorldLightTexture` (Direct Radiance + Lambertian Normal Response + Ambient).

### 5. Отрисовка мира (Scene Shading)

- **Файлы:** `Terrain.shader`, `Robot.cs`, `TentacleBatchRenderer.cs`
- **Задача:**
  - Террейн интерполирует `_WorldLightTexture`, накладывает анимации лавы/мерцания и UV-сдвиги.
  - Роботы, хвосты и эффекты рендерятся поверх в единой системе освещения.

### 6. Кастомный пост-процессинг (Post-Processing)

- **Файлы:** `PostProcessRendererFeature.cs`, `PostProcessRenderPass.cs`, `PostProcess.compute`
- **Задача:** Выполняется в RenderGraph **до** UI.
  - Встроенный URP `cameraData.renderPostProcessing` **выключен**.
  - Compute-шейдер делает: Bloom (Down/Up) → Chromatic Aberration → Motion Blur → ACES Tonemapping → Eigengrau Film Grain → Vignette.

### 7. UI Toolkit

- **Файлы:** `PlayerHUDView.cs`, `GlobalChatUI.cs`, `PlayerInteractionController.cs`
- **Задача:**
  - 100% UI Toolkit без EventSystem/uGUI.
  - Все координаты мыши конвертируются только через `RuntimePanelUtils.ScreenToPanel`.
  - Порядок слоев: Игровой UI — `0`, Лоадер меню — `100`.

---

## 3. Главные инварианты (Что нельзя ломать)

1. **Не добавлять искусственное затухание в Radiance Cascades:** интеграл сохранения энергии строгий; `distanceFalloff` убивает свет.
2. **Не раздувать паддинг меша:** размер меша террейна строго привязан к области видимости камеры + фиксированный паддинг.
3. **Не включать `cameraData.renderPostProcessing = true`:** это дублирует пост-процессинг стандартным проходом URP и ломает прозрачность.
4. **Координаты мыши:** клики проверяются только через `RuntimePanelUtils.ScreenToPanel` из-за `ScaleWithScreenSize`.
