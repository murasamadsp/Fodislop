#nullable enable

namespace Fodinae.Core;

public static class ProjectRuntimeContracts
{
    public static class World
    {
        public const float CellSize = 1f;
        public const int ChunkSize = 32;
        public const int MaximumWidth = ushort.MaxValue;
        public const int MaximumHeight = ushort.MaxValue;
    }

    public static class Gameplay
    {
        public const float DefaultDigCooldown = 0.3f;
    }

    public static class Chat
    {
        public const int MaximumGlobalChatLength = 256;
        public const int MaximumLocalChatLength = 256;
    }

    public static class Movement
    {
        public const float RobotMoveSpeed = 15f;
        public const float RobotRotationSpeed = 1080f;
        public const float ReferenceMoveSpeed = 25f;
    }

    public static class Debug
    {
        public const int CollisionDebugRange = 10;
    }

    public static class AssetStreaming
    {
        public const int AssetRequestTimeoutSeconds = 5;
        public const int LargeAssetRequestTimeoutSeconds = 10;
        public const long AssetCacheCapacityBytes = 256L * 1024 * 1024;
        public const long DecodedAssetCacheCapacityBytes = 256L * 1024 * 1024;
    }

    public static class ResourcePaths
    {
        public const string ProjectDefaultsResourceName = "ProjectDefaults";
        public const string Configuration = "Configuration";
        public const string ProjectDefaultsAsset = Configuration + "/" + ProjectDefaultsResourceName;
        public const string GraphicsQualityProfile = "GraphicsQualityProfile";
        public const string WorldLightingCompute = "Shaders/Lighting/WorldLighting";
        public const string PostProcessCompute = "Shaders/PostProcessing/PostProcess";
        public const string GatewayUxml = "UI/Gateway";
        public const string MainMenuUxml = "UI/MainMenu";
        public const string AssetLoadingIndicatorUxml = "UI/AssetLoadingIndicator";
        public const string GlobalChatUxml = "UI/GlobalChat";
        public const string LocalChatUxml = "UI/LocalChat";
    }

    public static class ShaderNames
    {
        public const string Terrain = "Universal Render Pipeline/Custom/Terrain";
        public const string DynamicEmission = "Hidden/Fodinae/DynamicEmission";
        public const string WorldSurface = "Fodinae/World Surface";
        public const string WorldEntity = "Fodinae/World Entity";
    }

    public static class RequiredLayers
    {
        public const string WorldUI = "UI";
        public const string WorldUISortingLayer = "World UI";
    }

    public static class RuntimeLimits
    {
        public const int MaximumPacketBatchPerFrame = 250;
        public const int MaximumWorldWidth = World.MaximumWidth;
        public const int MaximumWorldHeight = World.MaximumHeight;
        public const int WorldChunkSize = World.ChunkSize;
        public const int MaximumLightingUpdatesPerSecond = 60;
    }
}
