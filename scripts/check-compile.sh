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
# Код возврата 1 — ошибки компиляции или отсутствующие ссылки. Неполная
# проверка не может быть зелёной.
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

    # Compile changed producers before their consumers so a full check cannot
    # silently use stale Unity ScriptAssemblies copies. The order here follows
    # the asmdef chain: core constants/contracts, persistence and asset
    # pipeline (both referenced by Kern.World), terrain world, then
    # presentation and tests.
    ORDERED=()
    for TARGET in Kern.Core Kern.Contracts Kern.Persistence Kern.AssetPipeline Kern.World; do
        for ASM in "${ASSEMBLIES[@]}"; do
            if [ "$ASM" = "$TARGET" ]; then
                ORDERED+=("$ASM")
                break
            fi
        done
    done

    for ASM in "${ASSEMBLIES[@]}"; do
        case " ${ORDERED[*]} " in
            *" $ASM "*) ;;
            *) ORDERED+=("$ASM") ;;
        esac
    done
    ASSEMBLIES=("${ORDERED[@]}")
fi

mkdir -p "$WORK"
STATUS=0
CHECKED_ASSEMBLIES=()

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
    sed -i.bak -E "s|-r:\"$DAG_REL/([A-Za-z0-9_.]+)\.ref\.dll\"|-r:\"Library/ScriptAssemblies/\1.dll\"|g" "$RSP"
    rm -f "$RSP.bak"

    # Проверяем потребителей против зависимостей, собранных ЭТИМ запуском,
    # а не против старых DLL редактора. Передавайте зависимости первыми.
    for CHECKED in ${CHECKED_ASSEMBLIES[@]+"${CHECKED_ASSEMBLIES[@]}"}; do
        sed -i.bak "s|-r:\"Library/ScriptAssemblies/$CHECKED.dll\"|-r:\"$OUT/$CHECKED/$CHECKED.dll\"|g" "$RSP"
        rm -f "$RSP.bak"
    done

    MISSING=0
    while IFS= read -r ref; do
        [ -f "$ref" ] || { echo "$ASM: нет ссылки $ref"; MISSING=$((MISSING + 1)); }
    done < <(grep -oE '^-r:"[^"]+"' "$RSP" | sed -E 's/^-r:"//; s/"$//')

    if [ "$MISSING" -gt 0 ]; then
        echo "$ASM: отсутствует ссылок: $MISSING"
        STATUS=1
        continue
    fi

    # rsp может не заканчиваться переводом строки — иначе первый добавленный
    # файл склеится с последним флагом и не попадёт в компиляцию.
    if [ -n "$(tail -c 1 "$RSP")" ]; then
        printf '\n' >> "$RSP"
    fi

    grep '^"' "$RSP" | tr -d '"' > "$WORK/$ASM.listed"

    # Обратный случай: файл удалён после последнего импорта. Без этого любая
    # чистка кода роняла бы проверку на CS2001, а не на настоящей ошибке.
    while IFS= read -r file; do
        case "$file" in
            *.cs) ;;
            *) continue ;;
        esac
        [ -f "$file" ] && continue
        grep -vxF "\"$file\"" "$RSP" > "$RSP.tmp" && mv "$RSP.tmp" "$RSP"
        echo "$ASM: пропущен удалённый файл $file"
    done < "$WORK/$ASM.listed"

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
        CHECKED_ASSEMBLIES+=("$ASM")
        echo "$ASM: ok (предупреждений $WARNINGS)"
    else
        echo "$ASM: ошибок $ERRORS, смотри $LOG"
        grep -E 'error CS[0-9]+' "$LOG" | head -20
        STATUS=1
    fi
done

exit $STATUS
