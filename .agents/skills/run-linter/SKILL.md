---
name: run-linter
description: Проверка кодовой базы C# Рослин-анализаторами и прекоммит скриптами
---

# Roslyn Analyzers & Pre-Commit Lint Execution

Навык выполнения полной статической валидации C# кода перед коммитом или отправкой PR.

## 1. Локальная проверка staged-файлов
Запускает быструю валидацию застейдженных изменённых файлов:
```bash
./scripts/pre-commit-lint.sh
```

## 2. Полная проверка всей кодовой базы (CI-режим)
Проверяет абсолютно все `.cs` файлы под `Assets/Scripts/` и `Assets/Editor/` с компиляцией всех 15 подпроектов-зависимостей (`MinesServer.*`, `UniTask.*`, `Effekseer.*`):
```bash
CI=true ./scripts/pre-commit-lint.sh
```

## 3. Запуск полного пакета прекоммит-хуков Git
Запускает форматирование концов строк (LF), удаление trailing whitespace, проверку JSON/YAML и Roslyn-анализаторов:
```bash
pre-commit run --all-files
```

## 4. Решение частых проблем
* Ошибка `CS0246: MinesServer not found`: убедитесь, что в `scripts/pre-commit-lint.sh` в массиве `DEPENDENCIES` зафиксированы все `MinesServer.*.csproj` подпроекты.
* Ошибка `SA1513`: проверьте отсутствие пустой строки после закрывающей скобки `}`.
