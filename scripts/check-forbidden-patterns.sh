#!/usr/bin/env bash
# Blocks newly-added occurrences of known architectural anti-patterns:
#   - static singletons ("public static X Instance")
#   - ServiceLocator usage
#   - ad-hoc "new InputAction(...)"
#
# The check is incremental: only lines added in the staged diff are inspected,
# so pre-existing occurrences in the tree do not fail the hook. Replacements:
#   - singletons -> VContainer DI
#   - ServiceLocator -> constructor/DI injection
#   - InputAction -> actions in InputSystem_Actions.inputactions

set -euo pipefail

PATTERNS=(
    'public[[:space:]]+static[[:space:]]+[A-Za-z0-9_<>?.]+[[:space:]]+Instance[[:space:]]*([({;=]|=>)'
    'ServiceLocator'
    'new[[:space:]]+InputAction\('
)

# Vendored / generated code is exempt.
EXCLUDE_REGEX='^(Assets/Scripts/VContainer/|Assets/Plugins/|Packages/|Library/)'

declare -a files=()
if [ "$#" -gt 0 ]; then
    files=("$@")
else
    while IFS= read -r file; do
        files+=("$file")
    done < <(git diff --cached --name-only --diff-filter=ACM -- '*.cs')
fi

failed=0
for file in "${files[@]}"; do
    if [[ "$file" =~ $EXCLUDE_REGEX ]]; then
        continue
    fi

    for pattern in "${PATTERNS[@]}"; do
        while IFS= read -r line; do
            content="${line#+}"
            echo "FORBIDDEN PATTERN in $file: $content"
            failed=1
        done < <(git diff --cached -U0 -- "$file" | grep -E '^\+' | grep -E "$pattern" || true)
    done
done

if [ "$failed" -ne 0 ]; then
    echo ""
    echo "New occurrences of forbidden patterns detected:"
    echo "  - static 'Instance' singletons -> use VContainer DI"
    echo "  - ServiceLocator -> resolve through constructor/DI, not the static locator"
    echo "  - 'new InputAction(...)' -> add actions to InputSystem_Actions.inputactions"
    exit 1
fi

exit 0
