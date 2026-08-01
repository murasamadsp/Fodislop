# План: Нативная система постпроцессинга на Compute Shaders

## Обзор

Система постпроцессинга на compute shaders с кастомными URP VolumeComponent'ами,
интегрированная в URP 2D Renderer через ScriptableRendererFeature.
Per-object motion blur для роботов через velocity buffer.
Effekseer рендерится поверх постпроцессинга.

---

## Архитектура рендер-пайплайна (порядок проходов)

```
1. TerrainRenderer (сортировка -1000)
2. Спрайты роботов/паков/тентаклей (сортировка -1..300)
3. Velocity Buffer Pass (BeforeRenderingTransparents или AfterRenderingTransparents)
   ─ рендерит роботов с MotionBlurTag в _CameraVelocityTexture RTHandle
4. PostProcess Pass (AfterRenderingTransparents)
   ─ compute shader: читает _CameraColorTexture + _CameraVelocityTexture
   ─ применяет: Bloom → ColorGrading → Vignette → ChromaticAberration → Eigengrau → MotionBlur
   ─ пишет результат обратно в _CameraColorTexture
5. Effekseer Pass (AfterRenderingPostProcessing)
   ─ рендерит VFX поверх постобработанного мира
```

**Причина**: Effekseer поверх PP — VFX (взрывы, магия) остаются чистыми. Bloom на мир (лава, кристаллы) работает.

---

## Файлы для создания

### Volume Components (URP Volume система)

```
Assets/Scripts/Rendering/PostProcessing/Components/
  BloomComponent.cs               # threshold, intensity, scatter, tint
  VignetteComponent.cs            # intensity, color, smoothness, center
  ChromaticAberrationComponent.cs # intensity
  ColorGradingComponent.cs        # lift, gamma, gain, contrast, saturation
  EigengrauComponent.cs           # intensity, noiseScale, animationSpeed
  MotionBlurComponent.cs          # intensity (камерный), maxSamples
```

Каждый наследует `VolumeComponent` + `IPostProcessComponent` с атрибутом `[VolumeComponentMenu("Fodinae/...")]`.

### Compute Shaders

```
Assets/Resources/Shaders/PostProcessing/
  PostProcess.compute             # основной fullscreen pass: все эффекты
  MotionBlur.compute              # velocity-based directional blur
```

`PostProcess.compute` ядра: `BloomPrefilter`, `BloomDownsample`, `BloomUpsample`, `ColorGrading`, `Vignette`, `ChromaticAberration`, `Eigengrau`, `CompositeFinal`.

`MotionBlur.compute` ядро: `MotionBlurDirectional` — читает velocity buffer, делает N сэмплов по вектору движения.

### Renderer Feature + Pass

```
Assets/Scripts/Rendering/PostProcessing/
  PostProcessRenderPass.cs         # ScriptableRenderPass: dispatch compute
  PostProcessRendererFeature.cs    # ScriptableRendererFeature: создаёт pass
  VelocityBufferRenderPass.cs      # рендерит роботов в velocity RT
  VelocityBufferRendererFeature.cs # регистрирует velocity pass
  PostProcessController.cs         # MonoBehaviour: API для runtime-контроля
  MotionBlurTag.cs                 # компонент-маркер на роботах для motion blur
```

### Volume Profile

```
Assets/Settings/PostProcessVolumeProfile.asset  # Global Volume с дефолтными значениями
```

### Скрипт для регистрации Volume Components в URP

```
Assets/Scripts/Rendering/PostProcessing/
  PostProcessVolumeRegistration.cs  # [InitializeOnLoad] — регистрирует кастомные
                                     # VolumeComponent типы в VolumeManager
```

---

## Файлы для изменения

| Файл | Изменение |
| - | - |
| `Assets/Settings/Renderer2D.asset` | Добавить `VelocityBufferRendererFeature` и `PostProcessRendererFeature` в renderer features |
| `Assets/Settings/UniversalRP.asset` | Подключить `PostProcessVolumeProfile.asset` как default volume profile |
| `Assets/Scenes/SampleScene.unity` | Добавить Global Volume GameObject с `PostProcessVolumeProfile` |
| `Assets/Scripts/Game/Robot.cs` | Добавить `MotionBlurTag` компонент при спавне; хранить `_previousFramePosition` |
| `Assets/Scripts/Core/GameLifetimeScope.cs` | Зарегистрировать `PostProcessController` |
| `Assets/Scripts/UI/PauseMenu.cs` | Добавить слайдеры для эффектов постпроцессинга |

---

## Детали реализации

### 1. Volume Components (`BloomComponent.cs` и др.)

```csharp
[VolumeComponentMenu("Fodinae/Bloom")]
public class BloomComponent : VolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter intensity = new(0f, 0f, 5f);
    public ClampedFloatParameter threshold = new(0.9f, 0f, 2f);
    public ClampedFloatParameter scatter = new(0.7f, 0.1f, 1f);
    public ColorParameter tint = new(Color.white);
    
    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => true;
}
```

Аналогично для остальных. `IPostProcessComponent` нужен для интеграции с Volume системой URP.

### 2. PostProcessRenderPass (compute dispatch)

```csharp
class PostProcessRenderPass : ScriptableRenderPass
{
    RTHandle _tempRT1, _tempRT2;    // для bloom пирамиды
    RTHandle _bloomRT;              // bloom результат
    
    // В Execute:
    // 1. Читаем VolumeStack → получаем значения всех компонентов
    // 2. ComputeShader.SetFloat/SetVector/SetTexture
    // 3. Dispatch:
    //    - BloomPrefilter → BloomDownsample (×N) → BloomUpsample (×N)
    //    - ColorGrading (LiftGammaGain + Contrast + Saturation)
    //    - Vignette + ChromaticAberration + Eigengrau
    //    - MotionBlur (читает velocity из _CameraVelocityTexture)
    // 4. Blit результат в cameraColorTarget
}
```

**Bloom алгоритм** (Kawase или стандартный):

- `BloomPrefilter`: threshold → яркие пиксели → half-res RT
- `BloomDownsample`: повторный downsample + blur (4-6 итераций)
- `BloomUpsample`: upsample + accumulate (обратный проход)
- Итог: `color + bloom * intensity`

**Eigengrau** (анимированный perceptual noise):

```hlsl
// В шейдере:
float noise = simplex2D(uv * noiseScale + _Time.y * animationSpeed);
float eigengrau = noise * intensity * (1.0 - luminance(color));
color += eigengrau * 0.02;  // добавляет зернистость в тёмные зоны
```

### 3. Velocity Buffer для Motion Blur

**VelocityBufferRenderPass**:

- `RenderPassEvent = BeforeRenderingTransparents` (или `AfterRenderingOpaques`)
- `FilteringSettings` с layerMask содержащим роботов
- `DrawingSettings` с кастомным шейдером (пишет `currentPos - previousPos` в RGB каналы)
- Рендерит в `_CameraVelocityTexture` RTHandle

**MotionBlurTag.cs**:

```csharp
public class MotionBlurTag : MonoBehaviour
{
    public Vector3 PreviousFrameWorldPosition;
    
    void LateUpdate()
    {
        PreviousFrameWorldPosition = transform.position;
    }
}
```

**Robot.cs изменения**:

- Добавить `MotionBlurTag` при `Awake()`
- Velocity шейдер (Sprite Renderer replacement) пишет `(currentPos - prevPos)` в velocity RT
- Альтернатива (проще): `MaterialPropertyBlock` с `_Velocity` вектором, стандартный спрайт-шейдер расширяется доп. pass для velocity

### 4. PostProcessController API

```csharp
public class PostProcessController : MonoBehaviour
{
    Volume _globalVolume;
    BloomComponent _bloom;
    // ...
    
    // Свойства:
    public float BloomIntensity { get => _bloom.intensity.value; set => _bloom.intensity.value = value; }
    public float VignetteIntensity { get => ... }
    public bool EigengrauEnabled { get => ... }
    
    // Методы для сетевых пакетов (будущее):
    public void ApplyPostProcessPacket(PostProcessPacket packet) { ... }
}
```

Регистрируется в `GameLifetimeScope` как `RegisterManager<PostProcessController>(builder)`.

Интеграция с `PauseMenu` — слайдеры для bloom, vignette, CA, Eigengrau, motion blur (аналогично аудио-шинам).

### 5. Effekseer — перенос pass event

В `EffekseerURPRenderPassFeature.cs` изменить:

```diff
- renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
+ renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
```

**НО**: это пакетный код в `Packages/`. Лучше **не патчить пакет**, а сделать копию RenderPassFeature в проекте:

- Наследовать `EffekseerURPRenderPassFeature` → свой `FodinaeEffekseerRenderPassFeature`
- Переопределить `renderPassEvent`
- Заменить в `Renderer2D.asset` ссылку с пакетного на проектный

### 6. Dust (отложен)

Пыль — post-process оверлей с возможностью переключения на ParticleSystem:

- `DustComponent` VolumeComponent (intensity, scale, speed)
- Compute shader ядро: simplex noise pattern, анимированный, модулируется яркостью подлежащего изображения (dust видна только в светлых зонах)
- В будущем: переключение на `ParticleSystem` через controller

**Отложен** — текущая архитектура не поддерживает доступ к lighting data в post-process pass. Нужно либо передавать световой буфер 2D Renderer'а как дополнительный RT, либо реализовать позже.

---

## Порядок реализации

### Этап 1: Инфраструктура

1. Создать папки `Assets/Scripts/Rendering/PostProcessing/` и `Assets/Resources/Shaders/PostProcessing/`
2. Создать `PostProcessVolumeRegistration.cs` — регистрация кастомных VolumeComponent типов
3. Создать Volume Components: `BloomComponent`, `VignetteComponent`, `ChromaticAberrationComponent`, `ColorGradingComponent`, `EigengrauComponent`, `MotionBlurComponent`

### Этап 2: Compute Shaders

4. Создать `PostProcess.compute` с ядрами:
   - `BloomPrefilter`, `BloomDownsample`, `BloomUpsample` (Kawase blur)
   - `ColorGrading` (lift/gamma/gain + contrast + saturation)
   - `Vignette` (smoothstep radial)
   - `ChromaticAberration` (offset R/B каналов от центра)
   - `Eigengrau` (simplex noise, анимированный, luminance-weighted)
   - `CompositeFinal` (сборка всех эффектов)
2. Создать `MotionBlur.compute` — velocity-based directional blur (N сэмплов по вектору)

### Этап 3: Renderer Feature + Pass

6. Создать `PostProcessRenderPassFeature.cs` + `PostProcessRenderPass.cs`
   - ScriptableRenderPass с временными RTHandle для bloom пирамиды
   - Execute: чтение VolumeStack → dispatch compute → blit в camera target
2. Создать `VelocityBufferRenderPassFeature.cs` + `VelocityBufferRenderPass.cs`
   - Рендерит роботов в `_CameraVelocityTexture`
3. Создать `MotionBlurTag.cs` — компонент-маркер
4. Обновить `Robot.cs` — добавить MotionBlurTag, хранить previous position

### Этап 4: Volume Profile + Сцена
 1. Создать `PostProcessVolumeProfile.asset` с дефолтными значениями всех компонентов
 2. Обновить `SampleScene.unity` — добавить Global Volume GameObject
 3. Обновить `Renderer2D.asset` — добавить обе RendererFeatures
 4. Обновить `UniversalRP.asset` — подключить volume profile

### Этап 5: Runtime API + UI
 1. Создать `PostProcessController.cs` — MonoBehaviour с API
 2. Зарегистрировать в `GameLifetimeScope`
 3. Интегрировать слайдеры в `PauseMenu` (аналогично аудио-шинам)

### Этап 6: Effekseer
 1. Создать `FodinaeEffekseerRenderPassFeature` (копия с изменённым `renderPassEvent`)
 2. Обновить `Renderer2D.asset` — заменить Effekseer feature
 3. Создать `PostProcessVolumeProfile.asset`

---

## Риски

1. **URP 2D Renderer события рендера**: `AfterRenderingPostProcessing` может не вызываться в 2D Renderer. Если так — Effekseer остаётся на `AfterRenderingTransparents`, а постпроцессинг размещается ДО него через `BeforeRenderingPostProcessing`. Проверить в рантайме.

2. **Velocity buffer для спрайтов**: SpriteRenderer не поддерживает кастомные шейдеры с несколькими pass в URP 2D. Решение: отдельный `DrawingSettings` с override material, который рендерит только velocity pass. Либо MaterialPropertyBlock + расширенный Terrain.shader подход.

3. **HDR/тонемаппинг**: Камера в HDR, но террейн-шейдер может не писать HDR-значения. Bloom требует HDR source для корректного threshold. Проверить формат _CameraColorTexture.

4. **Производительность**: 6+ compute dispatch за кадр. При 1920×1080 и 4 уровнях bloom пирамиды — ~5-8ms на integrated GPU. На дискретных — <2ms. Приемлемо для 2D игры.

---

## Верификация

1. **Сборка**: `dotnet build Assembly-CSharp.csproj` — 0 ошибок
2. **Запуск в Editor**: Play mode → проверить что Global Volume создан и active
3. **Bloom**: Поместить яркий белый спрайт на сцене → bloom должен быть виден
4. **Motion Blur**: Двигать робота с высокой скоростью → directional blur по вектору движения
5. **UI интеграция**: Открыть PauseMenu → слайдеры постпроцессинга → эффекты реагируют
6. **Effekseer**: Воспроизвести эффект → он рендерится поверх постпроцессинга (без размытия/виньетки)
7. **Граничные случаи**: Все эффекты на 0 — идентично текущему рендеру. Все на максимум — нет крашей/артефактов.
8. **Memory**: Profiler → нет утечек RTHandle, временные RT освобождаются каждый кадр
