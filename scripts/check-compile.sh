#!/usr/bin/env bash
# Тип-проверка C# без запуска Unity.
#
# Зачем. Сгенерированные Unity .csproj в репозитории устарели (Kern.Runtime.csproj
# перечисляет 12 файлов, которых давно нет), поэтому `dotnet build` по ним не
# работает, и единственной проверкой компиляции оставался запуск редактора.
# Здесь берётся свежий отклик-файл Roslyn из Library/Bee — его пишет сама Unity
# при импорте, — ссылки переводятся на уже собранные сборки, а csc зовётся
# напрямую. Unity при этом не запускается: это обычный компилятор.
#
#   scripts/check-compile.sh                 # все сборки Kern.*
#   scripts/check-compile.sh Kern.World      # одна
#
# Файлы, которых нет в отклике (добавленные после последнего импорта), скрипт
# дописывает сам — иначе новая правка не попала бы в проверку.
#
# Код возврата 1 — ошибки компиляции. Строка «нет ссылки» означает, что проверка
# неполна (сборка ещё не собрана Unity), а не что код сломан.
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEE="$ROOT/Library/Bee/artifacts"
OUT="$ROOT/Temp/obj/check-compile"
WORK="$OUT/rsp"

if [ ! -d "$BEE" ]; then
    echo "Нет $BEE — проект ещё ни разу не импортировался Unity." >&2
    exit 2
fi

DAG="$(find "$BEE" -maxdepth 1 -type d -name '*.dag' | head -1)"
[ -n "$DAG" ] || { echo "Нет каталога .dag в $BEE" >&2; exit 2; }
# Пути внутри отклика относительны корня проекта, поэтому и подмену путей, и
# разбор ссылок делаем от него же, а не от абсолютного DAG.
DAG_REL="Library/Bee/artifacts/$(basename "$DAG")"
cd "$ROOT" || exit 2

SDK_LINE="$(dotnet --list-sdks | tail -1)"
SDK_VERSION="$(echo "$SDK_LINE" | awk '{print $1}')"
SDK_DIR="$(echo "$SDK_LINE" | sed -E 's/.*\[(.*)\]/\1/')"
CSC="$SDK_DIR/$SDK_VERSION/Roslyn/bincore/csc.dll"
[ -f "$CSC" ] || { echo "Не найден компилятор: $CSC" >&2; exit 2; }

if [ "$#" -gt 0 ]; then
    ASSEMBLIES=("$@")
else
    # Только отклики самих сборок: рядом лежат *.dll.mvfrm.rsp, это другой
    # инструмент (он собирает сгенерированные файлы), и его аргументы нашему
    # вызову не подходят. Признак настоящей сборки — свой -out: в отклике.
    ASSEMBLIES=()
    while IFS= read -r name; do
        if grep -q "^-out:\"[^\"]*/$name\.dll\"$" "$DAG/$name.rsp" 2>/dev/null; then
            ASSEMBLIES+=("$name")
        fi
    done < <(find "$DAG" -maxdepth 1 -name 'Kern.*.rsp' ! -name '*mvfrm*' -exec basename {} .rsp \; | sort)
fi

mkdir -p "$WORK"
STATUS=0

for ASM in ${ASSEMBLIES[@]+"${ASSEMBLIES[@]}"}; do
    SRC="$DAG/$ASM.rsp"
    if [ ! -f "$SRC" ]; then
        echo "$ASM: нет отклика $SRC"
        STATUS=1
        continue
    fi

    RSP="$WORK/$ASM.rsp"
    LOG="$OUT/$ASM.log"
    mkdir -p "$OUT/$ASM"

    sed -e "s|-out:\"$DAG_REL/$ASM.dll\"|-out:\"$OUT/$ASM/$ASM.dll\"|" \
        -e "s|-refout:\"$DAG_REL/$ASM.ref.dll\"|-refout:\"$OUT/$ASM/$ASM.ref.dll\"|" \
        "$SRC" > "$RSP"

    # Unity держит ref-сборки не всегда (пересобирает только при импорте).
    # Тип-проверке годится и уже собранная сборка: публичный API тот же.
    sed -i '' -E "s|-r:\"$DAG_REL/([A-Za-z0-9_.]+)\.ref\.dll\"|-r:\"Library/ScriptAssemblies/\1.dll\"|g" "$RSP"

    MISSING=0
    while IFS= read -r ref; do
        [ -f "$ref" ] || { echo "$ASM: нет ссылки $ref (проверка неполна)"; MISSING=$((MISSING + 1)); }
    done < <(grep -oE '^-r:"[^"]+"' "$RSP" | sed -E 's/^-r:"//; s/"$//')

    # rsp может не заканчиваться переводом строки — иначе первый добавленный
    # файл склеится с последним флагом и не попадёт в компиляцию.
    if [ -n "$(tail -c 1 "$RSP")" ]; then
        printf '\n' >> "$RSP"
    fi

    grep '^"' "$RSP" | tr -d '"' > "$WORK/$ASM.listed"
    while IFS= read -r dir; do
        while IFS= read -r file; do
            grep -qxF "$file" "$WORK/$ASM.listed" && continue
            printf '"%s"\n' "$file" >> "$RSP"
            echo "$ASM: добавлен новый файл $file"
        done < <(find "$dir" -maxdepth 1 -name '*.cs' 2>/dev/null | sed "s|^$ROOT/||")
    done < <(sed 's|/[^/]*$||' "$WORK/$ASM.listed" | sort -u)

    dotnet "$CSC" -noconfig -nostdlib+ "@$RSP" > "$LOG" 2>&1
    CODE=$?
    ERRORS=$(grep -cE '(^|[^:])error CS[0-9]+' "$LOG")
    WARNINGS=$(grep -cE '(^|[^:])warning CS[0-9]+' "$LOG")

    if [ "$CODE" -eq 0 ] && [ "$ERRORS" -eq 0 ]; then
        echo "$ASM: ok (предупреждений $WARNINGS)"
    else
        echo "$ASM: ошибок $ERRORS, смотри $LOG"
        grep -E 'error CS[0-9]+' "$LOG" | head -20
        STATUS=1
    fi
done

exit $STATUS
