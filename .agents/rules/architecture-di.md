# Architecture & VContainer DI Rules

## 1. Внедрение зависимостей (VContainer)
* Проект мигрировал с устаревших монолитных `.Instance` синглтонов на **VContainer DI**.
* Точкой сборки контейнера является `GameLifetimeScope` в `Assets/Scripts/Core/GameLifetimeScope.cs`.
* Зависимости передаются через конструкторы, интерфейсы или атрибут `[Inject]` для компонентов MonoBehaviour.

## 2. Разделение слоя мира и ассетов
* Сервер передает только координаты и идентификаторы состояний.
* Клиент загружает тяжелые ассеты (текстуры, спрайты, FMOD банки) однократно в `PersistentAssetCache` и RAM-кэши (`CellTextureCache`, `AssetCache`).
