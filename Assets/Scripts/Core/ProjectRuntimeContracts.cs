#nullable enable

namespace Fodinae.Core;

public static class ProjectRuntimeContracts
{
    public const int AssetRequestTimeoutSeconds = 5;
    public const int LargeAssetRequestTimeoutSeconds = 10;
    public const long AssetCacheCapacityBytes = 256L * 1024 * 1024;
    public const long DecodedAssetCacheCapacityBytes = 256L * 1024 * 1024;
    public const float RobotMoveSpeed = 15f;
    public const float RobotRotationSpeed = 1080f;

    public static class ResourcePaths
    {
        public const string Configuration = "Configuration";
        public const string WorldLightingCompute = "Shaders/Lighting/WorldLighting";
        public const string PostProcessCompute = "Shaders/PostProcessing/PostProcess";
        public const string MainMenuUxml = "UI/MainMenu";
        public const string AssetLoadingIndicatorUxml = "UI/AssetLoadingIndicator";
        public const string GlobalChatUxml = "UI/GlobalChat";
        public const string LocalChatUxml = "UI/LocalChat";
    }

    public static class ShaderNames
    {
        public const string Terrain = "Universal Render Pipeline/Custom/Terrain";
        public const string DynamicEmission = "Hidden/Fodinae/DynamicEmission";
        public const string Velocity = "Fodinae/PostProcessing/Velocity";
        public const string WorldSurface = "Fodinae/World Surface";
    }

    public static class RequiredLayers
    {
        public const string WorldUi = "UI"; //TODO: Ui поменять на UI!!!!!!!!!!!!!!!!!!!!!
    }

    public static class RuntimeLimits
    {
        public const int MaximumPacketBatchPerFrame = 250;
        public const int MaximumWorldWidth = ushort.MaxValue;
        public const int MaximumWorldHeight = ushort.MaxValue;
        public const int WorldChunkSize = 32;
        public const int MaximumLightingUpdatesPerSecond = 60;
    }
}
