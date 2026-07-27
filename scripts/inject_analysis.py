#!/usr/bin/env python3
"""
Scans all C# source files for [Inject] fields on MonoBehaviours,
then checks if those types are explicitly injected in GameLifetimeScope.
Reports NULL injection gaps — the root cause of silent dead code.
"""

import re
import os
from pathlib import Path
from collections import defaultdict

PROJECT = Path(__file__).parent.parent
SCRIPTS = PROJECT / "Assets" / "Scripts"
OUTPUT = PROJECT / "inject_analysis.txt"

INJECT_RE = re.compile(r'\[Inject\]')
# Match class declarations
CLASS_RE = re.compile(r'class\s+(\w+)(?:\s*:\s*([^\{]+))?')
# Match field declarations
FIELD_RE = re.compile(r'^\s*(?:\[.*?\]\s*)*(private|protected|public|internal)\s+([\w.<>\[\],\s]+?)\s+(\w+)\s*[;=]')
# Match Container.Inject / resolver.Inject calls
INJECT_CALL_RE = re.compile(r'(?:Container|resolver)\s*\.\s*Inject\s*(?:<[^>]+>)?\s*\([^)]*\)')
# Match RegisterInstance / RegisterBuildCallback patterns
REGISTER_RE = re.compile(r'(?:RegisterInstance|RegisterComponentOnNewGameObject|RegisterManager)\s*<\s*(\w+)\s*>')
# Match ServiceLocator.Resolve
RESOLVE_RE = re.compile(r'ServiceLocator\.Resolve\s*<\s*([\w.]+)\s*>\s*\(\s*\)')

def find_cs_files():
    for root, dirs, files in os.walk(SCRIPTS):
        for f in files:
            if f.endswith('.cs'):
                yield Path(root) / f

def analyze_inject_fields():
    """Find all [Inject] fields and their declaring types."""
    inject_map = defaultdict(list)  # type_name -> [(field_type, field_name, file, line)]

    for cs_file in find_cs_files():
        try:
            content = cs_file.read_text(encoding='utf-8', errors='replace')
        except:
            continue

        lines = content.split('\n')
        current_class = None

        for i, line in enumerate(lines):
            class_match = CLASS_RE.search(line)
            if class_match:
                current_class = class_match.group(1)

            if INJECT_RE.search(line) and current_class:
                # Look ahead for the field declaration
                for j in range(i + 1, min(i + 5, len(lines))):
                    field_match = FIELD_RE.match(lines[j])
                    if field_match:
                        field_type = field_match.group(2).strip()
                        field_name = field_match.group(3)
                        inject_map[current_class].append((
                            field_type, field_name,
                            str(cs_file.relative_to(PROJECT)), i + 1
                        ))
                        break

    return inject_map

def analyze_injections():
    """Find what gets explicitly injected in GameLifetimeScope and other inject calls."""
    injected = defaultdict(set)  # type_name -> {injected_types}
    scene_injected = defaultdict(set)

    gls_file = SCRIPTS / "Core" / "GameLifetimeScope.cs"
    if gls_file.exists():
        content = gls_file.read_text(encoding='utf-8', errors='replace')

        # Find RegisterManager<T> calls
        for m in REGISTER_RE.finditer(content):
            injected[m.group(1)].add("RegisterManager")

        # Find explicit resolver.Inject() calls
        for m in INJECT_CALL_RE.finditer(content):
            injected["*explicit*"].add(m.group(0).strip())

        # Find Container.Inject calls
        inject_calls = re.findall(r'Container\.Inject\s*\(\s*(\w+)', content)
        for var in inject_calls:
            injected["*scene_inject*"].add(var)

        # Find ServiceLocator.Resolve usage
        for m in RESOLVE_RE.finditer(content):
            injected["*servicelocator*"].add(m.group(1))

    # Scan ALL files for Container.Inject calls
    for cs_file in find_cs_files():
        if "GameLifetimeScope" in cs_file.name:
            continue
        try:
            content = cs_file.read_text(encoding='utf-8', errors='replace')
        except:
            continue

        for m in INJECT_CALL_RE.finditer(content):
            scene_injected[cs_file.stem].add(m.group(0).strip())

    return injected, scene_injected

def analyze_mono_behaviours():
    """Find all MonoBehaviours and their [Inject] fields."""
    mb_inject = {}  # class_name -> [(field_type, field_name, file, line)]

    for cs_file in find_cs_files():
        try:
            content = cs_file.read_text(encoding='utf-8', errors='replace')
        except:
            continue

        if "SingletonBehaviour" in cs_file.name or "VContainer" in str(cs_file):
            continue

        lines = content.split('\n')
        current_class = None
        is_mono = False

        for i, line in enumerate(lines):
            class_match = CLASS_RE.search(line)
            if class_match:
                current_class = class_match.group(1)
                bases = class_match.group(2) or ""
                is_mono = any(b.strip() in ('MonoBehaviour', 'LifetimeScope') or
                            'MonoBehaviour' in b or 'LifetimeScope' in b
                            for b in bases.split(','))

            if is_mono and INJECT_RE.search(line):
                for j in range(i + 1, min(i + 5, len(lines))):
                    field_match = FIELD_RE.match(lines[j])
                    if field_match:
                        field_type = field_match.group(2).strip()
                        field_name = field_match.group(3)
                        if current_class not in mb_inject:
                            mb_inject[current_class] = []
                        mb_inject[current_class].append((
                            field_type, field_name,
                            str(cs_file.relative_to(PROJECT)), i + 1
                        ))
                        break

    return mb_inject

def main():
    inject_map = analyze_inject_fields()
    injected, scene_injected = analyze_injections()
    mb_inject = analyze_mono_behaviours()

    sb = []
    sb.append("=" * 70)
    sb.append("INJECT ANALYSIS — Static scan of [Inject] field coverage")
    sb.append("=" * 70)

    total_fields = 0
    null_risk_fields = 0
    covered_types = set()

    for class_name, fields in sorted(mb_inject.items()):
        # Check if this type is covered by VContainer
        is_registered = class_name in injected
        is_explicitly_injected = any(
            class_name in targets
            for targets in scene_injected.values()
        )

        sb.append(f"\n[{class_name}]")
        sb.append(f"  Registered in VContainer: {is_registered}")
        sb.append(f"  Explicitly injected: {is_explicitly_injected}")

        for field_type, field_name, file, line in fields:
            total_fields += 1

            # Determine risk level
            if is_registered and is_explicitly_injected:
                risk = "COVERED"
                covered_types.add(class_name)
            elif is_registered:
                risk = "PARTIAL (registered but not explicitly Inject()d)"
                null_risk_fields += 1
            else:
                risk = "UNCOVERED (not in VContainer at all)"
                null_risk_fields += 1

            sb.append(f"  {field_type} {field_name} @ {file}:{line}")
            sb.append(f"    -> {risk}")

    sb.append(f"\n{'=' * 70}")
    sb.append(f"SUMMARY")
    sb.append(f"{'=' * 70}")
    sb.append(f"MonoBehaviours with [Inject]: {len(mb_inject)}")
    sb.append(f"Total [Inject] fields: {total_fields}")
    sb.append(f"Covered (safe): {total_fields - null_risk_fields}")
    sb.append(f"NULL RISK (will be null at runtime): {null_risk_fields}")

    # Check what's registered but never explicitly Inject()d
    sb.append(f"\nVContainer registered types:")
    for type_name in sorted(injected.keys()):
        if type_name.startswith('*'):
            continue
        registered = type_name in mb_inject
        explicitly = any(type_name in t for t in scene_injected.values())
        status = "OK" if explicitly else "REGISTERED BUT NEVER Inject()d — fields may be null!"
        sb.append(f"  {type_name}: {status}")

    sb.append(f"\nScene-level Container.Inject() calls:")
    for file, calls in sorted(scene_injected.items()):
        for call in sorted(calls):
            sb.append(f"  {file}: {call}")

    if null_risk_fields > 0:
        sb.append(f"\n{'!' * 70}")
        sb.append(f"ROOT CAUSE: {null_risk_fields} [Inject] fields have no injection path.")
        sb.append(f"VContainer's GameLifetimeScope must either:")
        sb.append(f"  1. RegisterManager<T>(builder) — for types it creates")
        sb.append(f"  2. resolver.Inject(obj) — for scene MonoBehaviours")
        sb.append(f"  3. Container.Inject(obj) — in Start() for scene objects")
        sb.append(f"If a MonoBehaviour has [Inject] but none of these are called,")
        sb.append(f"the field is NULL at runtime. Null guards make this SILENT.")
        sb.append(f"{'!' * 70}")

    OUTPUT.write_text('\n'.join(sb), encoding='utf-8')
    print(f"Analysis written to {OUTPUT}")
    print(f"NULL RISK fields: {null_risk_fields}/{total_fields}")

if __name__ == '__main__':
    main()
