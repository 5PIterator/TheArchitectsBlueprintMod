using System.ComponentModel;
using ArchitectsStencilMod.Core;
using Terraria;
using Terraria.ModLoader.Config;

namespace ArchitectsStencilMod.Config
{
    /// <summary>
    /// Tile mapping compilation and discovery settings.
    /// When discovery toggles change, compilation is re-run automatically.
    /// </summary>
    public sealed class TileMappingConfig : ModConfig
    {
        public static TileMappingConfig Instance;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("TileMapping")]

        [DefaultValue(true)]
        public bool ShowDebugLogs;

        [DefaultValue(true)]
        public bool EnablePlacementDiscovery;

        [DefaultValue(true)]
        public bool EnableCustomInteractionMap;

        [DefaultValue(true)]
        public bool EnableBiomeConversions;

        [DefaultValue(true)]
        public bool EnableShimmerTransforms;

        public override void OnLoaded()
        {
            Instance = this;
        }

    }
}
