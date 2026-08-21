#!/usr/bin/env python3
"""Валидатор USS для Fodinae.

Ловит свойства, функции и значения, которых в UI Toolkit нет. Нужен потому,
что Unity сообщает о таких вещах предупреждением в консоли уже во время
импорта — то есть только после того, как ошибка попала в проект, — а
присутствие имени в CSS ещё ничего не значит:

  • `cubic-bezier` есть в ExCSS.Unity.dll (это парсер CSS), но не в
    UnityEngine.UIElementsModule.dll, поэтому не работает нигде;
  • `-unity-image-tint-color` встречается строкой внутри сборки, но
    зарегистрированным свойством не является — красить можно только фон,
    через `-unity-background-image-tint-color`.

Единственный надёжный источник — реестр свойств UIElements: он хранит пары
camelCase↔kebab-case. Список ниже снят с Unity 6000.5.

    python3 Assets/Editor/Tools/lint-uss.py

Код возврата 1, если найдено хоть одно нарушение.
"""

from __future__ import annotations

import pathlib
import re
import sys

STYLES = pathlib.Path(__file__).resolve().parents[3] / "Assets" / "Resources" / "Styles"

# Длинные свойства из реестра UIElements 6000.5.
LONGHAND = {
    "-unity-background-image-tint-color", "-unity-editor-text-rendering-mode",
    "-unity-font", "-unity-font-definition", "-unity-material",
    "-unity-overflow-clip-box", "-unity-paragraph-spacing", "-unity-slice-bottom",
    "-unity-slice-left", "-unity-slice-right", "-unity-slice-scale",
    "-unity-slice-top", "-unity-slice-type", "-unity-text-align",
    "-unity-text-auto-size", "-unity-text-generator", "-unity-text-outline-color",
    "-unity-text-outline-width", "-unity-text-overflow-position",
    "align-content", "align-items", "align-self", "aspect-ratio",
    "background-color", "background-image", "background-position-x",
    "background-position-y", "background-repeat", "background-size",
    "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
    "border-bottom-width", "border-left-color", "border-left-width",
    "border-right-color", "border-right-width", "border-top-color",
    "border-top-left-radius", "border-top-right-radius", "border-top-width",
    "bottom", "color", "cursor", "display", "filter", "flex-basis",
    "flex-direction", "flex-grow", "flex-shrink", "flex-wrap", "font-size",
    "height", "justify-content", "left", "letter-spacing", "margin-bottom",
    "margin-left", "margin-right", "margin-top", "max-height", "max-width",
    "min-height", "min-width", "opacity", "overflow", "padding-bottom",
    "padding-left", "padding-right", "padding-top", "position", "right",
    "rotate", "scale", "text-overflow", "text-shadow", "top", "transform-origin",
    "transition-delay", "transition-duration", "transition-property",
    "transition-timing-function", "translate", "visibility", "white-space",
    "word-spacing", "width",
    # Однословные и особые — реестр хранит их иначе, чем kebab-пары.
    "-unity-font-style", "-unity-text-outline-color",
}

# Сокращения раскрываются в длинные свойства и в реестре не лежат.
SHORTHAND = {
    "background", "background-position", "border", "border-color",
    "border-radius", "border-width", "flex", "font", "margin", "padding",
    "transition", "-unity-slice", "-unity-text-outline",
}

ALLOWED = LONGHAND | SHORTHAND

# Функции, которых в UIElements нет вовсе.
BAD_FUNCS = {
    "cubic-bezier": "в USS только 23 именованные кривые; ближайшая к сигнатурной — ease-out-circ",
    "radial-gradient": "поддерживается только linear-gradient",
    "conic-gradient": "поддерживается только linear-gradient",
    "calc": "арифметики в значениях нет",
    "min": "арифметики в значениях нет",
    "max": "арифметики в значениях нет",
    "clamp": "арифметики в значениях нет",
    "color-mix": "не поддерживается",
    "drop-shadow": "в наборе filter нет; свечение делается подложкой с blur()",
    "brightness": "в наборе filter нет",
    "saturate": "в наборе filter нет",
}

EASINGS = {
    "ease", "ease-in", "ease-out", "ease-in-out", "linear",
    "ease-in-sine", "ease-out-sine", "ease-in-out-sine",
    "ease-in-cubic", "ease-out-cubic", "ease-in-out-cubic",
    "ease-in-circ", "ease-out-circ", "ease-in-out-circ",
    "ease-in-elastic", "ease-out-elastic", "ease-in-out-elastic",
    "ease-in-back", "ease-out-back", "ease-in-out-back",
    "ease-in-bounce", "ease-out-bounce", "ease-in-out-bounce",
}

problems: list[str] = []


def strip_comments(text: str) -> str:
    """Вырезает комментарии, сохраняя нумерацию строк."""
    return re.sub(r"/\*.*?\*/", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)


def main() -> int:
    files = sorted(STYLES.glob("*.uss"))
    if not files:
        print(f"Не найдено ни одного .uss в {STYLES}")
        return 1

    declared: set[str] = set()
    used: dict[str, set[str]] = {}

    for path in files:
        body = strip_comments(path.read_text(encoding="utf-8"))

        if body.count("{") != body.count("}"):
            problems.append(f"{path.name}: скобки не сбалансированы")

        declared |= set(re.findall(r"(--[a-z0-9-]+)\s*:", body, re.I))
        for token in re.findall(r"var\(\s*(--[a-z0-9-]+)", body, re.I):
            used.setdefault(token, set()).add(path.name)

        for i, line in enumerate(body.split("\n"), 1):
            decl = re.match(r"\s*(-?[a-zA-Z][\w-]*)\s*:", line)
            if decl:
                name = decl.group(1)
                if not name.startswith("--") and name not in ALLOWED:
                    problems.append(f"{path.name}:{i} свойство {name!r} отсутствует в UI Toolkit")

            for func, why in BAD_FUNCS.items():
                if re.search(rf"\b{re.escape(func)}\s*\(", line):
                    problems.append(f"{path.name}:{i} функция {func}() — {why}")

            timing = re.search(r"transition-timing-function\s*:\s*([^;]+);", line)
            if timing:
                for value in timing.group(1).split(","):
                    value = value.strip()
                    if value.startswith("var(") or not value:
                        continue
                    if value not in EASINGS:
                        problems.append(f"{path.name}:{i} кривая {value!r} не входит в набор USS")

    for token in sorted(set(used) - declared):
        problems.append(f"токен {token} используется ({', '.join(sorted(used[token]))}), но не объявлен")

    print(f"Проверено файлов: {len(files)}; объявлено токенов: {len(declared)}")
    if problems:
        print(f"\nНарушений: {len(problems)}")
        for p in problems:
            print(f"  ✗ {p}")
        return 1
    print("Нарушений нет.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
