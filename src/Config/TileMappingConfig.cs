using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ArchitectsStencilMod.Config
{
    /// <summary>
    /// Which SourceMaps interaction layers are included when compiling .itr files on the server or single-player host.
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

        public override void OnLoaded()
        {
            Instance = this;
        }
    }
}
