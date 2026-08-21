#!/usr/bin/env python3
"""Линтер дизайн-системы FODINAE.

Проверяет инварианты, которые легко нарушить вручную и невозможно заметить
глазом: неразрешённые токены, сырые цвета вне палитры, утёкшие имена,
недостаточный контраст.

    python3 tools/lint-design-system.py

Код возврата 1, если найдено хоть одно нарушение.
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TOKENS = ROOT / "css" / "tokens.css"

CSS_FILES = sorted(ROOT.glob("css/**/*.css")) + [ROOT / "styles.css"]
ALL_FILES = CSS_FILES + [ROOT / "index.html", ROOT / "app.js"]

# Цвета, которые разрешено писать сырыми вне tokens.css.
COLOR_ALLOWLIST = {"transparent", "currentColor", "inherit", "none"}

# Классы контраста по WCAG 2.1: обычный текст 4.5, крупный (>=18px) 3.0.
CONTRAST_PAIRS = [
    ("--hex-ink-100", "--hex-void", 4.5, "основной текст"),
    ("--hex-ink-70", "--hex-void", 4.5, "вторичный текст"),
    ("--hex-ink-50", "--hex-void", 4.5, "третичный текст"),
    ("--text-on-gold", "--hex-gold", 4.5, "текст на золотой кнопке"),
]

problems: list[str] = []


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8") if path.exists() else ""


# --------------------------------------------------------------------------
# 1. Каждый использованный токен должен быть объявлен
# --------------------------------------------------------------------------

def check_tokens_resolve() -> None:
    declared: set[str] = set()
    for f in CSS_FILES:
        declared |= set(re.findall(r"(--[a-z0-9-]+)\s*:", read(f), re.I))

    used: dict[str, set[str]] = {}
    for f in ALL_FILES:
        for tok in re.findall(r"var\(\s*(--[a-z0-9-]+)", read(f), re.I):
            used.setdefault(tok, set()).add(f.name)

    for tok in sorted(set(used) - declared):
        problems.append(
            f"токен {tok} используется ({', '.join(sorted(used[tok]))}), но нигде не объявлен"
        )

    # Обратная проверка: объявлен, но ни разу не использован.
    unused = sorted(declared - set(used))
    if unused:
        print(f"  примечание: {len(unused)} объявленных токенов не используются: "
              f"{', '.join(unused[:8])}{' …' if len(unused) > 8 else ''}")


# --------------------------------------------------------------------------
# 2. Сырые цвета разрешены только в tokens.css
# --------------------------------------------------------------------------

def check_no_raw_colors() -> None:
    """Ищет сырые цвета только внутри стилей.

    Сканировать весь HTML нельзя: значение пароля `MiningPassword#2026`,
    сид сервера `#849201` и ID `#8849-0192` — это контент, а не CSS.
    """
    color = re.compile(r"#[0-9a-f]{3,8}\b|rgba?\(\s*\d+\s*,", re.I)
    style_attr = re.compile(r'style="([^"]*)"', re.I)
    js_style = re.compile(r"\.style\.[a-zA-Z]+\s*=\s*['\"]([^'\"]*)['\"]")

    for f in ALL_FILES:
        if f == TOKENS:
            continue
        for i, line in enumerate(read(f).splitlines(), 1):
            if "lint-ignore" in line:
                continue
            if f.suffix == ".css":
                chunks = [line]
            elif f.suffix == ".html":
                chunks = style_attr.findall(line)
            else:
                chunks = js_style.findall(line)
            for chunk in chunks:
                for m in color.finditer(chunk):
                    problems.append(
                        f"{f.name}:{i} сырой цвет {m.group(0)!r} вне tokens.css"
                    )


# --------------------------------------------------------------------------
# 3. Имена, которым не место в продакшне
# --------------------------------------------------------------------------

def check_forbidden_names() -> None:
    forbidden = {
        "genshin": "имя источника вдохновения в продакшн-классах",
        "--fa-": "устаревший префикс токенов, заменён семантическим слоем",
    }
    for f in ALL_FILES:
        text = read(f)
        for needle, why in forbidden.items():
            n = len(re.findall(re.escape(needle), text, re.I))
            if n:
                problems.append(f"{f.name}: {n}× {needle!r} — {why}")


# --------------------------------------------------------------------------
# 4. Контраст
# --------------------------------------------------------------------------

def _srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(hex_color: str) -> float:
    h = hex_color.lstrip("#")
    if len(h) == 3:
        h = "".join(ch * 2 for ch in h)
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    return (0.2126 * _srgb_to_linear(r)
            + 0.7152 * _srgb_to_linear(g)
            + 0.0722 * _srgb_to_linear(b))


def contrast(fg: str, bg: str) -> float:
    a, b = luminance(fg), luminance(bg)
    lo, hi = sorted((a, b))
    return (hi + 0.05) / (lo + 0.05)


def check_contrast() -> None:
    text = read(TOKENS)
    values = dict(re.findall(r"(--[a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;", text))

    def resolve(name: str) -> str | None:
        if name in values:
            return values[name]
        m = re.search(rf"{re.escape(name)}\s*:\s*var\(\s*(--[a-z0-9-]+)\s*\)", text)
        return resolve(m.group(1)) if m else None

    for fg_name, bg_name, minimum, label in CONTRAST_PAIRS:
        fg, bg = resolve(fg_name), resolve(bg_name)
        if not fg or not bg:
            problems.append(f"контраст: не удалось разрешить {fg_name} или {bg_name}")
            continue
        ratio = contrast(fg, bg)
        mark = "ok" if ratio >= minimum else "НИЖЕ НОРМЫ"
        line = f"  {label:28s} {fg} на {bg} = {ratio:5.2f}:1  (нужно {minimum})  {mark}"
        print(line)
        if ratio < minimum:
            problems.append(f"контраст {label}: {ratio:.2f}:1 при норме {minimum}:1")


# --------------------------------------------------------------------------
# 5. Инлайн-стили в разметке
# --------------------------------------------------------------------------

def check_inline_styles() -> None:
    n = len(re.findall(r'\sstyle="', read(ROOT / "index.html")))
    print(f"  инлайн-стилей в index.html: {n}")
    if n:
        problems.append(
            f"index.html: {n} инлайн-стилей — вёрстка должна жить в классах"
        )


def main() -> int:
    print("Контраст:")
    check_contrast()
    print("\nТокены:")
    check_tokens_resolve()
    print("\nРазметка:")
    check_inline_styles()

    check_no_raw_colors()
    check_forbidden_names()

    print()
    if problems:
        print(f"Нарушений: {len(problems)}")
        for p in problems[:40]:
            print(f"  ✗ {p}")
        if len(problems) > 40:
            print(f"  … и ещё {len(problems) - 40}")
        return 1
    print("Нарушений нет.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
