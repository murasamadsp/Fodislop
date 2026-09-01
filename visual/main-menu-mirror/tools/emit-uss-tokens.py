#!/usr/bin/env python3
"""Генератор: css/tokens.css -> ThemeTokens.uss + TokenUtilities.uss + палитра.

ЗАЧЕМ

Связь макета и игры держалась на человеческом договоре: шапка ThemeTokens.uss
просила «меняя значение здесь, поменяй его и там». Договор нарушился молча.
Замер перед написанием генератора: шестнадцать токенов разошлись по значению,
худший — --border-subtle: 0.08 в макете против 0.22 в игре, втрое ярче, почти
на каждой поверхности. Увидеть это можно было только сравнив два файла руками.

Генератор убирает не расхождение, а саму возможность расхождения.

ПОЧЕМУ ФАЙЛ ЦЕЛИКОМ МАШИННЫЙ

Первая версия умела «сшивку»: писала свою секцию между маркерами и бережно
обходила чужое. Это была подпорка, а не система, и она уже дала сбой — старый
:root объявлял те же токены ПОСЛЕ машинных и перебивал их, то есть генератор
работал вхолостую. Вместо подпорки файл разобрали:
  • четыре слоя псевдонимов (--color-*, --mm-*, --scifi-*, --btn-*) схлопнуты
    в семантический слой — 291 подстановка, 69 объявлений удалено;
  • 27 компонентных правил .sci-fi-* уехали в SciFi.uss.
После этого в ThemeTokens.uss не осталось ничего, кроме токенов, и сшивка
стала не нужна.

ПЯТЬ ПРЕОБРАЗОВАНИЙ CSS -> USS

  1. var() раскрывается до конца: в USS нет слоя примитивов.
  2. rgb(var(--rgb-x) / N%) -> rgba(r, g, b, 0.N). Именно на этом переводе,
     который делался руками, и разъехались 16 значений.
  3. Отбрасывается непереносимое: --hex-*/--rgb-*/--mat-* (их роль выполняет
     раскрытие), --blur-* (нет backdrop-filter), --layer-* (нет z-index),
     --fit-lines (нет line-clamp), шрифтовая скоропись (нет shorthand font).
  4. cubic-bezier -> именованная кривая: в USS 23 имени и ни одной свободной.
     Подбор сделан tools/fit-easing.py, отклонение записано рядом со строкой.
  5. Гарнитура -> путь к SDF-ассету. Это не расхождение, а правильная
     подстановка, поэтому таблица задана явно и путь проверяется.
"""

import argparse
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent.parent
TOKENS = ROOT / "css" / "tokens.css"
STYLES = REPO / "Assets" / "Resources" / "Styles"
THEME = STYLES / "ThemeTokens.uss"
UTILS = STYLES / "TokenUtilities.uss"
PALETTE = STYLES / "token-palette.json"
CSHARP = REPO / "Assets" / "Scripts" / "UI" / "Builders" / "DesignTokens.g.cs"

WARNING = ("/* ФАЙЛ МАШИННЫЙ. Правки будут затёрты.\n"
           "   Источник истины: visual/main-menu-mirror/css/tokens.css\n"
           "   Генератор:       visual/main-menu-mirror/tools/emit-uss-tokens.py\n"
           "   Расхождение ловит CI (scripts/check-architecture.js). */\n")

DROP_PREFIX = ("--hex-", "--rgb-", "--mat-", "--blur-", "--layer-", "--z-")
DROP_EXACT = {"--fit-lines"}

FONT_ASSETS = {
    "--face-body": "Assets/Resources/Fonts/Exo2_SDF.asset",
    "--face-data": "Assets/Resources/Fonts/JetBrainsMono_SDF.asset",
    "--face-display": "Assets/Resources/Fonts/Unbounded_SDF.asset",
}

EASING = {
    "cubic-bezier(0.2,0.75,0.2,1)": ("ease-out-circ", "подбор fit-easing.py, max-отклонение 0.129"),
    "cubic-bezier(0.4,0,0.2,1)": ("ease-in-out", None),
    "cubic-bezier(0,0.2,0.8,1)": ("ease-out-cubic", None),
}

# Имена тиров НЕ придуманы здесь: их объявляет и ставит
# Assets/Scripts/UI/Common/Interaction/UILayoutTier.cs.
TIERS = {
    "max-width: 899px": "tier--compact",
    "min-width: 1600px": "tier--wide",
}


def read_blocks(src: str):
    """(:root по умолчанию, {класс тира: {токен: значение}})."""
    base: dict[str, str] = {}
    tiers: dict[str, dict[str, str]] = {}
    current: dict[str, str] | None = None
    in_media = False

    for line in src.splitlines():
        media = re.match(r"\s*@media\s*\(([^)]*)\)", line)
        if media:
            in_media = True
            cls = TIERS.get(media.group(1).strip())
            current = tiers.setdefault(cls, {}) if cls else None
            continue
        if re.match(r"\s*:root\s*\{", line):
            if not in_media:
                current = base
            continue
        if line.startswith("}"):
            in_media, current = False, None
            continue
        decl = re.match(r"\s*(--[\w-]+)\s*:\s*([^;]+);", line)
        if decl and current is not None:
            current.setdefault(decl.group(1), decl.group(2).strip())
    return base, tiers


def resolve(value: str, table: dict[str, str], depth: int = 0) -> str:
    if depth > 12:
        return value

    def sub(m: re.Match) -> str:
        name = m.group(1).strip()
        return resolve(table[name], table, depth + 1) if name in table else m.group(0)

    value = re.sub(r"var\(\s*(--[\w-]+)\s*\)", sub, value).strip()
    m = re.fullmatch(r"rgb\(\s*([\d\s,]+?)\s*/\s*([\d.]+)%\s*\)", value)
    if m:
        parts = [p for p in re.split(r"[,\s]+", m.group(1).strip()) if p]
        return f"rgba({', '.join(parts)}, {round(float(m.group(2)) / 100, 4):g})"
    return value


def convert(name: str, value: str):
    """Значение для USS и пояснение, либо None — если токен не переносится."""
    if name in DROP_EXACT or name.startswith(DROP_PREFIX):
        return None

    if name in FONT_ASSETS:
        path = FONT_ASSETS[name]
        if not (REPO / path).exists():
            sys.exit(f"нет SDF-ассета для {name}: {path}")
        return f'url("project://database/{path}")', None
    if name.startswith("--face-"):
        sys.exit(f"{name}: гарнитура без SDF-ассета — добавьте её в FONT_ASSETS")

    flat = value.replace(" ", "")
    if flat in EASING:
        return EASING[flat]
    if "cubic-bezier" in value:
        sys.exit(f"{name}: кривая {value} не подобрана, запустите tools/fit-easing.py")

    # font: weight size/leading family — в USS есть только longhand.
    if re.match(r"^\d+\s+\S+\s*/\s*\S+\s", value):
        return None

    # Относительные единицы USS не понимает вовсе: letter-spacing принимает
    # только пиксели. Пересчитать em в px статически нельзя — величина зависит
    # от кегля, а он у каждого правила свой. Поймано импортом Unity:
    # «Unsupported unit: '0.04em'».
    if re.search(r"[\d.]+(em|rem|ch|ex|vw|vh|vmin|vmax)\b", value):
        return None

    return value, None


def emit_tokens(base, tiers) -> str:
    out = [WARNING, ":root {"]
    dropped = []
    for name, raw in base.items():
        got = convert(name, resolve(raw, base))
        if got is None:
            dropped.append(name)
            continue
        value, note = got
        out.append(f"    {name}: {value};" + (f"  /* {note} */" if note else ""))
    out.append("}")

    for cls, table in tiers.items():
        merged = {**base, **table}
        out += ["", "/* Тир задаётся классом на корневом элементе: @media в USS нет.",
                "   Класс ставит UILayoutTier.cs. */", f".{cls} {{"]
        for name in table:
            got = convert(name, resolve(merged[name], merged))
            if got:
                out.append(f"    {name}: {got[0]};")
        out.append("}")

    # Имена в комментарии пишутся БЕЗ ведущих дефисов: парсер USS видит «--имя»
    # даже внутри комментария, принимает за объявление и падает с ColonMissing.
    names = ", ".join(n.lstrip("-") for n in sorted(dropped))
    out += ["", f"/* Не переносится в USS ({len(dropped)}): {names} */"]
    return "\n".join(out) + "\n"


UTILITY = [
    ("bg", "background-color", ("--surface-", "--accent-", "--state-", "--light-")),
    ("bd", "border-color", ("--border-", "--accent-", "--state-")),
    ("fg", "color", ("--text-", "--accent-", "--state-", "--rarity-", "--light-")),
]

# Отступы приходят по сторонам (Margins.Top/Right/Bottom/Left), поэтому и
# утилита нужна посторонняя. Классы машинные, писать их руками смысла нет.
SIDES = [("t", "top"), ("r", "right"), ("b", "bottom"), ("l", "left")]
BOXED = [("pad", "padding"), ("mar", "margin")]


def emit_utilities(base) -> str:
    """Класс на каждую роль — чтобы код не писал инлайн.

    Из C# нельзя написать element.style.backgroundColor = var(--surface-panel):
    инлайн принимает только конечное значение и перестаёт следовать теме и тиру.
    AddToClassList — может. Это единственный способ дать серверным окнам темы,
    и он же нужен StyleApplicator.cs.
    """
    out = [WARNING, "/* Утилиты для кода: класс вместо инлайна. См. StyleApplicator.cs. */"]
    seen = set()
    for prefix, prop, roots in UTILITY:
        out += ["", f"/* {prop} */"]
        for name in base:
            if not name.startswith(roots) or name.startswith(DROP_PREFIX):
                continue
            if name.startswith("--face-") or name in DROP_EXACT:
                continue
            cls = f"{prefix}-{name[2:]}"
            if cls in seen:
                continue
            seen.add(cls)
            out.append(f".{cls} {{ {prop}: var({name}); }}")

    spaces = [n for n in base if n.startswith("--space-")]
    for prefix, prop in BOXED:
        out += ["", f"/* {prop} — по сторонам и целиком */"]
        for name in spaces:
            out.append(f".{prefix}-{name[2:]} {{ {prop}: var({name}); }}")
        for short, side in SIDES:
            for name in spaces:
                out.append(f".{prefix}-{short}-{name[2:]} {{ {prop}-{side}: var({name}); }}")
    return "\n".join(out) + "\n"


def emit_palette(base) -> str:
    """Таблица примагничивания для StyleApplicator: сервер шлёт ARGB и пиксели,
    клиент ищет ближайший токен и выдаёт класс. Таблица машинная, потому что
    палитра меняется вместе с макетом."""
    colors, space = {}, {}
    for name, raw in base.items():
        if name.startswith(DROP_PREFIX):
            continue
        value = resolve(raw, base)
        m = re.fullmatch(r"rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s]+([\d.]+))?\s*\)", value)
        if m:
            colors[name] = [int(float(m.group(i))) for i in (1, 2, 3)] + \
                [round(float(m.group(4)) if m.group(4) else 1.0, 4)]
            continue
        m = re.fullmatch(r"#([0-9a-fA-F]{6})", value)
        if m:
            h = m.group(1)
            colors[name] = [int(h[i:i + 2], 16) for i in (0, 2, 4)] + [1.0]
            continue
        if name.startswith("--space-"):
            px = re.fullmatch(r"(\d+)px", value)
            if px:
                space[name] = int(px.group(1))
    return json.dumps({
        "_": "Машинный файл. Источник visual/main-menu-mirror/css/tokens.css, "
             "генератор tools/emit-uss-tokens.py. Правки будут затёрты.",
        "colors": dict(sorted(colors.items())),
        "space": dict(sorted(space.items(), key=lambda kv: kv[1])),
    }, ensure_ascii=False, indent=2) + "\n"


def emit_csharp(base) -> str:
    """Та же палитра, но таблицей на C#.

    JSON пришлось бы разбирать в рантайме, а разборщика в проекте нет — только
    ручной парсер словаря локализации. Сгенерированный C# не разбирается вовсе,
    проверяется компилятором и не может не найтись в Resources.
    """
    colors, space = [], []
    for name, raw in sorted(base.items()):
        if name.startswith(DROP_PREFIX):
            continue
        value = resolve(raw, base)
        m = re.fullmatch(r"rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s]+([\d.]+))?\s*\)", value)
        if m:
            r, g, b = (int(float(m.group(i))) for i in (1, 2, 3))
            a = float(m.group(4)) if m.group(4) else 1.0
        else:
            m = re.fullmatch(r"#([0-9a-fA-F]{6})", value)
            if m:
                h = m.group(1)
                r, g, b, a = int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 1.0
            else:
                if name.startswith("--space-"):
                    px = re.fullmatch(r"(\d+)px", value)
                    if px:
                        space.append((name, int(px.group(1))))
                continue
        cls = None
        for prefix, _, roots in UTILITY:
            if name.startswith(roots):
                cls = f"{prefix}-{name[2:]}"
                break
        colors.append((name, cls, r, g, b, round(a, 4)))

    lines = [
        "// ФАЙЛ МАШИННЫЙ. Правки будут затёрты.",
        "// Источник истины: visual/main-menu-mirror/css/tokens.css",
        "// Генератор:       visual/main-menu-mirror/tools/emit-uss-tokens.py",
        "",
        "namespace Fodinae.UI.Builders",
        "{",
        "    /// <summary>Палитра дизайн-системы для примагничивания серверных значений.</summary>",
        "    public static class DesignTokens",
        "    {",
        "        public readonly struct Swatch",
        "        {",
        "            public readonly string Token;",
        "            public readonly string UtilityClass;",
        "            public readonly float R, G, B, A;",
        "",
        "            public Swatch(string token, string utilityClass, float r, float g, float b, float a)",
        "            {",
        "                Token = token; UtilityClass = utilityClass;",
        "                R = r; G = g; B = b; A = a;",
        "            }",
        "        }",
        "",
        "        public static readonly Swatch[] Colors =",
        "        {",
    ]
    for name, cls, r, g, b, a in colors:
        cls_lit = f'"{cls}"' if cls else "null"
        lines.append(f'            new Swatch("{name}", {cls_lit}, '
                     f"{r / 255:.4f}f, {g / 255:.4f}f, {b / 255:.4f}f, {a:.4f}f),")
    lines += [
        "        };",
        "",
        "        /// <summary>Ступени шкалы пространства: значение в пикселях и класс утилиты.</summary>",
        "        public static readonly (string Token, int Px)[] Space =",
        "        {",
    ]
    for name, px in sorted(space, key=lambda kv: kv[1]):
        lines.append(f'            ("{name}", {px}),')
    lines += ["        };", "    }", "}", ""]
    return "\n".join(lines)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="ничего не писать; выйти с 1, если игра разошлась с макетом")
    args = ap.parse_args()

    base, tiers = read_blocks(TOKENS.read_text(encoding="utf-8"))
    targets = {
        THEME: emit_tokens(base, tiers),
        UTILS: emit_utilities(base),
        PALETTE: emit_palette(base),
        CSHARP: emit_csharp(base),
    }
    stale = [p for p, text in targets.items()
             if not p.exists() or p.read_text(encoding="utf-8") != text]

    if args.check:
        if stale:
            print("ТОКЕНЫ ИГРЫ РАЗОШЛИСЬ С МАКЕТОМ:")
            for p in stale:
                print(f"  {p.relative_to(REPO)}")
            print("\n  Источник истины — visual/main-menu-mirror/css/tokens.css")
            print("  Выполните: python3 visual/main-menu-mirror/tools/emit-uss-tokens.py")
            return 1
        print(f"игра совпадает с макетом ({len(base)} токенов, {len(tiers)} тира)")
        return 0

    for p, text in targets.items():
        p.write_text(text, encoding="utf-8")
    print(f"прочитано токенов: {len(base)}, тиров: {len(tiers)}")
    for p in targets:
        print(f"  {p.relative_to(REPO)}" + ("  (изменился)" if p in stale else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
