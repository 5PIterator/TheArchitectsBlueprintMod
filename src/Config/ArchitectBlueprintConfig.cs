using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>Server policy for capture/paste safeguards (map exploration and player reach).</summary>
    public sealed class ArchitectBlueprintConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("MapExploration")]
        [DefaultValue(true)]
        public bool RestrictCaptureAndPasteToRevealedTiles;

        [Header("PlayerReach")]
        [DefaultValue(true)]
        public bool LimitCaptureAndPasteToPlayerReach;

        [DefaultValue(200)]
        [Range(16, 5000)]
        public int MaxReachDistanceTiles;
    }
}
