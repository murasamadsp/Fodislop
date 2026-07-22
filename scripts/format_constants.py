#!/usr/bin/env python3
import os
import re
import sys

# Словарь для разделения склеенных слов в ALL_CAPS именах констант
KNOWN_WORDS = [
    "VISUAL", "ROTATION", "OFFSET", "MIN", "MAX", "SMOOTH", "TIME", "REFERENCE", "MOVE", "SPEED",
    "POINT", "COUNT", "SEGMENT", "DIST", "HOTBAR", "COLS", "ROWS", "INVENTORY", "CELL", "SIZE",
    "GAP", "ICON", "PANEL", "WIDTH", "PADDING", "LABEL", "FONT", "TITLE", "BAR", "HEIGHT",
    "BTN", "BONUS", "SKILL", "GRID", "DURATION", "FLOAT", "FADE", "START", "MESSAGES",
    "DEFAULT", "CHUNK", "UPDATE", "DELAY", "THRESHOLD", "MINIMAP", "COLLISION", "DEBUG", "RANGE",
    "PER", "PAGE", "TOTAL", "CELLS", "BANK", "PATH"
]

def split_all_caps_name(name):
    if '_' in name:
        return name

    if name in ["TAG", "COLS", "ROWS", "GAP", "PADDING", "WIDTH", "HEIGHT", "PAGES"]:
        return name

    res = []
    curr = name
    while curr:
        matched = False
        for word in sorted(KNOWN_WORDS, key=len, reverse=True):
            if curr.startswith(word):
                res.append(word)
                curr = curr[len(word):]
                matched = True
                break
        if not matched:
            if res:
                res[-1] += curr[0]
            else:
                res.append(curr[0])
            curr = curr[1:]

    return "_".join(res)

def convert_pascal_to_snake(name):
    if '_' in name:
        return name
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1_\2', name)
    s2 = re.sub('([a-z0-9])([A-Z])', r'\1_\2', s1).upper()
    return s2

def find_all_constants(scripts_dir):
    const_pattern = re.compile(r'\bconst\s+[\w\<\>]+\s+([A-Za-z0-9_]+)\b')
    results = []

    for root, dirs, files in os.walk(scripts_dir):
        for file in files:
            if file.endswith('.cs'):
                filepath = os.path.join(root, file)
                with open(filepath, 'r', encoding='utf-8') as f:
                    content = f.read()
                    for match in const_pattern.finditer(content):
                        name = match.group(1)
                        results.append((filepath, name))
    return results

def main():
    apply_changes = "--apply" in sys.argv
    scripts_dir = "Assets/Scripts"
    for arg in sys.argv[1:]:
        if not arg.startswith("--"):
            scripts_dir = arg
            break

    print(f"Scanning constants in {scripts_dir}...")
    constants = find_all_constants(scripts_dir)

    renames = {}
    for filepath, name in constants:
        if name.isupper():
            new_name = split_all_caps_name(name)
        elif not '_' in name and any(c.isupper() for c in name[1:]):
            new_name = convert_pascal_to_snake(name)
        else:
            new_name = name

        if name != new_name:
            renames[name] = new_name
            print(f"  {name}  ->  {new_name} ({os.path.basename(filepath)})")

    print(f"\nFound {len(renames)} constants to rename.")

    if apply_changes:
        print("\nApplying renames across codebase...")
        for old_name, new_name in renames.items():
            pattern = re.compile(r'\b' + re.escape(old_name) + r'\b')
            for root, dirs, files in os.walk(scripts_dir):
                for file in files:
                    if file.endswith('.cs'):
                        filepath = os.path.join(root, file)
                        with open(filepath, 'r', encoding='utf-8') as f:
                            content = f.read()
                        if old_name in content:
                            new_content = pattern.sub(new_name, content)
                            with open(filepath, 'w', encoding='utf-8') as f:
                                f.write(new_content)
        print("Done!")

if __name__ == "__main__":
    main()
