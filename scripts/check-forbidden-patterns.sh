#!/usr/bin/env bash
# Audits the complete production C# tree for project architecture, settings,
# and performance regressions. Existing violations fail exactly like new ones;
# this is a project linter, not a staged-diff decoration. Replacements:
#   - singletons -> VContainer DI
#   - ServiceLocator -> constructor/DI injection
#   - InputAction -> actions in InputSystem_Actions.inputactions

set -euo pipefail

PATTERNS=(
    'public[[:space:]]+static[[:space:]]+[A-Za-z0-9_<>?.]+[[:space:]]+Instance[[:space:]]*([({;=]|=>)'
    'ServiceLocator'
    'new[[:space:]]+InputAction\('
    'FitFieldDimensionsToAtlasBudget'
    'Mathf\.Approximately\([^,]*CameraOrthoSize'
    'Camera\.main'
    'Application\.targetFrameRate[[:space:]]*='
    'QualitySettings\.vSyncCount[[:space:]]*='
    'new[[:space:]]+Texture2D(Array)?[[:space:]]*\('
    '\.LoadImage[[:space:]]*\('
    '\.styleSheets\.Add[[:space:]]*\('
    'new[[:space:]]+Vector2[[:space:]]*\([^,]+,[[:space:]]*Screen\.height[[:space:]]*-'
    '\.style\.(width|height)[[:space:]]*=[^;]*Screen\.(width|height)'
    'LightingCascadeAtlasLimit[[:space:]]*<=[[:space:]]*256[[:space:]]*\?'
    '(FindAnyObjectByType|FindFirstObjectByType)<Camera>'
    'AddComponent<[A-Za-z0-9_]*(Manager|Service)>'
    '(Config|config)\.GraphicsPreset[[:space:]]*='
    '(Config|config)\.GraphicsQualitySettings[[:space:]]*='
    'QualitySettings\.antiAliasing[[:space:]]*='
    'QualitySettings\.SetQualityLevel[[:space:]]*\('
    '\.renderScale[[:space:]]*='
    'PlayerPrefs\.(Set|Delete|Save)'
    '(slider|toggle|dropdown|quality|preset)\.value[[:space:]]*='
    'ServerConfig[^;]*(Master|Sfx|Music|Ambience|Voice|Ui)Volume'
    '_clientConfig\.Config\.[A-Za-z0-9_]+[[:space:]]*='
    '_clientConfig\.Save[[:space:]]*\('
)

RULE_NAMES=(
    'static Instance singleton'
    'ServiceLocator access'
    'ad-hoc InputAction'
    'fractional lighting-field fitting'
    'exact camera zoom cache comparison'
    'Camera.main outside GameplayCamera'
    'FPS cap outside DisplayManager'
    'VSync ownership outside DisplayManager'
    'runtime Texture2D construction outside RuntimeTextureFactory'
    'runtime image decoding outside RuntimeTextureFactory'
    'controller-local UI Toolkit stylesheet'
    'manual screen-to-panel Y flip'
    'UI root sized from Screen dimensions'
    'duplicated radiance-cascade count policy'
    'ad-hoc gameplay camera lookup'
    'manual manager/service construction'
    'graphics preset mutation outside ClientConfigManager'
    'graphics quality snapshot mutation outside ClientConfigManager'
    'MSAA ownership outside TerrariaLightingEngine'
    'Unity quality-level ownership outside TerrariaLightingEngine'
    'URP render-scale ownership outside TerrariaLightingEngine'
    'settings persistence in PlayerPrefs'
    'notifying UI settings refresh'
    'audio volume in ServerConfig'
    'direct ClientConfig field mutation'
    'unowned ClientConfig persistence'
)

# Per-rule path exemptions. Tests may construct tiny fixture textures directly;
# production runtime code may not. Generated/vendored code is excluded below.
ALLOW_REGEX=(
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^Assets/Scripts/Core/GameplayCamera\.cs$'
    '^Assets/Scripts/Rendering/DisplayManager\.cs$'
    '^Assets/Scripts/Rendering/DisplayManager\.cs$'
    '^(Assets/Editor/|Assets/Scripts/AssetPipeline/RuntimeTextureFactory\.cs|Assets/Scripts/Tests/)'
    '^(Assets/Editor/|Assets/Scripts/AssetPipeline/RuntimeTextureFactory\.cs|Assets/Scripts/Tests/)'
    '^$'
    '^$'
    '^$'
    '^$'
    '^Assets/Scripts/Core/GameplayCamera\.cs$'
    '^$'
    '^(Assets/Scripts/Core/ClientConfigManager\.cs|Assets/Scripts/World/Lighting/TerrariaLightingEngine\.cs)$'
    '^Assets/Scripts/Core/ClientConfigManager\.cs$'
    '^Assets/Scripts/World/Lighting/TerrariaLightingEngine\.cs$'
    '^Assets/Scripts/World/Lighting/TerrariaLightingEngine\.cs$'
    '^Assets/Scripts/World/Lighting/TerrariaLightingEngine\.cs$'
    '^(Assets/Editor/.*|Assets/Scripts/Networking/Auth/AuthTokenManager\.cs|Assets/Scripts/UI/AuthGate\.cs|Assets/Scripts/UI/GatewayController\.cs)$'
    '^$'
    '^$'
    '^$'
    '^(Assets/Scripts/Rendering/GraphicsSettingsController\.cs|Assets/Scripts/Rendering/DisplayManager\.cs|Assets/Scripts/World/Lighting/TerrariaLightingEngine\.cs)$'
)

# Narrow line-level exemptions for the canonical declaration of a shared
# policy. Path-wide exemptions would let a second copy sneak into the file.
ALLOW_CONTENT_REGEX=(
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    'return atlasDimension <= 256 \? 3 : 4;'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
)

if [ "${#PATTERNS[@]}" -ne "${#RULE_NAMES[@]}" ] ||
    [ "${#PATTERNS[@]}" -ne "${#ALLOW_REGEX[@]}" ] ||
    [ "${#PATTERNS[@]}" -ne "${#ALLOW_CONTENT_REGEX[@]}" ]; then
    echo "Lint configuration error: rule metadata arrays have different lengths." >&2
    exit 2
fi

# Vendored / generated code is exempt.
EXCLUDE_REGEX='^(Assets/Scripts/VContainer/|Assets/Plugins/|Packages/|Library/)'

declare -a files=()
if [ "$#" -gt 0 ]; then
    files=("$@")
else
    while IFS= read -r file; do
        files+=("$file")
    done < <(rg --files Assets/Scripts Assets/Editor -g '*.cs' | sort)
fi

if [ "${#files[@]}" -eq 0 ]; then
    exit 0
fi

declare -a production_files=()
for file in "${files[@]}"; do
    if [ ! -f "$file" ] || [[ "$file" =~ $EXCLUDE_REGEX ]]; then
        continue
    fi

    production_files+=("$file")
done

if [ "${#production_files[@]}" -eq 0 ]; then
    exit 0
fi

failed=0
for index in "${!PATTERNS[@]}"; do
    pattern="${PATTERNS[$index]}"
    allow_regex="${ALLOW_REGEX[$index]}"
    while IFS= read -r line; do
        file="${line%%:*}"
        if [[ -n "$allow_regex" && "$file" =~ $allow_regex ]]; then
            continue
        fi

        location_and_content="${line#*:}"
        line_number="${location_and_content%%:*}"
        content="${location_and_content#*:}"
        if [[ "$content" =~ ^[[:space:]]*(//|/\*|\*|///) ]]; then
            continue
        fi

        allow_content_regex="${ALLOW_CONTENT_REGEX[$index]}"
        if [[ -n "$allow_content_regex" && "$content" =~ $allow_content_regex ]]; then
            continue
        fi

        echo "FORBIDDEN ${RULE_NAMES[$index]} in $file:$line_number: $content"
        failed=1
    done < <(grep -nHE "$pattern" "${production_files[@]}" || true)
done

if [ "$failed" -ne 0 ]; then
    echo ""
    echo "Forbidden patterns detected in the production source tree:"
    echo "  - static 'Instance' singletons -> use VContainer DI"
    echo "  - ServiceLocator -> resolve through constructor/DI, not the static locator"
    echo "  - 'new InputAction(...)' -> add actions to InputSystem_Actions.inputactions"
    echo "  - FitFieldDimensionsToAtlasBudget -> select an integer pixels-per-cell scale"
    echo "  - exact CameraOrthoSize comparison -> use a quantized coverage cache"
    echo "  - Camera.main -> resolve the gameplay camera through GameplayCamera"
    echo "  - targetFrameRate/VSync -> DisplayManager is the only owner"
    echo "  - runtime textures/image decode -> use RuntimeTextureFactory"
    echo "  - UI Toolkit styles -> use PanelSettings.themeUss imports"
    echo "  - screen-to-panel coordinates -> use RuntimePanelUtils.ScreenToPanel"
    echo "  - UI dimensions -> use PanelSettings and USS layout"
    echo "  - cascade-count policy -> use the shared lighting helper"
    echo "  - camera lookup -> use GameplayCamera.Resolve/ResolveIn"
    echo "  - manager/service creation -> register and resolve it through VContainer"
    echo "  - graphics preset/snapshot writes -> use ClientConfigManager"
    echo "  - MSAA, Unity quality and URP render scale -> TerrariaLightingEngine is the owner"
    echo "  - persistent client settings -> use ClientConfigManager, not PlayerPrefs"
    echo "  - UI refresh -> use SetValueWithoutNotify"
    echo "  - audio volumes -> client config only, never ServerConfig"
    echo "  - client setting mutation/persistence -> expose one atomic owner method"
    exit 1
fi

exit 0
