#!/usr/bin/env python3
"""Словарь протокола: то, на чём сервер имеет право говорить.

Зачем. Сервер собирает окна на клиенте. Значит нужен общий язык. Если языка
нет — сервер начнёт слать пиксели, и вместе с ними уедут темы, тиры, подгонка
под длинные переводы и настройки доступности игрока. Если язык есть — сервер
говорит ЧТО, клиент решает КАК.

Словарь не сочиняется: он ИЗВЛЕКАЕТСЯ из дизайн-системы. Всё, что объявлено
классом, — уже словарное слово. Поэтому расхождение протокола и вида
невозможно по построению: не бывает слова, которое клиент не умеет нарисовать.

Выход — protocol/ui-vocabulary.json: семейство -> варианты, с версией.
Версия меняется, когда меняется СОСТАВ. Клиент присылает её в хендшейке,
сервер не употребляет слов новее той версии, которую клиент назвал.
"""
import collections
import hashlib
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SKIP = {"tokens.css", "styleguide.css"}

# Семейства, которыми сервер говорить НЕ должен: это внутренняя механика вида,
# а не смысл. Сервер не выбирает зазор — он выбирает роль, зазор следует из неё.
INTERNAL_PREFIX = ("is-", "sg-", "dev-", "fdn-row", "fdn-stack", "fdn-grow", "fdn-rule")


def collect() -> dict:
    families: dict[str, set[str]] = collections.defaultdict(set)
    plain: set[str] = set()
    for f in sorted(ROOT.joinpath("css").rglob("*.css")):
        if f.name in SKIP:
            continue
        for m in re.finditer(r"(?m)^([.#][^{}\n]+?)\s*\{", f.read_text(encoding="utf-8")):
            for sel in m.group(1).split(","):
                for cls in re.findall(r"\.([a-zA-Z][\w-]*)", sel):
                    if cls.startswith(INTERNAL_PREFIX):
                        continue
                    if "--" in cls:
                        base, variant = cls.split("--", 1)
                        families[base].add(variant)
                    else:
                        plain.add(cls)
    return families, plain


def main() -> None:
    families, plain = collect()
    words = {k: sorted(v) for k, v in sorted(families.items())}
    # Слова без вариантов — тоже слова: это типы поверхностей и ролей.
    solo = sorted(c for c in plain if c not in families)

    payload = {
        "version": None,
        "families": words,
        "standalone": solo,
        "rules": {
            "unknown-word": "клиент рисует базовое семейство без варианта; "
                            "неизвестное базовое семейство — пропускает узел и пишет в лог",
            "who-decides": "сервер выбирает СЛОВО, клиент выбирает ЗНАЧЕНИЕ "
                           "(токен, тир, плотность, направление письма)",
            "not-in-protocol": "зазоры, отступы, цвета, кегли, порядок слоёв — "
                               "следствие слова, а не часть пакета",
        },
    }
    body = json.dumps(payload["families"], ensure_ascii=False, sort_keys=True) + \
        json.dumps(payload["standalone"], ensure_ascii=False)
    payload["version"] = hashlib.sha256(body.encode()).hexdigest()[:12]

    out = ROOT / "protocol" / "ui-vocabulary.json"
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"словарь: {len(words)} семейств с вариантами, {len(solo)} одиночных слов")
    print(f"версия:  {payload['version']}")
    print(f"файл:    {out.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
