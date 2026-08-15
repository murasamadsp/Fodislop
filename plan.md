# Нативный рефактор UI, сцен, input и world-render pipeline

## 1. Статус документа

Этот документ является decision-complete планом реализации. Он фиксирует не только общее направление, но и принятые продуктовые и технические решения, чтобы исполнитель не выбирал архитектуру по ходу работы.

План охватывает два связанных трека:

1. Полный рефактор runtime UI, scene flow, UI lifecycle, input и Editor preview.
2. Исправление исчезнувшей верхней поверхности мира (`transit`/`perspective`) и подготовка контракта под будущую полноценную 3D surface-сцену.

Регрессия `Tentacle`/`TentacleBatchRenderer` и их shaders в этот план не входит. Она остаётся отдельным расследованием.

## 2. Подтверждённые проблемы и причины

### 2.1. Recursive UI Toolkit layout

Наблюдаемый runtime error:

```text
Layout update is struggling to process current layout
PanelRootElement PanelSettings (x:0, y:0, width:0, height:0)
```

Это не единичная проблема кнопки Play и не кэш Unity. Текущая UI-архитектура создаёт нестабильный layout lifecycle:

- один `UIDocument.rootVisualElement` используется как глобальный mutable container;
- разные контроллеры независимо клонируют fullscreen UXML и добавляют их напрямую в root;
- часть UI полностью строится через `new VisualElement()` в C#;
- `GameManager.SetupUI()` вручную создаёт GameObjects и компоненты UI во время startup;
- controllers самостоятельно добавляют и удаляют visual trees;
- несколько компонентов меняют `PanelSettings.scale` и размеры Canvas;
- встречаются scheduled callbacks и layout/style mutations после первого layout;
- `GeometryChangedEvent` используется рядом с изменением layout properties;
- прозрачные fullscreen roots участвуют в hit-test и могут перекрывать Play;
- `z-index` используется в USS, хотя UI Toolkit его не поддерживает;
- `StandaloneWorldInitializer` с `[ExecuteAlways]` активируется при восстановлении backup-сцены после Play Mode;
- предупреждение начинает появляться именно во время Edit Mode restore, когда panel root временно имеет размер `0×0`;
- runtime UI и Editor preview используют пересекающиеся lifecycle paths.

Следствие: точечное изменение `pickingMode`, ручной `width/height`, принудительный rebuild или schedule-delay не является исправлением. Нужен один владелец visual tree, один владелец UI state и изолированный Editor preview.

### 2.2. Play отображается не там и не получает click

Причина состоит из нескольких факторов:

- `MainMenu` клонирует UXML в общий root во время `OnEnable`;
- рядом существуют другие fullscreen roots;
- loader и menu управляются двумя независимыми компонентами;
- `OnPlayButtonClicked()` скрывает loader в момент, когда loader должен, наоборот, начать защищать загрузку мира;
- MainMenu и `AssetLoadingIndicator` конкурируют за одно состояние;
- root может быть `NaN`, `0×0` или перекрыт другим fullscreen element;
- PanelSettings и visual tree восстанавливаются в неправильных lifecycle phases.

Новый flow должен быть `Frontend → Connect/Auth/Loading → Gameplay`, а не скрытие всего UI сразу после Play.

### 2.3. Исчезнувшая верхняя texture над terrain

Причина подтверждена историей репозитория:

- `SurfaceRenderer` создаёт два простых quad mesh: `SurfaceTransit` и `SurfacePerspective`;
- эти meshes содержат position и обычный UV0;
- в commit `519ef819` материал `SurfaceMaterial` был переключён на `Terrain.shader`;
- terrain shader ожидает terrain-specific vertex contract: atlas rect и дополнительные UV-каналы;
- у surface quad `subAtlasRect` остаётся нулевым;
- terrain fragment path считает atlas rect невалидным и возвращает прозрачный результат;
- texture может успешно загрузиться, но поверхность всё равно не отображается.

Это доменная ошибка shader/material contract. Исправление загрузчика texture не решает её.

## 3. Цели

- Полностью устранить recursive layout и root `0×0/NaN` во время Play/Stop и scene transitions.
- Сделать Play визуально корректным, кликабельным и доступным через keyboard/gamepad.
- Свести весь экранный runtime UI к одному `UIDocument` и одному `UIHost`.
- Перенести статическую структуру в UXML, оставив C# только binding и действительно динамические collections/grids.
- Разделить application lifetime и world/session lifetime.
- Создать отдельную frontend-сцену с утверждённым новым дизайном `Orbital Arrival`.
- Сохранить серверный auth flow через `OpenWindowPacket(tag="auth")` и `CloseWindowPacket`, но встроить его в frontend.
- Поддержать desktop resolutions, Retina/FullHD/4K, responsive landscape phone/tablet viewports и safe area.
- Обеспечить полноценные keyboard, mouse, touch и gamepad UI contracts.
- Централизовать world-space labels отдельно от screen UI.
- Автоматически показывать UI, локальную карту и робота в Edit Mode Game View без runtime `[ExecuteAlways]`.
- Вернуть `transit`/`perspective` surface textures через dedicated shader.
- Подключить surface emission к Radiance Cascades так, чтобы она освещала верхние клетки terrain.
- Подготовить `WorldSurfaceRoot` и lighting contributor API под будущую 3D surface-сцену.

## 4. Не входит в текущий трек

- Исправление `Tentacle`, `TentacleBatchRenderer` и tentacle shaders.
- Полная реализация будущей 3D surface-сцены с meshes, environment art и gameplay.
- Полный визуальный redesign gameplay HUD, inventory, chat, Programmator и packet windows.
- Реальные iOS/Android builds и mobile release pipeline.
- Сокрытие или declutter world labels при высокой плотности объектов.
- Новый серверный auth protocol.
- Поддержка старого и нового UI architecture параллельно через feature flag.

## 5. Scene topology

### 5.1. Сцены

В Build Settings находятся три runtime-сцены:

1. `Bootstrap.unity` — build index 0.
2. `Frontend.unity` — frontend visual/content scene.
3. `MainGame.unity` — world/gameplay scene.

Создание сцен, назначение компонентов, PanelSettings и изменение Build Settings выполняются только через Unity Editor API. Текстовое редактирование `.unity`, `.asset` и `.prefab` запрещено.

### 5.2. Bootstrap scene

`Bootstrap` загружается первой и остаётся активной до закрытия приложения. Она содержит:

- `AppLifetimeScope`;
- единственный runtime `UIDocument`;
- runtime clone `PanelSettings`;
- `UIHost`;
- `UIStateController`;
- `UIScaleService`;
- `InputDeviceService`;
- `SessionCoordinator`;
- `WorldPacketGate`;
- `ConnectionManager` и transport-independent networking;
- `NetworkService` и application packet router;
- `ClientAssetLoader` и persistent asset caches;
- `ClientConfigManager`;
- localization services;
- audio services, которым требуется переживать scene transition;
- application diagnostics.

Bootstrap не содержит terrain, player, robots или gameplay renderers.

### 5.3. Frontend scene

`Frontend` загружается additively из Bootstrap и содержит только frontend visual domain:

- layered background renderers;
- planet renderer/material/shader;
- optional frontend camera/visual effects;
- scene-owned generated art references;
- marker component, сигнализирующий `FrontendSceneReady`.

Controls, text, auth content, loader и reconnect UI не принадлежат Frontend scene. Они живут в persistent `UIHost`, поэтому не теряются при загрузке `MainGame`.

### 5.4. MainGame scene

`MainGame` загружается additively и создаёт `WorldLifetimeScope`. Scope регистрирует:

- `MapStorage` и `MapManager`;
- `TerrainRenderer`;
- `TerrariaLightingEngine`;
- `WorldTextureManager`;
- `WorldSurfaceRoot` и `SurfaceRenderer`;
- world lighting contributors;
- local player и movement/input bridge;
- robot, pack, VFX и world managers;
- gameplay packet consumers;
- gameplay presenters;
- `WorldUiRoot`;
- world-space nickname/clan/chat presenters;
- camera and world-space UI camera services.

При возврате во frontend весь `WorldLifetimeScope` dispose-ится, `MainGame` выгружается, а app-level services сохраняются.

### 5.5. Ownership table

| Domain | Lifetime | Владелец | Очищается при Back to Menu |
|---|---|---|---|
| PanelSettings, UIDocument, UIHost | Application | AppLifetimeScope | Нет |
| Locale, UI scale, input-device mode | Application | AppLifetimeScope | Нет |
| Connection/reconnect process | Session | SessionCoordinator | Да |
| Staged gameplay packets | Session | WorldPacketGate | Да |
| MapStorage, terrain, lighting | World | WorldLifetimeScope | Да |
| Gameplay feature views | World | WorldLifetimeScope + UIHost mount handles | Да |
| Packet window/auth bindings | Session | PacketWindowPresenter | Да |
| World labels and bubbles | World | WorldUiRoot | Да |
| Disk asset cache | Application/disk | ClientAssetLoader | Нет |
| RAM world texture/cache state | World/session | WorldLifetimeScope | Да |
| Editor preview objects | Edit Mode only | EditorWorldPreviewHost | Перед Play |

## 6. Application state machine

### 6.1. States

```text
Boot
Frontend
Connecting
Authenticating
LoadingWorld
Gameplay
Reconnecting
ReturningToFrontend
FatalError
```

Только `UIStateController` имеет право переключать верхнеуровневые состояния и видимость соответствующих shell layers.

### 6.2. Boot → Frontend

1. Bootstrap создаёт AppLifetimeScope.
2. Создаётся runtime clone PanelSettings.
3. UIDocument получает `sourceAsset = GameShell.uxml`.
4. UIHost query-ит обязательные slots и fail-fast завершает startup, если shell неполон.
5. Загружается Frontend scene.
6. После `FrontendSceneReady` UI переходит в `Frontend`.
7. Play становится активным только после finite layout и готовности input routing.

### 6.3. Play flow

Нажатие Play разрешено mouse, touch, Enter/Space и gamepad Submit.

После первого accepted click:

1. Play немедленно блокирует повторную активацию.
2. State переходит в `Connecting`.
3. Начинается async preload `MainGame` с отложенной activation.
4. Параллельно запускается transport connect.
5. `ConnectionManager` отправляет `ClientHelloPacket` после соединения.
6. Frontend остаётся видимым, но action area заменяется loading/status composition.
7. Normal loading не имеет Cancel button.

### 6.4. Auth flow

Серверный контракт сохраняется:

- если token принят, приходит `AuthTokenPacket`;
- если авторизация требуется, приходит `OpenWindowPacket` с tag `auth`;
- после успешной авторизации сервер присылает `CloseWindowPacket` и продолжает world flow.

Client behavior:

- `OpenWindowPacket(tag="auth")` не создаёт generic floating window;
- UIState переходит в `Authenticating`;
- server-built auth content монтируется в `FrontendAuthContentSlot`;
- Orbital Arrival background остаётся видимым;
- MainGame preload может продолжаться, но scene activation запрещена;
- client submit отправляет packet context серверу;
- auth view не скрывается локально после submit;
- auth view закрывается только после server `CloseWindowPacket`;
- переход к world activation требует одновременно `IsAuthorized == true` и закрытого auth view.

### 6.5. World packet staging

Dummy и текущий server flow могут отправить `WorldInitPacket` сразу после `AuthTokenPacket`, ещё до готовности `WorldLifetimeScope`. Поэтому application router делит packets на две категории.

Application packets обрабатываются сразу:

- connection status;
- auth;
- outdated client;
- server/client config;
- asset transport/manifest responses;
- disconnect/reconnect control;
- protocol/fatal errors.

World packets до готовности scope помещаются в `WorldPacketGate`:

- `WorldInitPacket`;
- `MapRegionPacket`;
- player/robot/world state;
- inventory/stats/chat/gameplay packets;
- gameplay packet windows;
- world audio/VFX events.

Требования к staging:

- FIFO сохраняет точный receive order;
- drain выполняется на Unity main thread;
- packets не обрабатываются одновременно со scene activation;
- drain начинается только после регистрации полного набора world consumers;
- новые packets, пришедшие во время drain, добавляются в хвост;
- buffer ограничен одновременно количеством envelopes и фактическим retained payload size;
- target limits: 4096 packets и 64 MiB serialized payload;
- превышение любого limit вызывает protocol/session error и disconnect;
- silent drop, replacement и неограниченный список запрещены;
- teardown очищает buffer и запрещает drain в disposed scope;
- reconnect использует новый session generation id, чтобы packets старой сессии не попали в новый world.

### 6.6. MainGame activation

Activation разрешена после:

- successful auth;
- закрытия auth packet flow;
- завершения async scene preload;
- доступности required app services;
- отсутствия fatal connection state.

После activation:

1. MainGame создаёт WorldLifetimeScope.
2. Scope регистрирует world packet consumers.
3. `WorldPacketGate.AttachConsumerSet()` атомарно открывает drain.
4. Staged packets обрабатываются последовательно.
5. Runtime textures и terrain caches запрашивают необходимые assets.
6. UI остаётся в `LoadingWorld`.

### 6.7. World-ready gate

Loader скрывается только когда одновременно выполнены условия:

- `MapStorage.IsReady == true`;
- `MapManager.IsWorldInitialized == true`;
- local player получил server position;
- terrain mesh построен для spawn region;
- обязательные terrain/surface textures готовы;
- Radiance Cascades создали и опубликовали валидные textures/rect;
- первый lighting solve завершён;
- camera target назначен;
- `CameraFollow.SnapToTarget()` выполнен;
- player visual разрешён для отображения.

Только после этого state становится `Gameplay`.

### 6.8. Reconnect

Reconnect бесконечный, пока пользователь явно не выбрал Back to Menu.

Backoff sequence:

```text
1s → 2s → 4s → 8s → 16s → 30s → 30s ...
```

Reconnect UI показывает:

- причину последнего disconnect;
- номер попытки;
- countdown до следующей попытки;
- текущий transport status;
- Back to Menu.

Во время reconnect:

- gameplay input заблокирован;
- world visual может оставаться видимым, но simulation/network actions заморожены;
- generic packet windows закрываются/dispose-ятся;
- reconnect overlay блокирует hit-test нижних слоёв;
- успешный reconnect создаёт новую packet generation и проходит auth/world synchronization;
- старые staged packets не переиспользуются.

Back to Menu выполняет полную session teardown:

1. Остановить retry loop и cancellation token.
2. Disconnect transport.
3. Dispose packet bindings и staged packets.
4. Dispose WorldLifetimeScope.
5. Выгрузить MainGame.
6. Очистить session RAM caches и world UI.
7. Сохранить disk asset cache, client settings, locale и UI scale.
8. Загрузить/показать Frontend.
9. Вернуть state в `Frontend`.

## 7. UIHost и visual tree

### 7.1. Единственный shell

`UIDocument.sourceAsset` назначен на `GameShell.uxml`. Shell содержит постоянный порядок слоёв:

1. `FrontendLayer`.
2. `GameplayLayer`.
3. `TouchControlsLayer`.
4. `WindowLayer`.
5. `ModalBlockerLayer`.
6. `LoadingLayer`.
7. `ReconnectLayer`.
8. `ErrorLayer`.
9. `TooltipLayer`.
10. `DiagnosticsLayer`.

Порядок в UXML является z-order. `z-index` не используется.

### 7.2. Picking contract

- Пустые layer roots имеют `PickingMode.Ignore`.
- Controls имеют стандартный picking.
- Невидимые feature roots используют `display: none`.
- Видимый modal blocker использует `PickingMode.Position` и полностью блокирует нижние слои.
- Loader и reconnect blocker участвуют в picking только когда видимы.
- Нельзя имитировать visibility через прозрачность и изменение pickingMode каждый frame.
- Нельзя оставлять прозрачный fullscreen element поверх Play.

### 7.3. Feature mounting

`UIHost`:

- валидирует slots при startup;
- один раз clone-ит feature templates;
- возвращает typed mount handle;
- запрещает повторный mount одного singleton feature;
- dispose handle скрывает, очищает dynamic content и снимает bindings;
- не удаляет shell layers;
- не разрешает presenter обращаться к `UIDocument.rootVisualElement` напрямую.

Presenters:

- получают конкретный feature root через constructor/DI;
- query-ят обязательные named elements;
- fail-fast сообщают отсутствующие names;
- подписывают callbacks один раз;
- отписываются в `Dispose()`;
- не загружают USS;
- не задают root width/height;
- не создают static controls в C#.

### 7.4. UXML feature templates

Нужны отдельные templates:

- Frontend/OrbitalArrival;
- Frontend/Auth;
- LoadingWorld;
- Reconnect;
- FatalError;
- PlayerHUD;
- TouchHUD;
- Inventory;
- LocalChat;
- GlobalChat;
- FloatingChat presentation template;
- PauseMenu;
- Settings sections;
- Programmator shell/list/grid/dialog;
- PacketWindow;
- ServerModal;
- WorldMap;
- Minimap;
- FPS/connection diagnostics;
- MissionArrow;
- Tooltip.

В C# остаются динамическими:

- inventory slots и item data;
- server packet component trees;
- chat message lists;
- Programmator cells и command data;
- robot/world label instances;
- status/buff collections;
- map texture content.

### 7.5. Legacy migration mapping

| Legacy area | Новая ответственность |
|---|---|
| `MainMenu` runtime CloneTree | `FrontendPresenter` + pre-mounted UXML |
| `AssetLoadingIndicator` fullscreen overlay | `LoadingWorldPresenter` под UIStateController |
| `ReconnectUI.Instance` | app-level `ReconnectPresenter` |
| `GameErrorUI` direct root mutation | `FatalErrorPresenter` |
| `GameManager.SetupUI()` | удаляется; composition выполняют scopes/UIHost |
| `PauseMenu` imperative tree | `PauseMenu.uxml` + presenter |
| `PlayerHUDView` root.Add controls | `PlayerHUD.uxml` + dynamic collections only |
| `InventoryView` floating root element | feature-local drag layer внутри Inventory template |
| chats direct fullscreen clones | chat feature templates и shared chat model |
| `WindowPacketProcessor` root.Add | `PacketWindowPresenter` в `WindowLayer` |
| `ModalWindowHandler` ad-hoc overlay | `ServerModalPresenter` + `ModalBlockerLayer` |
| `WorldMapRenderer` Canvas | UI Toolkit WorldMap feature |
| `MinimapController` Canvas | UI Toolkit Minimap feature |
| `FPSCounter` owned Canvas | diagnostics feature в shell |
| `UIInputManager` raw element stack | `UIInputCoordinator` с typed modal ownership |
| `PauseMenu.IsMenuOpen` static | state/query interface |
| direct `PanelSettings.scale` writes | только `IUIScaleService` |

Legacy system удаляется в той же миграции. Dual runtime path и feature flag не создаются.

## 8. Packet windows

### 8.1. Количество окон

Одновременно отображается ровно одно gameplay `OpenWindowPacket` window.

Если приходит новое окно при открытом старом:

1. Dispose old `WindowBinding`.
2. Unregister old callbacks.
3. Очистить old dynamic content.
4. Атомарно bind новое окно в тот же slot.
5. Не ожидать отдельный `CloseWindowPacket` для локальной замены, потому что новый Open является server command замены.

### 8.2. Server-authoritative close

- ESC/Cancel/close button отправляют request серверу.
- Окно локально не скрывается.
- Повторный close request блокируется до response/timeout policy transport layer.
- Фактическое закрытие происходит после `CloseWindowPacket`.
- Auth использует тот же authoritative contract.

### 8.3. Size contract

- Server `Width`/`Height` — reference UI units, не physical pixels.
- Запрошенный outer size считается желаемым fixed size.
- Client не пересчитывает его через DPI.
- Invalid values (`<= 0`, NaN, infinity, overflow) являются protocol error.
- Final outer size равен `min(serverSize, safeViewportSize - shellMargins)` по каждой оси.
- Clamp не меняет aspect/content semantics сам по себе.
- Content region получает `overflow/scroll`, если не помещается.
- Responsive classes позволяют content reflow на compact viewport.
- Header/footer остаются видимыми, scroll применяется только к body.
- UI Toolkit `calc()` не используется.

## 9. Responsive layout и PanelSettings

### 9.1. PanelSettings

- Reference resolution: `1920×1080`.
- Scale mode: `Scale With Screen Size`.
- Screen match: balanced width/height (`0.5`).
- Render mode: Screen Space Overlay.
- Theme source: только `FodinaeTheme.tss`.
- Runtime использует clone PanelSettings, чтобы пользовательский scale не мутировал project asset.
- PanelSettings назначается один раз до первого attach UI tree.
- Controllers не могут заменять PanelSettings.

### 9.2. User scale

- Config range сохраняется `0.5–2.0`.
- Default `1.0`.
- Effective UI scale вычисляет только `IUIScaleService`.
- Pause/settings меняют config через service, а не напрямую PanelSettings.
- Scale update выполняется только при изменении значения.
- Gameplay Canvas больше не существует, поэтому обход `FindObjectsByType<Canvas>()` удаляется.
- World-space label scale не наследует screen UI multiplier напрямую; он имеет свой readable-scale policy.

### 9.3. Responsive buckets

Bucket определяется по logical safe viewport после PanelSettings scaling:

- `compact`: safe width `< 960` reference units;
- `standard`: `960–1599`;
- `wide`: `>= 1600`.

Bucket controller:

- читает geometry;
- меняет class только при переходе между buckets;
- откладывает class mutation до следующей UI update phase;
- не пишет width/height обратно;
- не создаёт GeometryChanged recursion;
- не реагирует на sub-pixel oscillation.

### 9.4. Safe area

- Background art может выходить до физических краёв.
- Все interactive controls находятся внутри safe content rect.
- Safe area применяется frontend, gameplay HUD, packet windows, modals и touch controls.
- Изменение safe area/resolution пересчитывает padding существующего `SafeAreaContainer`.
- Нельзя создавать новый container при каждом изменении.
- Landscape является обязательной orientation моделью для mobile viewport.
- Portrait показывает отдельный rotate-device state, но полноценный portrait layout не реализуется.

### 9.5. Target viewports

Desktop build acceptance:

- 1366×768;
- 1920×1080;
- 2560×1440;
- 3840×2160;
- Retina-like logical 1512×982;
- ultrawide 2560×1080.

Responsive emulation acceptance:

- phone 844×390;
- phone 932×430;
- tablet 1180×820 landscape;
- tablet 1366×1024 landscape;
- safe-area inset variants.

Реальные mobile builds в текущий scope не входят.

## 10. Frontend design: Orbital Arrival

### 10.1. Утверждённая концепция

Новый frontend не переиспользует старый фон.

Narrative:

- в Menu игрок находится на орбите и видит планету как цель экспедиции;
- во время Loading камера/planet composition визуально приближается к поверхности;
- Gameplay означает завершённую высадку;
- Reconnect трактуется как потеря сигнала экспедиции.

Composition:

- narrative visual/planet находится справа;
- branding, заголовок и primary action находятся слева;
- system/network status является вторичным слоем;
- planet не пересекается с branding/controls;
- contrast обеспечивается dedicated action region, а не случайным затемнением всего экрана.

### 10.2. Assets

Новые assets генерируются и затем вручную дорабатываются:

- 4K master background;
- separate planet albedo;
- atmosphere/rim mask;
- cloud/surface detail mask;
- optional night/emission mask;
- star/background depth layers;
- logo/brand vector или high-resolution source;
- signal-loss overlay/mask.

Требования:

- source art хранится отдельно от runtime derivatives;
- runtime textures имеют корректный sRGB/alpha/import settings;
- texture sizes и compression проверяются для desktop;
- generated image не содержит baked UI text/buttons;
- controls и localized text всегда рисуются UI Toolkit;
- planet/background layers позволяют анимацию без смены полноэкранного bitmap state.

### 10.3. Shader

Frontend shader отвечает только за visual presentation:

- planet transform/approach;
- atmosphere rim;
- subtle emission;
- signal-loss/desaturation;
- transition orbit → descent;
- reduced motion variant.

Shader не участвует в world Radiance Cascades и не использует terrain pipeline.

### 10.4. Frontend states

`Frontend`:

- Play enabled;
- server status visible;
- planet orbit composition.

`Connecting`:

- Play disabled;
- current connection stage visible;
- no fake percent.

`Authenticating`:

- server auth content in dedicated slot;
- background remains orbit composition;
- submit remains server-authoritative.

`LoadingWorld`:

- descent composition;
- real stage list;
- real files/bytes counters when totals are known;
- spinner/stage label when total is unknown;
- no fabricated combined 0–100% progress.

`Reconnecting`:

- signal-loss visual;
- attempt/countdown/status;
- Back to Menu.

## 11. Localization

- Добавить официальный Unity Localization package.
- Client-authored text использует string table keys.
- UXML не содержит значимые hardcoded production strings, кроме preview placeholders.
- Server-supplied strings остаются protocol data и не переводятся клиентом автоматически.
- Active locale принадлежит AppLifetimeScope и переживает world teardown.
- Locale switch обновляет mounted feature views без их повторного mount.
- Layout tests включают длинные строки и text expansion.
- Font fallback должен поддерживать русский и английский minimum set.

## 12. Input architecture

### 12.1. Единый Input Actions source

Существующий `InputSystem_Actions.inputactions` становится единственным источником runtime actions.

Удаляются:

- прямой polling `Keyboard.current` из `PlayerInputHandler`;
- создание отдельных `new InputAction()` в controllers для Escape/map/scroll, если action уже принадлежит asset;
- прямые вызовы player methods из UI, обходящие input command layer;
- эмуляция gamepad как mouse для стандартных controls.

Action maps разделяются минимум на:

- `Gameplay`;
- `UI`;
- `TouchGameplay`;
- `Programmator`;
- `Map`.

`UIInputCoordinator` включает и выключает maps по application/UI state.

### 12.2. Last-used device

`IInputDeviceService` отслеживает реальный последний meaningful input:

- mouse/keyboard;
- gamepad;
- touch.

Он публикует:

- active device family;
- prompt scheme;
- navigation mode;
- whether touch HUD should be visible.

Noise/deadzone события stick и synthetic mouse events от touch не должны постоянно переключать mode.

### 12.3. Keyboard and mouse

- Mouse pointer работает с UI Toolkit controls.
- Keyboard navigation поддерживает Tab/Shift+Tab, arrows, Submit и Cancel.
- Escape routing централизован: Programmator → packet window request → pause menu → gameplay.
- Gameplay movement/actions блокируются, когда `IInputBlocker.IsInputBlocked` true.

### 12.4. Gamepad UI

- Standard UI использует semantic focus navigation.
- Submit/Cancel отображают platform-appropriate prompts.
- При открытии feature focus назначается на primary/last valid element.
- При закрытии focus возвращается вызывающему control или безопасному gameplay target.
- Focus не остаётся на hidden/detached element.

Inventory/Programmator grids используют semantic pick/place:

- D-pad/left stick перемещает cell focus;
- A/Submit выбирает или берёт/кладёт;
- X выполняет multi-select/toggle action;
- Y открывает radial/context action;
- shoulder buttons переключают tabs/sections;
- virtual cursor не используется.

### 12.5. Touch gameplay HUD

- Landscape only.
- Movement: floating stick в левой safe zone.
- Primary action: крупная удерживаемая Dig button справа.
- Secondary actions: radial/compact action cluster.
- В cluster входят auto-dig, aggression, map и основные context actions.
- Поддерживаются left-handed и right-handed presets.
- Пользователь настраивает touch-control scale и opacity.
- Полный drag editor расположения controls в первой версии не реализуется.
- Touch HUD отображается только для touch mode/device policy.

### 12.6. Touch grids

- Tap выбирает cell/item.
- Drag переносит inventory item.
- Long press открывает radial/context menu.
- Multi-select включается явным mode toggle.
- Desktop Shift/Ctrl modifiers не показываются как экранные кнопки.
- Gesture thresholds централизованы и учитывают UI scale.

## 13. World-space UI

### 13.1. Rendering domain

Screen UI остаётся UI Toolkit `UIDocument`.

World-bound UI живёт в отдельном `WorldUiRoot` и использует подходящий renderer domain:

- nickname/clan text;
- floating chat;
- object-attached markers;
- world-space UI camera where required.

World labels не проецируются в общий screen shell каждый frame.

### 13.2. Grouping

Для каждого player/robot создаётся один grouped presenter:

```text
Floating chat
Nickname
Clan
```

Presenter:

- владеет vertical offsets;
- обновляет content по событиям;
- управляет chat lifetime;
- dispose-ится вместе с world entity;
- не создаёт независимые competing sorting offsets.

### 13.3. Scale and density

- Labels сохраняют читаемый screen-relative size в поддерживаемом zoom range.
- Scale clamp не зависит от screen UI scale напрямую.
- Все labels показываются.
- Distance culling и overlap declutter не применяются.
- Пересечение labels разных объектов является осознанно допустимым поведением.

### 13.4. Legacy Canvas migration

- FPS переносится в UI Toolkit diagnostics layer.
- Minimap переносится в UI Toolkit gameplay feature.
- World map переносится в UI Toolkit feature.
- ScreenSpaceOverlay Canvas, созданные этими controllers, удаляются.
- Настоящие world-bound TMP/MeshRenderer elements остаются в WorldUiRoot domain.

## 14. Editor preview

### 14.1. Требуемое поведение

До нажатия Play в Game View автоматически видны:

- интерфейс;
- реальная локальная карта;
- локальный робот;
- terrain/surface/lighting preview в доступном качестве.

Default preview state: Gameplay.

Editor toolbar позволяет переключать:

- Gameplay;
- Frontend;
- Loading;
- Packet Window;
- Pause.

### 14.2. Архитектура preview

Preview реализует код только под `Assets/Editor`:

- `EditorWorldPreviewHost` создаётся через editor lifecycle callbacks;
- runtime MonoBehaviour не получает `[ExecuteAlways]` ради preview;
- preview использует actual local `.mapb` и actual local textures;
- отсутствие map/assets вызывает явную preview error, без generated fallback world;
- preview objects имеют `HideFlags.DontSave`/`DontSaveInEditor`;
- preview state не сериализуется в MainGame scene;
- preview не подключается к server;
- preview не регистрируется в App/World runtime scopes;
- preview presenters не подписывают runtime NetworkService.

### 14.3. Play Mode transition

На `ExitingEditMode` host синхронно:

1. Останавливает preview updates.
2. Отписывает Editor callbacks.
3. Dispose-ит preview bindings.
4. Уничтожает preview meshes/material instances/GameObjects.
5. Очищает static preview state.
6. Только после teardown разрешает runtime startup.

После возврата в Edit Mode preview восстанавливается только когда:

- Unity не играет и не собирается входить в Play;
- scene restore завершён;
- editor не compiling/updating;
- MainGame scene валидна;
- нет уже активного preview generation.

Это предотвращает запуск preview на временной backup-сцене с panel root `0×0`.

## 15. SurfaceRenderer и будущая 3D surface-сцена

### 15.1. Current surface scope

Сейчас реализуются и восстанавливаются две bands:

- `transit`;
- `perspective`.

Также формализуется замкнутая граница terrain: слева, справа и снизу визуально
продолжается бесконечная масса `CellType.RedRock`. Это presentation geometry
границы мира, а не данные `MapStorage` и не подстановка серверных map cells.

Полноценная 3D scene между верхней границей карты и surface environment будет отдельным треком. Текущий рефактор создаёт API и hierarchy, в которые она подключится без повторного изменения terrain renderer.

### 15.2. Assets

- Textures временно хранятся локально в `Assets/Textures/Surface`.
- Они назначаются через serialized Unity references.
- Они не загружаются через Resources.
- Они не запрашиваются по строковым CDN paths.
- Они штатно попадают в build как referenced assets.
- Отсутствующая serialized texture является startup validation error.

### 15.3. Materials

- Transit и perspective имеют отдельные material instances.
- Shared project material не мутируется runtime texture assignment.
- Material lifecycle принадлежит `SurfaceRenderer`/WorldLifetimeScope.
- Runtime material instances уничтожаются при world teardown.
- Surface material не использует `Terrain.shader`.
- Surface material не ожидает terrain atlas rect или семь UV channels.

### 15.4. Dedicated surface shader

Shader имеет явные passes:

1. Visible surface pass.
2. Lighting material/occupancy pass.
3. Emission pass.

Visible pass:

- семплирует собственную `_BaseMap`;
- использует обычный quad UV0;
- семплирует `_WorldLightTexture` через world position и `_WorldLightRect`;
- не добавляет relief/connectivity darkening;
- поддерживает transparency согласно source alpha;
- не использует fallback color при отсутствии texture.

Lighting material pass:

- публикует surface albedo;
- публикует физическую occupancy для геометрии surface;
- не создаёт фиктивные map cells;
- не меняет terrain cell occupancy.

Emission pass:

- использует explicit emission mask/intensity material properties;
- записывает radiance в `EmissionField`;
- участвует в том же Radiance Cascades solve;
- освещает верхние terrain cells;
- visual emission и physical emission имеют согласованный цвет/intensity;
- bloom остаётся visual post-effect и не заменяет radiance contribution.

### 15.5. Geometry contributor API

Добавляется `ILightingGeometryContributor`, через который static/dynamic geometry предоставляет draw commands lighting engine.

Минимальный контракт:

```csharp
public interface ILightingGeometryContributor
{
    int GeometryRevision { get; }

    void RenderMaterialField(CommandBuffer commandBuffer, in LightingFieldContext context);

    void RenderEmissionField(CommandBuffer commandBuffer, in LightingFieldContext context);
}
```

Требования:

- TerrainRenderer остаётся первичным producer terrain Material/Emission fields.
- SurfaceRenderer становится отдельным contributor.
- Future 3D surface renderers регистрируются через тот же API.
- Geometry revision меняется только при изменении mesh/material occupancy.
- Pure emission intensity change не заставляет пересчитывать AO.
- Lighting engine не выполняет глобальный scene scan каждый frame.
- Contributors регистрируются/удаляются через WorldLifetimeScope/service.

### 15.6. WorldSurfaceRoot

`WorldSurfaceRoot`:

- задаёт hierarchy над верхней границей карты;
- использует server Top-Left → Unity world conversion через `CoordinateUtils`;
- anchor располагается относительно `MapManager.WorldHeight`;
- содержит current bands;
- позже примет 3D meshes, materials и surface camera content;
- уничтожается вместе с MainGame;
- не использует `DontDestroyOnLoad`.

### 15.7. Lighting invariants

- Сохраняется GPU Radiance Cascades pipeline.
- Не возвращаются legacy SDF/raymarch/CPU fallback/blur paths.
- `_WorldLightTexture`, `_WorldLightRect`, `InvalidateCell` остаются внешним контрактом.
- Surface geometry не подставляется как cell type `0`.
- Закрытые направления мира используют explicit `CellType.RedRock` boundary
  classification и проходят полный terrain vertex/material contract.
- Surface emission не меняет серверные `CellConfigProperties.Glowing` у terrain cells.
- Contact/cavity AO продолжает зависеть только от geometry revision.
- Surface occupancy может участвовать в geometry revision через contributor API.

## 16. Public interfaces и types

### 16.1. UI

```csharp
public interface IUIHost
{
    VisualElement GetSlot(UISlot slot);

    IUIViewHandle Mount(UIFeature feature);
}

public interface IUIViewHandle : IDisposable
{
    VisualElement Root { get; }

    bool IsVisible { get; }

    void SetVisible(bool visible);
}

public interface IUIStateController
{
    UIAppState CurrentState { get; }

    event Action<UIAppState>? StateChanged;
}

public interface IUIScaleService
{
    float UserScale { get; set; }

    Rect SafeViewport { get; }

    UIResponsiveBucket ResponsiveBucket { get; }
}
```

Exact mutations перехода state остаются internal у `SessionCoordinator`; произвольный controller не получает публичный `SetState()`.

### 16.2. Input

```csharp
public interface IInputDeviceService
{
    InputDeviceFamily ActiveFamily { get; }

    InputPromptScheme PromptScheme { get; }

    event Action<InputDeviceFamily>? ActiveFamilyChanged;
}
```

`IInputBlocker` вычисляется из typed state: modal, packet window, chat focus, pause, Programmator и reconnect/loading blockers.

### 16.3. Networking/session

```csharp
public interface IWorldPacketGate
{
    WorldPacketGateState State { get; }

    void Stage(in ReceivedPacketEnvelope packet);

    void AttachConsumerSet(IWorldPacketConsumerSet consumers, long sessionGeneration);

    void Reset(long sessionGeneration);
}
```

`ReceivedPacketEnvelope` содержит receive sequence, session generation, packet и точный payload byte count.

### 16.4. World UI

```csharp
public interface IWorldUIRoot
{
    IWorldUILabelHandle Attach(WorldUIAnchor anchor, WorldUILabelModel model);
}
```

Handle владеет grouped nickname/clan/chat presentation и dispose-ится entity lifecycle.

### 16.5. Surface/lighting

- `ILightingGeometryContributor` — material/emission geometry.
- `ILightingGeometryRegistry` — explicit registration без scene scans.
- `WorldSurfaceRoot` — world scene owner future surface content.
- `SurfaceMaterialSettings` — explicit texture/emission parameters без implicit defaults.

Старые публичные UI controller APIs совместимость не сохраняют.

## 17. Failure policy и diagnostics

### 17.1. Fail-fast cases

- UIDocument отсутствует в Bootstrap.
- PanelSettings/theme/sourceAsset отсутствуют.
- Обязательный shell slot не найден.
- Feature UXML или обязательный named element отсутствует.
- Invalid packet window dimensions.
- World packet staging overflow.
- Session generation mismatch.
- Required local `.mapb` отсутствует.
- Required surface texture/material/shader отсутствует.
- Surface shader pass отсутствует.
- World lighting texture/rect не опубликованы перед world-ready.
- Scene activation завершилась без WorldLifetimeScope.

### 17.2. Запрещённые fallback

- generated placeholder map;
- white/default texture;
- случайная default position `(0,0)`;
- автоматическое создание второго UIDocument;
- повторное добавление stylesheet;
- manual root resize;
- silent packet drop;
- catch-and-ignore layout/asset errors;
- очистка Unity Library/cache как исправление.

### 17.3. Structured diagnostics

Логи должны содержать стабильные categories:

- `[UIHost]` mount/state/lifecycle;
- `[UILayout]` viewport/safe-area/bucket;
- `[Session]` state transitions;
- `[PacketGate]` stage/drain/limits/generation;
- `[Auth]` auth view lifecycle;
- `[WorldLoad]` readiness gates;
- `[EditorPreview]` create/teardown/restore;
- `[Surface]` material/texture/mesh readiness;
- `[LightingGeometry]` contributor revisions.

Normal frame не логирует повторяющийся status каждый Update.

## 18. Implementation order

### Phase 0. Baseline and protection

- Зафиксировать текущий dirty worktree и не изменять несвязанные файлы.
- Добавить regression tests, которые воспроизводят root `0×0/NaN`, Play hit-test и Play/Stop warning.
- Зафиксировать текущий terrain surface failure render test.
- Удалить временные диагностические hacks только после появления equivalent tests.

### Phase 1. Bootstrap and shell

- Создать scenes через Editor utility.
- Создать AppLifetimeScope.
- Настроить runtime PanelSettings clone.
- Добавить GameShell UXML и UIHost.
- Добавить UI scale/safe area/responsive bucket services.
- Перенести frontend skeleton в UIHost.

### Phase 2. Session coordinator

- Разделить app/world services.
- Реализовать state machine.
- Реализовать MainGame preload/activation.
- Реализовать WorldPacketGate и generation handling.
- Встроить auth OpenWindow/CloseWindow в frontend.
- Реализовать reconnect/back-to-menu teardown.

### Phase 3. Frontend production pass

- Сгенерировать и доработать layered art.
- Реализовать planet shader.
- Реализовать Frontend/Connecting/Auth/Loading/Reconnect states.
- Добавить localization keys/tables.
- Проверить mouse/keyboard/gamepad click/submit.

### Phase 4. Gameplay UI migration

- Перенести HUD и loading/error.
- Перенести packet/modal windows.
- Перенести inventory.
- Перенести chats.
- Перенести pause/settings.
- Перенести Programmator.
- Перенести minimap/world map/FPS.
- Удалить legacy Canvas и direct root mutation.
- Удалить `GameManager.SetupUI()`.

### Phase 5. Input unification

- Нормализовать Input Actions maps.
- Удалить direct keyboard polling.
- Добавить last-used device service.
- Реализовать gamepad focus contracts.
- Реализовать floating touch stick/action cluster.
- Реализовать grid semantic/gesture controls.

### Phase 6. World UI

- Создать WorldUiRoot.
- Сгруппировать nickname/clan/chat.
- Перенести world labels на explicit handles.
- Удалить независимые sorting/offset ownership paths.

### Phase 7. Editor preview

- Удалить runtime `[ExecuteAlways]` preview initialization.
- Создать EditorWorldPreviewHost.
- Подключить actual local map/robot/UI preview.
- Добавить toolbar state switcher.
- Добавить strict teardown перед Play.

### Phase 8. Surface regression

- Перенести local textures в `Assets/Textures/Surface` через Unity asset operations.
- Создать dedicated surface shader/materials.
- Исправить SurfaceRenderer lifecycle/serialized references.
- Создать WorldSurfaceRoot.
- Добавить lighting geometry contributor registry.
- Подключить surface material/emission passes.
- Подтвердить освещение верхних terrain cells.

### Phase 9. Legacy removal and hardening

- Удалить superseded classes/static state.
- Удалить `z-index` и unsupported USS.
- Удалить direct PanelSettings/Canvas scale writes.
- Удалить runtime root Add/Remove paths.
- Добавить startup validation.
- Выполнить полный test/lint matrix.

## 19. Test plan

### 19.1. Unit tests

- Все разрешённые/запрещённые UI state transitions.
- Double Play click создаёт одну session attempt.
- Auth Open/Close routing.
- Auth submit не закрывает view локально.
- Packet FIFO сохраняет order.
- Packets разных generation не смешиваются.
- Packet limits fail-fast.
- Atomic gameplay window replacement.
- Server-authoritative close.
- Reconnect backoff sequence и cap.
- Back to Menu cancellation/teardown.
- Effective UI scale `0.5/1.0/2.0`.
- Responsive bucket thresholds без oscillation.
- Safe-area conversion.
- Last-used device noise/deadzone filtering.
- World label grouped lifetime.
- Surface UV generation.
- Lighting contributor revision behavior.

### 19.2. UI PlayMode tests

Для каждого target viewport и scale:

- root layout finite и больше нуля;
- ни один worldBound не содержит NaN/infinity;
- Play находится внутри safe viewport;
- Play получает pointer click;
- Play получает keyboard Submit;
- Play получает gamepad Submit;
- hidden fullscreen roots не участвуют в picking;
- visible blocker действительно блокирует нижний control;
- modal focus trap работает;
- focus возвращается после close;
- packet body scroll работает после clamp;
- compact layout reflow не перекрывает close/submit;
- long localized text не выходит за action region;
- touch controls остаются внутри safe area;
- scale `2.0` использует reflow/scroll, а не clipping primary actions.

### 19.3. Scene/session integration

- Boot → Frontend.
- Frontend Play → Connecting → authorized → LoadingWorld → Gameplay.
- Invalid token → Auth screen → CloseWindow → Gameplay.
- World packets приходят до scene activation и корректно дренируются.
- Disconnect в LoadingWorld → Reconnecting.
- Disconnect в Gameplay → frozen world + Reconnecting.
- Successful reconnect использует новую generation.
- Back to Menu полностью выгружает world.
- Повторный Play создаёт чистый новый WorldLifetimeScope.

### 19.4. Layout regression

- 20 последовательных Play/Stop циклов.
- 20 Frontend→MainGame→Frontend циклов.
- Domain Reload enabled/disabled.
- Scene Reload enabled/disabled, если режим поддерживается текущей Unity configuration.
- Ни одного `Layout update is struggling`.
- Ни одного root `0×0/NaN` после стабилизации layout.
- Ни одного stale scheduled callback после teardown.
- Root child count стабилен между циклами.
- Event subscriptions не растут между циклами.

### 19.5. Editor preview

- Preview автоматически появляется в Edit Mode.
- Карта загружается из реального local `.mapb`.
- Robot и terrain видимы.
- Gameplay UI отображается по умолчанию.
- Toolbar переключает preview states.
- Preview не изменяет scene dirty flag без пользовательского действия.
- Preview objects не сохраняются в scene.
- Перед Play preview полностью уничтожен.
- После Stop создаётся ровно одна новая preview generation.
- Backup scene restore не запускает runtime presenters.

### 19.6. Surface and lighting

- Transit texture видима.
- Perspective texture видима.
- Materials используют dedicated shader.
- Quad требует только documented vertex attributes.
- UV scroll остаётся непрерывным при camera movement.
- WorldHeight/Top-Left conversion корректен.
- Surface видима на Metal.
- Surface семплирует world light.
- Surface emission пишет в EmissionField.
- Включение emission увеличивает radiance верхних terrain receivers.
- Изменение emission не пересчитывает AO.
- Изменение occupancy меняет geometry revision и пересчитывает AO.
- Surface не создаёт out-of-world terrain cells.
- Закрытая presentation boundary создаёт только viewport-local synthetic
  `RedRock` geometry и никогда не записывает её в `MapStorage`/`.mapb`.

### 19.7. Shader and build validation

- Terrain, surface, frontend и lighting shaders компилируются для Metal.
- URP 2D renderer продолжает работать.
- HDR pipeline остаётся включён.
- SDR display policy не меняется.
- BuildScript включает новые referenced textures/scenes.
- MonoScript.GetClass() не null для новых Unity types.

### 19.8. C# lint

После C# изменений обязательно выполнить:

```bash
dotnet build Assembly-CSharp.csproj -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary 2>&1
CI=true ./scripts/pre-commit-lint.sh
```

Перед финальной передачей также выполнить применимые Unity EditMode/PlayMode tests и `pre-commit run --all-files`, если он не затрагивает несвязанные пользовательские изменения.

Все `SA`, `CA`, `RCS`, `S`, `UNT` warnings исправляются, а не подавляются обходами.

## 20. Acceptance criteria

Работа считается завершённой только если:

- Play находится в утверждённой Orbital Arrival composition и всегда кликабелен.
- Frontend является отдельной scene domain, а UIHost переживает world transitions.
- Auth OpenWindow/CloseWindow работает внутри frontend.
- Loader не скрывается до полной world readiness.
- Reconnect бесконечный и имеет Back to Menu.
- Gameplay packets не теряются между auth и WorldLifetimeScope startup.
- Ни один UI controller не добавляет static tree напрямую в UIDocument root.
- PanelSettings меняет только UIScaleService.
- Нет runtime-created ScreenSpaceOverlay Canvas для FPS/minimap/world map.
- Mouse, keyboard, gamepad и touch contracts проходят tests.
- Edit Mode показывает UI, карту и робота без runtime ExecuteAlways.
- 20 Play/Stop cycles не дают recursive layout warning.
- Transit/perspective снова видимы.
- Surface использует dedicated shader и освещает terrain через emission.
- Слева, справа и снизу находится бесконечный красноскал, использующий штатный
  terrain atlas, relief/connectivity, automatic normals и physical occupancy.
- Future 3D surface content может подключиться через WorldSurfaceRoot/lighting contributor без изменения terrain shader contract.
- C# build/lint и Unity tests проходят без analyzer warnings.

## 21. Зафиксированные assumptions и tradeoffs

- Gameplay UI мигрирует архитектурно с текущей visual semantics; его полный redesign будет отдельной задачей.
- Frontend/loading/reconnect получают новый утверждённый visual language сейчас.
- Новый frontend art генерируется и вручную дорабатывается; старый фон не переиспользуется.
- Server auth packets остаются без изменений.
- Gameplay packet window на экране одно; новый Open атомарно заменяет старый.
- Server window dimensions используют reference units, client имеет право clamp outer frame к safe viewport.
- UI scale range остаётся `0.5–2.0`.
- Mobile landscape responsive behavior и touch input проектируются/тестируются, но mobile builds не входят.
- Gamepad входит в обязательный acceptance.
- World labels показываются все; overlap разных объектов допускается.
- Only app settings/locale/input mode переживают Back to Menu; gameplay view state и drafts очищаются.
- Disk asset cache сохраняется, session/world RAM очищается.
- Edit Mode preview автоматический, а не manual command.
- Surface textures пока локальные serialized Unity assets.
- Surface emission физически освещает terrain.
- Полная 3D surface scene не входит, но API под неё обязателен.
- Existing unrelated dirty worktree changes сохраняются.
- Prefabs/scenes/assets не редактируются как YAML.
- Unity cache никогда не считается причиной или исправлением дефекта.

## 22. Системный рефактор world-render pipeline

### 22.1. Статус и приоритет

Этот раздел является обязательным продолжением плана и имеет приоритет над
старыми формулировками разделов 15, 17, 18, 19 и 20 при конфликте.

Причина добавления — серия связанных регрессий, показавшая отсутствие единого
render contract:

- исчезновение `transit`/`perspective` после назначения terrain material;
- чёрный экран при корректно построенном terrain mesh;
- мёртвый `LightingGeometryRegistry`, зарегистрированный, но не вызываемый solve;
- декоративная texture верхней поверхности, ошибочно применённая к боковым граням;
- отдельный boundary shader без terrain atlas/relief/occupancy semantics;
- переход освещения от попиксельного представления к визуально поблочному;
- отсутствие единого readiness/error/threading contract между asset loading,
  geometry, lighting и presentation.

До завершения Phase R0–R4 запрещены новые точечные visual fixes, добавляющие
ещё один material, runtime mesh, global shader property или fallback path вне
описанной ниже архитектуры.

### 22.2. Зафиксированная регрессия по screenshot 2026-08-08 18:19:58

Наблюдаемая композиция считается неверной по следующим причинам:

1. Левая масса `RedRock` заканчивается на уровне верхней границы мира, поэтому
   над ней возникает синяя прямоугольная ступенька.
2. `perspective.png` повторяется как короткий world tile и образует вертикальные
   зелёно-жёлтые колонны вместо непрерывной перспективной поверхности.
3. `transit` и `perspective` имеют разные, визуально несогласованные масштабы.
4. Верхняя поверхность пересвечена и не принадлежит той же экспозиции, что
   непосредственно прилегающий terrain.
5. Красноскал и верхняя поверхность встречаются без формального corner policy.
6. Освещение terrain и неблочных объектов визуально квантуется по клеткам.

Этот screenshot становится обязательным regression fixture. Golden image не
используется как единственный oracle: каждый перечисленный дефект проверяется
отдельным structural/render assertion.

### 22.3. Целевой causal graph

Единственный разрешённый путь world visual data:

```text
Server world/config
  -> Asset manifests and typed asset handles
  -> Atlas/material readiness
  -> WorldRenderSnapshot
  -> Geometry producers
  -> Full-resolution Material/Occupancy/Emission raster
  -> Radiance Cascades solve
  -> Edge-aware lighting reconstruction
  -> Terrain/Surface/Entity presentation
  -> UI compositing
```

Ни одна стадия не читает состояние более поздней стадии. Presentation shader
не определяет physical occupancy. Asset loader не создаёт UI. Geometry builder
не публикует global shader state. Lighting solve не ищет объекты сцены.

### 22.4. Владение доменами

| Domain | Владелец | Выход | Запрещено |
|---|---|---|---|
| World data | `MapStorage`/`MapManager` | typed cell/config snapshot | textures, materials, UI |
| Assets | `WorldTextureManager` | atlas handles + revision | mesh rebuild policy |
| Terrain geometry | `TerrainGeometryProducer` | terrain vertex/index batches | lighting solve |
| Boundary geometry | `WorldBoundaryGeometryProducer` | synthetic RedRock batches | запись в `.mapb` |
| Surface geometry | `SurfaceGeometryProducer` | transit/perspective meshes | terrain-cell подстановки |
| Entity geometry | entity-specific contributors | actual mesh/sprite coverage | cell-sized proxy quads |
| Geometry raster | `LightingGeometryRasterizer` | full-res fields | radiance propagation |
| Radiance solve | `RadianceCascadeSolver` | radiance textures | scene scans, asset loading |
| Reconstruction | `WorldLightingReconstructor` | `_WorldLightTexture` | изменение occupancy |
| Presentation | typed renderers | final world fragments | физические fallback-решения |

`SingleMeshTerrainRenderer` может временно реализовывать несколько внутренних
interfaces, но публичные обязанности остаются разделёнными. Разделение не
требует немедленно создавать отдельный GameObject на каждый domain.

### 22.5. WorldRenderCoordinator

Вводится один coordinator world-lifetime уровня со state machine:

```text
WaitingForWorld
  -> WaitingForAssets
  -> BuildingGeometry
  -> RasterizingGeometry
  -> SolvingLighting
  -> Ready
  -> Failed
```

Правила:

- переходы однонаправленные в рамках одной generation;
- reconnect/world reload создаёт новую generation;
- callback старой generation не может изменить новую;
- каждая стадия имеет typed input/output и revision;
- `Ready` публикуется только после первого завершённого reconstruction;
- исключение переводит coordinator в `Failed` ровно один раз;
- `Update`/`LateUpdate` не выбрасывают одинаковое исключение каждый frame;
- loader скрывается только после `Ready`;
- failure содержит stage, generation, revisions и исходное exception.

### 22.6. Revision graph

Используются четыре независимых monotonic revision:

- `AssetRevision` — atlas/texture/material handle изменился;
- `GeometryRevision` — positions, topology или physical coverage изменились;
- `RadianceRevision` — emission/source/extinction изменились;
- `PresentationRevision` — только visual color/UV/exposure изменились.

Зависимости:

```text
AssetRevision -> GeometryRevision only when vertex/material contract changed
GeometryRevision -> MaterialField + AO + automatic normals + radiance
RadianceRevision -> radiance + reconstruction, but not AO
PresentationRevision -> visible draw only
```

Camera movement не меняет `GeometryRevision` статической поверхности. Visible
camera-local mesh и world-stable lighting mesh являются разными views одного
typed geometry source.

### 22.7. Full-resolution geometry fields

Клетка остаётся единицей streaming, server coordinates и atlas variation, но
не является минимальной единицей освещения.

Обязательные поля geometry raster:

- `MaterialAlbedoField` — surface albedo;
- `OccupancyField` — физическая coverage 0…1;
- `EmissionField` — HDR emission;
- `AutomaticNormalField` — normal, восстановленная из full-res occupancy;
- `ContactOcclusionField` — persistent full-res AO.

Размер geometry fields определяется фактической camera pixel density и
physical viewport, а не только `gridWidth * LightingPixelsPerCell`.
Максимум — native viewport resolution с необходимым safe border. Retina scale
учитывается явно через render target descriptor.

Запрещено снижать spatial fidelity geometry fields при переключении Low/Medium.
Quality tiers могут менять:

- число cascade directions;
- probe spacing;
- interval count/ray steps;
- radiance atlas resolution;
- update frequency dynamic sources;
- internal bounce resolution.

Quality tiers не могут превращать mesh coverage в cell coverage.

### 22.8. Разделение raster resolution и solve resolution

Material/occupancy/emission raster выполняется в full resolution. Radiance
Cascades может решаться в более низком internal resolution.

Reconstruction обязан:

- восстанавливать radiance в full-resolution `_WorldLightTexture`;
- учитывать discontinuities `OccupancyField` и `AutomaticNormalField`;
- не размазывать свет через физическую границу;
- не создавать cell-sized plateaus;
- сохранять sub-cell источники и тонкие meshes;
- выполнять ambient ровно один раз;
- сохранять direct emission отдельно от AO.

Обычный bilinear upscale без geometry guidance не принимается.

### 22.9. Terrain physical coverage

Terrain продолжает использовать семь документированных vertex channels.

Для обычных terrain cells:

- physical mass определяется серверными properties;
- visual alpha/animation не меняют массу;
- roundable loose geometry использует actual analytic/mesh contour;
- соседние solid cells образуют непрерывную occupancy без внутренних швов;
- relief/connectivity относится к presentation/coverage contract, а не к
  скрытому затемнению texture;
- `LightingMaterialField` растеризует фактическую coverage каждого fragment.

Нельзя заменять full-res contour одним boolean `isPhysicalMass` на всю клетку,
если producer объявляет неблочную форму.

### 22.10. Неблочные структуры

Robot, tentacle, future structures, surface meshes и другие неблочные объекты
публикуют geometry через `ILightingGeometryContributor`.

Contributor обязан предоставить:

- stable identity и lifecycle owner;
- visible mesh/material handle;
- lighting mesh или exact coverage source;
- physical occupancy policy;
- emission policy;
- geometry и radiance revisions;
- world bounds для culling.

Запрещены cell proxy, bounding-box occupancy и CPU readback texture alpha.
Sprite alpha может участвовать в visual coverage только через GPU raster pass;
физическая occupancy задаётся отдельной explicit mask/mesh policy.

### 22.11. World boundary policy

Классификация выполняется в следующем порядке:

```text
if x < 0 or x >= WorldWidth:
    ClosedRedRock
else if unityY < 0:
    ClosedRedRock
else if unityY >= WorldHeight:
    OpenSurface
else:
    StoredWorldCell
```

Следствия:

- левая и правая стены являются бесконечными по вертикали;
- нижняя масса является бесконечной по горизонтали;
- открытый верх существует только над диапазоном `0 <= x < WorldWidth`;
- верхние углы не создают синюю ступеньку;
- boundary geometry viewport-local, но UV/variation world-stable;
- boundary не записывается в `MapStorage`, network packets или `.mapb`;
- boundary использует `CellType.RedRock` config, atlas, terrain shader,
  full vertex contract, occupancy, automatic normals и extinction;
- boundary никогда не использует отдельный shader/material fallback.

### 22.12. Transit и perspective projection

`transit` и `perspective` имеют разные projection semantics.

`transit`:

- world-aligned horizontal strip;
- повторяется по X с документированным tile length;
- UV anchor зависит от world X, а не от mesh-local origin;
- не растягивает clamp edge texture;
- нижняя линия точно совпадает с `WorldHeight`.

`perspective`:

- camera-projected непрерывная поверхность;
- один projection span на видимую ширину, без повторения полного PNG каждые
  пять клеток;
- допускается слабый world-space parallax, заданный одним параметром;
- aspect/vertical scale задаются explicit `SurfaceProjectionSettings`;
- texture не образует вертикальных колонн на screenshot regression case.

Обе bands:

- имеют отдельные visible и lighting meshes;
- visible mesh может быть camera-local;
- lighting mesh world-stable и не увеличивает geometry revision при движении;
- семплируют lighting из валидной области верхних receivers;
- только явно указанная верхняя band даёт emission;
- не используются для левой, правой или нижней границы.

### 22.13. Shader contract system

Вводятся typed descriptors:

- `TerrainShaderContract`;
- `SurfaceShaderContract`;
- `LightingFieldContract`;
- `WorldLightingGlobalsContract`.

Descriptor содержит required passes, properties, keywords, vertex layout и
render-target formats. String IDs объявляются один раз в contract type.

Editor validator проверяет:

- shader существует и поддерживается URP/Metal;
- все required passes найдены;
- material properties и formats совпадают;
- vertex descriptors совпадают с shader attributes;
- MRT count/formats корректны;
- material field не очищается между terrain и contributors;
- global texture/rect публикуются согласованной revision;
- ни один presentation material не используется как physical producer без
  объявленного lighting pass.

### 22.14. Threading и async contract

Все Unity objects, textures, materials, meshes, command buffers и UI живут на
Unity main thread.

Asset decode может выполняться в background, но результат проходит границу:

```text
Download bytes -> verify/decode data -> SwitchToMainThread -> create/upload Unity object
```

Вводятся правила анализатора:

- `FODR001` — UnityEngine.Object API из метода, способного выполняться вне main thread;
- `FODR002` — `UniTaskVoid`/`.Forget()` без централизованного exception handler;
- `FODR003` — fire-and-forget callback без lifetime cancellation token;
- `FODR004` — изменение Mesh/Material/Texture из network callback до main-thread dispatch;
- `FODR005` — blocking `.Result`/`.Wait()` в Unity lifecycle;
- `FODR006` — runtime global scan в frame callback;
- `FODR007` — создание/уничтожение render resource в `Update`/`LateUpdate`;
- `FODR008` — shader property/pass string вне typed contract;
- `FODR009` — exception throw path, повторяемый каждый frame без state transition;
- `FODR010` — revision field изменяется не своим domain owner.

Для runtime debug builds добавляется `MainThreadGuard.Assert()` на границах
asset upload, mesh upload и command buffer submission.

### 22.15. Error model

Используется typed `RenderFailure`:

```text
Code
Stage
WorldGeneration
AssetRevision
GeometryRevision
RadianceRevision
Resource/Cell/Contributor identity
Original exception
Recovery policy
```

Категории recovery:

- `FatalWorldData` — disconnect и error screen;
- `FatalRenderContract` — остановка world activation;
- `RetryableAssetTransport` — bounded retry до world-ready timeout;
- `DeviceCapabilityMismatch` — явная unsupported-platform ошибка;
- `SessionSuperseded` — тихая отмена только старой generation;
- `UserCancellation` — штатный teardown.

Запрещены общий `catch (Exception) { LogWarning; continue; }`, generated texture,
подмена материала и продолжение world-ready после failed required stage.

### 22.16. Structured diagnostics

F12 snapshot дополняется секциями:

```text
RenderCoordinator: state/generation/failure
Assets: requested/ready/failed/revision
Atlas: count/formats/cell mappings/revision
Geometry: producers/bounds/vertex layouts/revisions
Fields: dimensions/formats/pixel density/revisions
Lighting: cascade layout/solve revision/timing
Reconstruction: input/output revision/debug mode
Presentation: renderer/material/global bindings
Threads: main-thread id/pending dispatches
```

Для каждого кадра лог не пишется. Ring buffer хранит последние 64 state/revision
transitions и выгружается только по F12 или failure.

### 22.17. Инструменты причинно-следственной диагностики

Editor window `World Render Inspector` показывает:

- causal graph стадий;
- текущий owner каждого resource;
- producer/consumer revisions;
- Material, Occupancy, Emission, Automatic Normal, AO, Direct Radiance,
  Diffuse Bounce и Final Lighting;
- pixel density каждого field;
- world/camera rect и boundary classification;
- contributor meshes и bounds;
- причину, по которой стадия dirty или blocked.

Инспектор read-only по умолчанию. Изменение debug view не мутирует production
settings и не создаёт fallback resources.

### 22.18. Test matrix

Unit:

- boundary classification с приоритетом side over open top;
- revision dependency graph;
- coordinator state transitions;
- stale generation cancellation;
- typed failure classification;
- field-size calculation по viewport pixel density;
- quality tier не меняет geometry coverage.

Geometry integration:

- `RedRock` boundary проходит тот же seven-channel contract, что stored cell;
- boundary отсутствует в `MapStorage` после render;
- side/bottom occupancy равна физической массе RedRock;
- top open region не получает synthetic terrain occupancy;
- solid boundary не имеет внутренних occupancy seams;
- round/non-block mesh сохраняет sub-cell contour.

Lighting:

- тонкая диагональная geometry не превращается в staircase размером с клетку;
- sub-cell emitter сохраняет форму в EmissionField;
- automatic normals следуют full-res occupancy;
- RedRock ослабляет direct radiance по solid extinction;
- AO меняется только от geometry revision;
- emission-only change не запускает AO;
- reconstruction не пропускает свет через occupancy edge;
- Low/Ultra различаются стоимостью, но не topology света.

Surface:

- transit world anchor непрерывен при camera movement;
- perspective не повторяет полный PNG короткими колоннами;
- left/right RedRock продолжаются выше `WorldHeight`;
- top surface существует только внутри world X range;
- corner не содержит gap/overlap/z-fighting;
- surface lighting mesh не меняет revision при camera pan;
- emission верхней band попадает в общий MRT после terrain без clear.

Threading/error:

- background asset completion не трогает Unity object;
- cancellation teardown не публикует stale texture;
- `.Forget()` exception достигает central handler;
- fatal shader contract не повторяет exception каждый frame;
- world-ready невозможен после required render failure.

Performance:

- неподвижная камера не аллоцирует render resources;
- camera pan не пересобирает static lighting geometry каждый frame;
- AO не dispatch-ится от dynamic light movement;
- field memory укладывается в рассчитанный budget;
- 20 world reloads не увеличивают native texture/mesh/material count.

### 22.19. Реализация по фазам

#### Phase R0. Freeze и executable baseline

- Зафиксировать screenshots и RenderDoc/Frame Debugger captures дефектов.
- Добавить boundary, field-resolution и contributor regression tests.
- Зафиксировать GPU timings и resource counts.
- Не менять visual coefficients до появления tests.

#### Phase R1. Contracts и coordinator

- Ввести typed render contracts/revisions/failures.
- Реализовать `WorldRenderCoordinator`.
- Подключить world-ready к coordinator `Ready`.
- Удалить повторяемые throw paths из frame callbacks.

#### Phase R2. Terrain и boundary unification

- Выделить единый `WorldBoundaryPolicy`.
- Перенести closed RedRock geometry в terrain producer.
- Удалить отдельные boundary meshes/materials/shader branches.
- Проверить atlas/relief/occupancy/extinction end-to-end.

#### Phase R3. Full-resolution geometry raster

- Разделить geometry-field и radiance-solve resolutions.
- Растеризовать actual terrain/entity/surface coverage.
- Перенести automatic normals и AO на full-res occupancy.
- Добавить edge-aware reconstruction.

#### Phase R4. Surface projection

- Разделить transit и perspective projection settings.
- Устранить clamp/repeat ambiguity.
- Разделить visible/lighting meshes.
- Зафиксировать corner composition и exposure.

#### Phase R5. Contributors и неблочные структуры

- Подключить Surface, Robot, Tentacle и structures через registry.
- Удалить cell proxies и параллельные lighting paths.
- Добавить culling и revisions без scene scans.

#### Phase R6. Analyzer и diagnostics

- Реализовать `FODR001–FODR010`.
- Добавить `World Render Inspector`.
- Расширить F12 snapshot и failure ring buffer.
- Подключить analyzer к pre-commit и CI.

#### Phase R7. Hardening

- Пройти Metal/URP shader validation.
- Пройти full C#/shader/test matrix.
- Сравнить GPU memory/timing с R0 baseline.
- Удалить superseded diagnostics и legacy contracts.

### 22.20. Definition of done

Рефактор завершён только когда:

- visual и physical geometry имеют одного объявленного owner;
- RedRock boundary не имеет отдельного shader path;
- screenshot regression не содержит синей corner-ступеньки и perspective columns;
- неблочные структуры получают sub-cell, визуально попиксельное освещение;
- quality tier не меняет topology/coverage света;
- Material/Emission fields terrain и contributors собираются одним MRT без clear;
- occupancy, automatic normals, AO и extinction согласованы;
- world-ready зависит от первого успешного lighting reconstruction;
- все async Unity writes проходят main-thread boundary;
- `FODR001–FODR010` включены как errors;
- render failure имеет typed cause и не повторяется каждый frame;
- F12 однозначно показывает блокирующую стадию и revision mismatch;
- C# analyzers, shader validation, EditMode/PlayMode и render regression tests
  проходят без warnings/errors.

## 23. Единый источник defaults для всего проекта

Статус: обязательный сквозной рефактор. Он выполняется после инвентаризации,
параллельно с доменными фазами UI/world-render, но до удаления legacy-кода.

Цель: во всём проекте существует ровно один вручную поддерживаемый источник
осознанных стартовых значений:

`Assets/Resources/Configuration/ProjectDefaults.asset`

`ProjectDefaults.cs` описывает только сериализуемую типизированную схему и не
содержит product values. Таким образом Unity asset является source of truth, а
C# остаётся компилируемым контрактом.

Это не разрешение добавлять fallback на отсутствующие данные. Defaults отвечают
только на вопрос «какое валидное значение получает новая локальная настройка до
первого изменения пользователем». Они не маскируют отсутствие обязательного
runtime-состояния.

### 23.1. Сначала классифицировать значения

Перед переносом каждое похожее на default значение занести в inventory с owner,
типом, источником и потребителями. Для каждого кандидата выбрать ровно одну
категорию:

1. **User/product default** — стартовая локальная настройка. Переносится в
   `ProjectDefaults.asset`.
2. **Domain invariant** — размер чанка, размер клетки, формат пакета, число
   каскадов, геометрический предел или enum value. Остаётся рядом с доменным
   контрактом и не называется default.
3. **Server-authoritative value** — размеры мира, spawn, cooldown, cell config,
   emission, asset identifiers и доступные каналы. Клиентского default нет;
   отсутствие значения завершает соответствующую загрузку typed failure.
4. **Derived value** — вычисляется из authoritative input/default/invariant и не
   хранится второй копией.
5. **Serialized authored value** — часть конкретного prefab/profile/material.
   Это authored data, а не глобальный default. Если значение должно быть общим,
   asset становится генерируемым представлением центрального default.
6. **Test fixture** — локальные данные конкретного теста. Они именуются fixture,
   не подключаются к production defaults и не используются runtime-кодом.
7. **Sentinel** — `null`, `-1`, invalid handle, `int.MinValue` для явного
   состояния «не инициализировано». Sentinel остаётся в typed state machine и не
   подменяет default.

Inventory обязан покрыть:

- field/property initializers и конструкторы;
- `[SerializeField]` initializers;
- optional parameter values;
- `PlayerPrefs.Get*`, JSON/config deserialization и migration code;
- `?? literal`, `GetValueOrDefault(literal)`, catch/fallback branches;
- `Default*`, `Fallback*`, `Initial*`, `Preset*` и локальные `const`;
- quality profiles, UI, input, audio, camera, cache, networking и rendering;
- Editor utilities, asset generators и `Reset()`/`OnValidate()`;
- runtime-создание материалов, текстур, профилей и GameObject settings.

### 23.2. `ProjectDefaults` как нативный Unity-контракт

`ProjectDefaults.cs` содержит один `ScriptableObject` с сериализуемыми
тематическими группами, например:

```csharp
public sealed class ProjectDefaults : ScriptableObject
{
    [SerializeField] private int _schemaVersion;
    [SerializeField] private UserInterfaceDefaults _userInterface;
    [SerializeField] private InputDefaults _input;
    [SerializeField] private AudioDefaults _audio;
    [SerializeField] private CameraDefaults _camera;
    [SerializeField] private GraphicsDefaults _graphics;
    [SerializeField] private LightingDefaults _lighting;
    [SerializeField] private WorldPresentationDefaults _worldPresentation;
}
```

Пример иллюстрирует форму, а не разрешает inline initializers: значения задаются
только в единственном asset через Inspector. Точный состав определяется
inventory. Требования к схеме и asset:

- группы — `[Serializable]` typed records/classes без самостоятельных assets;
- runtime получает immutable validated snapshot через `IProjectDefaults`;
- у каждого значения есть единица измерения в имени или типе;
- связанные значения представлены typed group, а не россыпью
  несвязанных `float`;
- диапазон, конечность и cross-field constraints проверяются до регистрации DI;
- schema не выполняет `Resources.Load`, `AssetDatabase`, scene lookup или
  service resolution;
- asset не хранит scene objects, runtime services, material instances, maps или
  другие обязательные данные под видом defaults;
- после bootstrap runtime не мутирует ScriptableObject;
- значения не меняются по quality tier скрытыми fallback-коэффициентами;
- изменение схемы или migration semantics требует увеличения `SchemaVersion`.

Nested groups нужны для Inspector, навигации и типизации. Они не становятся
отдельными источниками: запрещены другие default ScriptableObject assets,
`*Defaults.cs` со значениями, hidden presets и локальные fallback tables.

### 23.3. Bootstrap и DI

Порядок инициализации:

1. bootstrap загружает required `ProjectDefaults.asset` по одному стабильному
   resource contract;
2. проверяет, что найден ровно один asset ожидаемого типа;
3. валидирует schema version, диапазоны и cross-field constraints;
4. строит immutable `ProjectDefaultsSnapshot`;
5. регистрирует snapshot как `IProjectDefaults` в `GameLifetimeScope`;
6. только после этого создаются config stores, UI, audio и render pipeline.

Отсутствующий, дублирующийся или невалидный asset — startup failure, а не набор
C# fallback values. Потребители получают нужную typed group через DI и не имеют
доступа к mutable ScriptableObject. Editor utilities читают тот же asset через
явный provider и проходят ту же validation.

### 23.4. Что запрещено помещать в defaults

В `ProjectDefaults.asset` и в любом другом месте запрещены fallback-значения для:

- world width/height, player/server position и spawn;
- отсутствующих `.mapb`, terrain chunks и cell configurations;
- server config, cooldowns, permissions и сетевых идентификаторов;
- auth/session state и connection endpoints, получаемых из обязательного config;
- отсутствующих textures, sprites, materials, shaders, FMOD banks и maps;
- `_WorldLightTexture`, atlas, occupancy/emission fields и render contracts;
- `PanelSettings`, UXML, theme, renderer feature и camera references;
- server-authoritative emission и physical occupancy;
- любого значения, отсутствие которого означает повреждённую сборку,
  несовместимый протокол или незавершённую загрузку мира.

Для них действует fail-fast с typed причиной. Нельзя генерировать белую текстуру,
пустую карту, `(0, 0)`, случайный материал, dummy profile или продолжать загрузку
с логом warning.

### 23.5. Typed settings вместо строк и литералов

Typed key, validation и migration contract объявляются в C# schema, но
`InitialValue` берётся из соответствующего поля `ProjectDefaults.asset`:

```text
SettingDefinition<T>:
  Key
  InitialValue
  Validate(value)
  PersistenceScope
  MigrationPolicy
```

Потребители работают с typed setting, а не повторяют key и fallback literal.
`PlayerPrefs.GetInt("...", 1)` и аналоги удаляются. Store получает definition,
проверяет сохранённое значение и возвращает:

- persisted value, если key существует и значение валидно;
- `InitialValue`, только если key действительно никогда не существовал;
- typed migration result, если schema устарела;
- typed error, если данные повреждены и migration их не покрывает.

Повреждённое значение нельзя тихо заменить default. Сброс пользовательских
настроек — отдельная явная команда UI/diagnostics.

### 23.6. Serialized fields и производные Unity assets

Простая замена initializer не исправляет уже сериализованные значения Unity.
Поэтому migration разделяется на два пути:

- runtime settings читают injected immutable `IProjectDefaults` snapshot;
- общие serialized profiles/material settings генерируются Editor API из
  `ProjectDefaults.asset`, а производный asset хранит source `SchemaVersion` и
  deterministic content hash;
- конкретные authored значения prefab/scene не объявляются глобальными
  defaults и остаются частью этого объекта;
- `Reset()` и custom inspectors применяют центральный definition, не копируют
  literals;
- validator сравнивает generated asset hash с текущим source asset hash и завершает
  build ошибкой при рассинхронизации;
- runtime не чинит и не пересохраняет assets автоматически.

`.prefab`, `.unity` и `.asset` изменяются только Unity Editor API/Inspector.
Текстовая правка YAML в этом треке запрещена.

### 23.7. Владение и зависимости

Направление зависимостей строгое:

```text
ProjectDefaults.asset
  -> validated immutable IProjectDefaults snapshot
  -> typed config/settings definitions
  -> domain adapters
  -> UI / Audio / Input / Rendering consumers

Server packet / required asset / authored scene data
  -> validation
  -> runtime domain state
  -X-> ProjectDefaults fallback
```

Доменные системы не должны ссылаться друг на друга ради получения стартового
значения. Defaults не знают о DI, scene lifecycle, network transport или asset
pipeline. Runtime override возможен только через явно типизированный config или
server-authoritative state; он не мутирует source asset или snapshot.

### 23.8. Миграция существующих пользователей

Изменение product default не должно неожиданно перезаписывать валидный выбор
существующего пользователя:

- новая установка получает актуальное значение из source asset;
- существующий валидный key сохраняется;
- отсутствующий новый key получает значение из source asset;
- устаревший schema version проходит явную versioned migration;
- повреждённый config выдаёт typed failure либо предлагает явный reset;
- migration логирует old schema, new schema и список преобразованных keys без
  вывода секретов;
- migration idempotent: повторный запуск не меняет результат.

Для config-файлов хранить schema version отдельно от версии приложения. Каждая
migration покрывается fixture старой версии и expected snapshot новой.

### 23.9. Analyzer defaults-flow

Добавить Roslyn analyzer и включить правила как errors:

- `FODD001`: production field/property/serialized initializer содержит
  неклассифицированный default literal;
- `FODD002`: `PlayerPrefs.Get*` или config read получает inline fallback;
- `FODD003`: optional parameter кодирует product default вне central file;
- `FODD004`: `??`, catch или switch silently подставляет runtime fallback;
- `FODD005`: найден второй `Default*`/`Fallback*` источник production values;
- `FODD006`: server-authoritative или required asset value получает client
  default;
- `FODD007`: runtime генерирует placeholder texture/material/map/position;
- `FODD008`: schema/source asset зависит от runtime service, scene object или
  required data, не являющихся defaults;
- `FODD009`: generated Unity representation имеет устаревшие schema/hash;
- `FODD010`: persisted schema изменена без versioned migration/test.

Analyzer не должен ругаться на:

- математические литералы внутри локальной формулы;
- enum discriminants и protocol/domain invariants;
- explicit sentinels typed state machine;
- test fixture data;
- значения, сгенерированные из source asset и подтверждённые hash.

Для спорных мест suppression разрешён только с category, owner и объяснением,
проверяемым analyzer-ом. Голый `SuppressMessage` запрещён.

### 23.10. Runtime validation и диагностика

На startup до авторизации UI:

- загрузить единственный source asset и проверить его schema;
- проверить внутренние диапазоны и cross-field constraints;
- проверить config schema и migrations;
- проверить generated asset hashes;
- не разрешать world-ready при required-data failure;
- публиковать один typed diagnostic с owner и remediation;
- не повторять одну и ту же ошибку каждый frame.

F12 snapshot расширить разделом `Defaults`:

- asset GUID, `SchemaVersion` и content hash;
- persisted config schema;
- выполненные migrations;
- список generated artifacts и совпадение hash;
- active user-setting sources: persisted или initial;
- обнаруженные violations без вывода значений секретных keys.

### 23.11. Порядок реализации

#### Phase D0. Inventory без поведения

- Запустить статический поиск по всем production и Editor assemblies.
- Составить machine-readable classification manifest.
- Для каждого кандидата назначить domain owner.
- Зафиксировать config snapshots и generated asset hashes.

#### Phase D1. Центральная схема и asset

- Создать `ProjectDefaults.cs`, typed groups и `IProjectDefaults` snapshot.
- Создать единственный `ProjectDefaults.asset` через Unity Editor API.
- Добавить schema validation и deterministic hash.
- Покрыть каждую группу unit tests.
- Пока не менять persisted values и serialized assets.

#### Phase D2. Core/user settings migration

- Перевести UI, input, audio, camera и graphics user settings.
- Добавить versioned config migrations.
- Удалить string keys и fallback literals из consumers.
- Проверить clean install, upgrade и corrupted config paths.

#### Phase D3. Render/world audit

- Перенести только presentation/quality defaults.
- Переклассифицировать geometry constants как invariants.
- Удалить client fallbacks для server/world/asset data.
- Связать defaults revision с `WorldRenderCoordinator`, не с geometry revision.

#### Phase D4. Editor/generated assets

- Реализовать генератор через Unity Editor API.
- Проставить schema/hash в generated artifacts.
- Добавить build validation и inspector для drift.
- Не редактировать scene/prefab/asset YAML вручную.

#### Phase D5. Enforcement и cleanup

- Включить `FODD001–FODD010` как errors.
- Удалить legacy `Default*`, `Fallback*` и дублирующие profiles.
- Подключить inventory check к pre-commit/CI.
- Выполнить полный C# lint, EditMode/PlayMode и config migration matrix.

### 23.12. Обязательные тесты

Static/analyzer:

- в проекте существует ровно один authoritative `ProjectDefaults.asset`;
- C# schema не содержит product default values;
- нет inline user/product fallback literals;
- нет `PlayerPrefs.Get*` с fallback argument;
- server-authoritative paths не зависят от `IProjectDefaults`;
- generated artifacts соответствуют schema/hash;
- test fixtures не импортируются production assemblies.

Behavior:

- clean install получает актуальные initial values;
- existing valid settings переживают изменение product defaults;
- новый key получает значение из source asset;
- все historical config fixtures мигрируют idempotently;
- corrupted value не заменяется тихо;
- missing map/texture/material/server config завершает pipeline typed failure;
- reset settings выполняется только явным действием пользователя;
- смена graphics default не инвалидирует terrain geometry;
- смена lighting quality default инвалидирует только заявленные render stages.

Unity integration:

- generated profiles создаются и обновляются Editor API;
- missing/duplicate/invalid source asset останавливает startup и build;
- stale hash блокирует build;
- `MonoScript.GetClass()` валиден для добавленных Unity script types;
- scene/prefab authored values не перезаписываются массово;
- runtime не создаёт placeholder assets и не сохраняет project assets.

### 23.13. Definition of done

Рефактор defaults завершён только когда:

- `Assets/Resources/Configuration/ProjectDefaults.asset` — единственный вручную
  поддерживаемый источник product/user defaults;
- `ProjectDefaults.cs` содержит только schema/validation, но не дублирует
  значения asset;
- каждый найденный literal-кандидат классифицирован как default, invariant,
  authoritative input, derived value, authored data, fixture или sentinel;
- в проекте нет вторых default tables, hidden presets и silent fallbacks;
- обязательные network/world/assets данные fail-fast и не получают defaults;
- persisted settings имеют versioned, idempotent migrations;
- generated Unity artifacts имеют совпадающие schema/hash;
- изменение default не перезаписывает валидный выбор существующего пользователя;
- `FODD001–FODD010` включены как errors;
- diagnostics показывают источник каждого активного setting;
- C# analyzers, config migrations, EditMode/PlayMode и build validation проходят
  без warnings/errors.
