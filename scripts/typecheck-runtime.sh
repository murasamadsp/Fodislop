#!/bin/bash
# Проверка компиляции сборок Assets/Scripts без запуска Unity.
#
# ЗАЧЕМ. Единственный способ узнать, что правка компилируется, — открыть
# редактор, а редактор трогать нельзя без прямой просьбы. Здесь тот же
# компилятор Roslyn, который применяет Unity, вызывается напрямую: исходники
# берутся из дерева, ссылки — из установленного редактора и из уже собранных
# сборок в Library/ScriptAssemblies.
#
# ПОЧЕМУ НЕ dotnet build. Сгенерированные Unity .csproj прибиты к версии
# редактора, под которую их создали, и после обновления Unity ссылаются на
# несуществующие пути. Здесь версия ищется в Hub, а не берётся из файла.
#
# ЧЕГО ЭТА ПРОВЕРКА НЕ ДЕЛАЕТ. Она не говорит, что игра работает: сборка кода
# и правильность поведения — разные вещи. Она отвечает ровно на один вопрос —
# компилируется ли то, что написано.

set -u
cd "$(dirname "$0")/.."
ROOT="$PWD"

UNITY_ROOT=$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents 2>/dev/null | sort -V | tail -1)
if [ -z "$UNITY_ROOT" ]; then
    echo "Не найден установленный Unity в /Applications/Unity/Hub/Editor" >&2
    exit 2
fi

CSC="$UNITY_ROOT/Resources/Scripting/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
if [ ! -f "$CSC" ]; then
    CSC=$(find "$UNITY_ROOT/Resources/Scripting" -name csc.dll -path "*bincore*" 2>/dev/null | head -1)
fi
if [ ! -f "$CSC" ]; then
    echo "Не найден компилятор Roslyn внутри редактора" >&2
    exit 2
fi

DOTNET=$(command -v dotnet)
if [ -z "$DOTNET" ]; then
    echo "Не найден dotnet — им запускается csc.dll" >&2
    exit 2
fi

OUT="${TMPDIR:-/tmp}/fodinae-typecheck"
rm -rf "$OUT"
mkdir -p "$OUT"

# Директивы берутся из сгенерированного проекта: список платформенных
# символов длинный, зависит от версии и вручную не воспроизводится.
DEFINES=$(python3 - <<'PY'
import re
text = open('Fodinae.Runtime.csproj').read()
match = re.search(r'<DefineConstants>(.*?)</DefineConstants>', text, re.S)
print(match.group(1).strip() if match else '')
PY
)

# Порядок важен: сборка компилируется после тех, на которые ссылается.
ORDER="Fodinae.Contracts Fodinae.Persistence Fodinae.AssetPipeline Fodinae.World Fodinae.Networking Fodinae.Runtime Fodinae.UI Fodinae.Bootstrap Fodinae.Editor Fodinae.Tests.Editor"

# Полифил для init-свойств.
#
# Типы-записи в проекте есть, и Unity их собирает, а вот отдельного вызова
# Roslyn для них не хватает: IsExternalInit не лежит ни в netstandard 2.1, ни в
# сборках редактора — его подставляет сам конвейер компиляции Unity. Здесь он
# добавляется как внутренний тип каждой проверяемой сборки; на исходники
# проекта это не влияет никак.
POLYFILL="$OUT/IsExternalInit.cs"
cat > "$POLYFILL" <<'POLY'
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
POLY

failed=0
for NAME in $ORDER; do
    ASMDEF=$(find Assets/Scripts -name "$NAME.asmdef" | head -1)
    if [ -z "$ASMDEF" ]; then
        echo "⊘ $NAME — asmdef не найден, пропуск"
        continue
    fi

    DIR=$(dirname "$ASMDEF")
    SOURCES=$(python3 - "$DIR" <<'PY'
import os, sys
root = sys.argv[1]
nested = set()
for base, dirs, files in os.walk(root):
    if base != root and any(f.endswith('.asmdef') for f in files):
        nested.add(base)
for base, dirs, files in os.walk(root):
    if any(base == n or base.startswith(n + os.sep) for n in nested):
        continue
    for f in files:
        if f.endswith('.cs'):
            print(os.path.join(base, f))
PY
)

    RSP="$OUT/$NAME.rsp"
    {
        echo "-target:library"
        echo "-nostdlib+"
        echo "-noconfig"
        echo "-unsafe+"
        echo "-langversion:12.0"
        echo "-nowarn:0169,USG0001"
        echo "-out:$OUT/$NAME.dll"
        echo "-define:$DEFINES"
        # Ссылки собираются с отсевом одноимённых копий: редактор держит
        # UnityEngine и модули в двух местах сразу, а System.Memory дублирует
        # типы из netstandard. Компилятору всё равно, какая копия «правильная»,
        # — он видит один и тот же тип дважды и останавливается.
        {
            find "$OUT" -name "*.dll" 2>/dev/null
            find "$UNITY_ROOT/Resources/Scripting/NetStandard/ref" -name "*.dll" 2>/dev/null
            find "$UNITY_ROOT/Resources/Scripting/NetStandard/compat" -name "*.dll" 2>/dev/null
            find "$UNITY_ROOT/Resources/Scripting/NetStandard/Extensions" -name "*.dll" 2>/dev/null
            find "$UNITY_ROOT/Resources/Scripting/Managed/UnityEngine" -name "*.dll" 2>/dev/null
            find "$UNITY_ROOT/Resources/Scripting/Managed" -maxdepth 1 -name "*.dll" 2>/dev/null
            find "$ROOT/Library/ScriptAssemblies" -name "*.dll" 2>/dev/null | grep -v "/$NAME.dll$"
            find "$ROOT/Library/PackageCache" -name "*.dll" 2>/dev/null
            find "$ROOT/Assets" -name "*.dll" 2>/dev/null
        } | python3 -c "
import os, sys

def is_managed(path):
    # Пакеты везут и нативные библиотеки под тем же расширением. Подсунутая
    # компилятору нативная DLL — это не пропущенная ссылка, а обрыв всей
    # компиляции: csc сообщает CS0009 и не собирает ничего. Признак
    # управляемой сборки — сигнатура таблиц метаданных BSJB.
    try:
        with open(path, 'rb') as handle:
            return b'BSJB' in handle.read()
    except OSError:
        return False

_DUPLICATES = {'Microsoft.Bcl.HashCode.dll'}

seen = set()
for line in sys.stdin:
    path = line.strip()
    name = os.path.basename(path)
    # Первый путь выигрывает: порядок выше задан по убыванию доверия —
    # свежесобранное, базовая библиотека, движок, пакеты.
    # Сборки, дублирующие типы базовой библиотеки, отсекаются по имени:
    # netstandard 2.1 уже содержит и Span, и HashCode, а вторая копия делает
    # тип неоднозначным на ровном месте.
    if name in seen or name in _DUPLICATES or not is_managed(path):
        continue
    seen.add(name)
    print('-r:' + path)
"
        echo "$SOURCES"
        echo "$POLYFILL"
    } > "$RSP"

    # Ошибка без пути в начале строки — это отказ уровня набора ссылок, а не
    # исходников, и его нельзя терять: именно так выглядит обрыв компиляции.
    ERRORS=$("$DOTNET" "$CSC" "@$RSP" 2>&1 | grep -E "(^| )error CS" | grep -v "CS1703\|CS1704")
    if [ ! -f "$OUT/$NAME.dll" ] && [ -z "$ERRORS" ]; then
        ERRORS="компилятор не выдал сборку и не назвал причину"
    fi
    if [ -n "$ERRORS" ]; then
        echo "✗ $NAME"
        echo "$ERRORS" | head -25
        failed=1
    else
        echo "✓ $NAME"
    fi
done

exit $failed
