# C# 12 & Code Formatting Rules

## 1. Пространства имён (Namespaces)
* **File-scoped namespace**: Для обычных C# классов и структур обязательно использовать file-scoped namespace (`namespace Fodinae.Domain;`).
* **Исключение Unity**: Любой Unity-сериализуемый тип, наследующий `MonoBehaviour` или `ScriptableObject`, ОБЯЗАН использовать блочный namespace (`namespace Fodinae.Domain { ... }`), чтобы уберечь `MonoScript.GetClass()` в Unity 6 от возврата `null`.

## 2. Безопасность типов и типы данных
* **Nullable Reference Types**: Глобально включен `#nullable enable`. Все поля, свойства и параметры явно размечаются (`string?`, `null!`).
* **Record Structs**: Легковесные структуры и хендлы данных используют `readonly record struct` и первичные конструкторы (Primary Constructors).
* **Collection Expressions**: Используйте выражение коллекций `[]` вместо `new List<T>()` или `new T[]`.
