using System.ComponentModel;
using TheArchitectsBlueprintMod.Core;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>
    /// Server/host controls for mapping discovery and compile behavior.
    /// </summary>
    public sealed class TileMappingConfig : ModConfig
    {
        public static TileMappingConfig Instance = null!;

        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("TileMapping")]

        [DefaultValue(true)]
        public bool EnablePlacementDiscovery;

        [DefaultValue(true)]
        public bool EnableCustomInteractionMap;

        [DefaultValue(true)]
        public bool EnableBiomeConversions;

        [DefaultValue(true)]
        public bool EnableShimmerTransforms;

        [Header("Actions")]

        [DefaultValue(false)]
        public bool RunPlacementDiscovery;

        [DefaultValue(false)]
        public bool RunBiomeDiscovery;

        [DefaultValue(false)]
        public bool RunShimmerDiscovery;

        [DefaultValue(false)]
        public bool RunCompileNow;

        [DefaultValue(false)]
        public bool RunFullDiscoveryAndCompile;

        public override void OnLoaded()
        {
            Instance = this;
        }

        public override void OnChanged()
        {
            Instance = this;
            TileMappingSystem.QueueConfiguredActions(this);

            // One-shot action toggles: saving with true enqueues work, then they reset to false.
            RunPlacementDiscovery = false;
            RunBiomeDiscovery = false;
            RunShimmerDiscovery = false;
            RunCompileNow = false;
            RunFullDiscoveryAndCompile = false;
        }
    }
}
