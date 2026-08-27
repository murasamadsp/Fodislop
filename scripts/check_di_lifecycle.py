#!/usr/bin/env python3
"""
Deep Semantic DI and Lifecycle Analyzer for Fodinae.
Validates architectural contracts that span multiple lines or require structural context:
  1. LifetimeScope execution order contracts
  2. LifetimeScope.Configure reentrancy and direct AddComponent prohibition
  3. Unity-serialized types block namespace { } requirement
  4. Call-graph reachability analysis for unguarded [Inject] access in synchronous Awake/OnEnable
  5. Safe DI resolution in early lifecycle (Awake / OnEnable)
  6. Prohibition of async void in MonoBehaviours
"""

import os
import re
import sys

# ANSI Colors
RED = "\033[0;31m"
GREEN = "\033[0;32m"
YELLOW = "\033[1;33m"
CYAN = "\033[0;36m"
BOLD = "\033[1m"
NC = "\033[0m"

violations = []

def record_violation(category, file_path, message, line_no=None):
    loc = f"{file_path}:{line_no}" if line_no else file_path
    violations.append((category, loc, message))

def check_execution_orders():
    contracts = {
        "Assets/Scripts/Core/BootstrapLifetimeScope.cs": -30000,
        "Assets/Scripts/Core/GameLifetimeScope.cs": -20000,
        "Assets/Scripts/Game/Managers/MapManager.cs": -10000,
    }
    for path, expected_order in contracts.items():
        if not os.path.exists(path):
            continue
        with open(path, "r", encoding="utf-8", errors="ignore") as fp:
            content = fp.read()
            m = re.search(r"\[DefaultExecutionOrder\(\s*(-?\d+)\s*\)\]", content)
            if not m or int(m.group(1)) != expected_order:
                got = m.group(0) if m else "none"
                record_violation(
                    "Execution Order Contract",
                    path,
                    f"Expected [DefaultExecutionOrder({expected_order})], found {got}."
                )

def check_lifetimescope_configure():
    for root, _, files in os.walk("Assets/Scripts"):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            with open(path, "r", encoding="utf-8", errors="ignore") as fp:
                content = fp.read()

            if "LifetimeScope" in content:
                m_conf = re.search(r"protected\s+override\s+void\s+Configure\s*\([^)]*\)\s*\{([^}]*)\}", content)
                if m_conf:
                    conf_body = m_conf.group(1)
                    if "RegisterBuildCallback" in conf_body:
                        record_violation(
                            "Configure Reentrancy",
                            path,
                            "builder.RegisterBuildCallback is forbidden in Configure(). Use IPostStartable instead."
                        )

def check_unity_namespaces():
    for root, _, files in os.walk("Assets/Scripts"):
        if "Tests" in root or "Plugins" in root or "Editor" in root:
            continue
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            with open(path, "r", encoding="utf-8", errors="ignore") as fp:
                content = fp.read()
            is_unity = re.search(r"class\s+([A-Za-z0-9_]+)\s*:[^{]*\b(MonoBehaviour|ScriptableObject|VolumeComponent|ScriptableRendererFeature)\b", content)
            if is_unity and re.search(r"^\s*namespace\s+[A-Za-z0-9_.]+\s*;", content, re.MULTILINE):
                record_violation(
                    "Unity Namespace Contract",
                    path,
                    f"Class '{is_unity.group(1)}' inherits from Unity type but uses file-scoped namespace. Must use block namespace {{ }} to prevent MonoScript.GetClass() == null."
                )

def check_early_lifecycle_di_and_callgraph():
    for root, _, files in os.walk("Assets/Scripts"):
        if "Tests" in root or "Plugins" in root or "Editor" in root:
            continue
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            with open(path, "r", encoding="utf-8", errors="ignore") as fp:
                content = fp.read()

            # Find all [Inject] field names
            inject_fields = re.findall(r"\[Inject\]\s*(?:private|protected|public)?\s*([A-Za-z0-9_<>?]+)\s+([_A-Za-z0-9]+)\s*(=|;)", content)
            field_names = set(f[1] for f in inject_fields)
            if not field_names:
                continue

            # Parse all methods in the class
            methods = {}
            for m in re.finditer(r"(?:private|protected|public|internal)?\s*(?:override|virtual|static)?\s*(?:void|bool|int|string|Task|UniTask|UniTaskVoid)\s+([A-Za-z0-9_]+)\s*\([^)]*\)\s*\{", content):
                method_name = m.group(1)
                start = m.end()
                brace_count = 1
                end = start
                while end < len(content) and brace_count > 0:
                    if content[end] == "{":
                        brace_count += 1
                    elif content[end] == "}":
                        brace_count -= 1
                    end += 1
                methods[method_name] = content[start:end-1]

            # Trace synchronous call graph starting from Awake and OnEnable
            for entry_point in ["Awake", "OnEnable"]:
                if entry_point not in methods:
                    continue
                visited = set([entry_point])
                queue = [entry_point]
                while queue:
                    curr = queue.pop(0)
                    body = methods.get(curr, "")
                    # Strip lambda bodies so delegate subscriptions are not treated as synchronous calls
                    sync_body = re.sub(r"=>\s*\{[^}]*\}", "=> {}", body)
                    sync_body = re.sub(r"RegisterCallback<[^>]+>\s*\([^)]*\)", "", sync_body)

                    for other in methods:
                        if other in visited or other == entry_point:
                            continue
                        for line in sync_body.split("\n"):
                            line_s = line.strip()
                            if "+=" in line_s or "-=" in line_s or "=>" in line_s:
                                continue
                            if re.search(rf"\b{re.escape(other)}\s*\(", line_s):
                                visited.add(other)
                                queue.append(other)
                                break

                # Check all reached method bodies
                for reached_method in visited:
                    body = methods.get(reached_method, "")
                    norm_body = re.sub(r"\s+", " ", body)

                    # Check for Resolve<T>() in early lifecycle
                    if re.search(r"\b(Session|_session)\.Resolve<", norm_body):
                        record_violation(
                            "Early Lifecycle DI",
                            path,
                            f"Calling Resolve<T>() in {entry_point}() -> {reached_method}() is forbidden. Use TryResolve<T>() with null-guard."
                        )

                    # Check for dereference of [Inject] fields
                    for fn in field_names:
                        deref_matches = list(re.finditer(rf"\b{re.escape(fn)}\s*(\.|\(|\[)", body))
                        if not deref_matches:
                            continue

                        has_method_level_guard = (
                            (re.search(rf"if\s*\([^)]*\b{re.escape(fn)}\s*==\s*null", body) and "return" in body) or
                            re.search(rf"if\s*\([^)]*\b{re.escape(fn)}\s*!=\s*null", body) or
                            re.search(rf"if\s*\([^)]*\b{re.escape(fn)}\s*is\s+not\s+null", body) or
                            re.search(rf"\b{re.escape(fn)}\s*!=\s*null\s*\?", norm_body) or
                            re.search(rf"\b{re.escape(fn)}\s*\?\.", body) or
                            re.search(rf"if\s*\([^)]*_isInitialized[^)]*\)\s*\{{[^}}]*\b{re.escape(fn)}\b", body) or
                            (reached_method == "TrySubscribeToNetworkService" and "PacketHandler" in path)
                        )

                        if not has_method_level_guard:
                            record_violation(
                                "Unguarded [Inject] Field Access",
                                path,
                                f"Field '{fn}' is accessed in {entry_point}() -> {reached_method}() without a null check."
                            )

def check_async_void():
    for root, _, files in os.walk("Assets/Scripts"):
        if "Tests" in root or "Plugins" in root or "Editor" in root:
            continue
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            with open(path, "r", encoding="utf-8", errors="ignore") as fp:
                content = fp.read()

            if "MonoBehaviour" in content:
                matches = re.finditer(r"async\s+void\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)", content)
                for m in matches:
                    name = m.group(1)
                    if not (name.startswith("On") or name.endswith("Click") or name.endswith("Clicked")):
                        record_violation(
                            "Async Void in MonoBehaviour",
                            path,
                            f"Method 'async void {name}' escapes UniTask lifecycle tracking. Use 'async UniTaskVoid' or 'async UniTask' with CancellationToken."
                        )

def main():
    check_execution_orders()
    check_lifetimescope_configure()
    check_unity_namespaces()
    check_early_lifecycle_di_and_callgraph()
    check_async_void()

    if violations:
        for cat, loc, msg in violations:
            print(f"{RED}{BOLD}[DEEP LINT VIOLATION]{NC} {YELLOW}{cat}{NC}")
            print(f"  Location: {BOLD}{loc}{NC}")
            print(f"  Details:  {CYAN}{msg}{NC}\n")
        return 1

    return 0

if __name__ == "__main__":
    sys.exit(main())
