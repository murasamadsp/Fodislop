# C# и структура

Читать перед написанием или редактированием любого C# файла в проекте.

## Язык и nullable

- Включено `#nullable enable`; reference types обозначайте nullable/non-null явно.
- Используйте C# 12 (`primary constructors`, `readonly record struct`, collection expressions), когда это уместно.

## Namespace

- Обычные типы имеют **file-scoped namespace**.
- Типы-наследники `MonoBehaviour`, `ScriptableObject`, `ScriptableRendererFeature` или `VolumeComponent` имеют **block namespace** — иначе `MonoScript.GetClass()` может вернуть `null`.

## Стиль

- Allman braces, обязательные `{}`, SA1513/SA1508.
- Trailing comma в многострочных инициализаторах.
- Приватные поля — `_camelCase`; публичные члены и типы — `PascalCase`.
- Имя Unity-скрипта должно совпадать с классом.

## DI / VContainer

- Не создавайте менеджеры через `AddComponent` в `Configure`.
- Scene-компоненты регистрируйте через `RegisterComponent`; prefab/entity — через `ISceneObjectFactory`.
- `IObjectResolver` допустим только в composition roots и фабриках.
- `RegisterInstance` не инжектит вручную созданные объекты.
- Не резолвите контейнер в `Awake`, `OnEnable` или `Start`.

## Слои сборок

- `Kern.Contracts` — нижний слой: не ссылается ни на одну сборку `Kern.*`.
- Контракты модуля лежат рядом с модулем в папке `Contracts/` с `Kern.Contracts.asmref`; без него тип попадает в сборку модуля и ломает всех, кто ниже.
- Тип, зависящий от реализации, в `Contracts/` не кладите.
- Охраняет `ContractsAssemblyBoundaryTests`.

## Документы

- Документы в `docs/` должны быть автономным HTML с inline `<style>`, без Markdown и внешних зависимостей.
