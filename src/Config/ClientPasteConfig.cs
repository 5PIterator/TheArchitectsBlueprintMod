using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>Client clear-phase preference sent with paste requests when the server allows it.</summary>
    public sealed class ClearPastePreference
    {
        [DefaultValue(ClearPhaseStrategy.Transparent)]
        public ClearPhaseStrategy Strategy = ClearPhaseStrategy.Transparent;
    }

    /// <summary>Client place-phase tolerance preferences sent with paste requests when the server allows them.</summary>
    public sealed class PlacePastePreference
    {
        [DefaultValue(false)]
        public bool RetryOnMissingItems;

        [DefaultValue(false)]
        public bool RetryOnInvalidPlacement;
    }

    /// <summary>Client sweep-phase preference sent with paste requests when the server allows it.</summary>
    public sealed class SweepPastePreference
    {
        [DefaultValue(SweepPhaseStrategy.Detail)]
        public SweepPhaseStrategy Strategy = SweepPhaseStrategy.Detail;
    }

    /// <summary>Client-only paste overlay debug visuals.</summary>
    public sealed class PasteOverlayDebugConfig
    {
        [DefaultValue(false)]
        public bool RectBorders;
    }

    /// <summary>Client-side paste preferences carried with each paste request. Server policy decides which are honored.</summary>
    public sealed class ClientPasteConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Preferences")]
        public ClearPastePreference Clear = new();
        public PlacePastePreference Place = new();
        public SweepPastePreference Sweep = new();

        [Header("Debug")]
        public PasteOverlayDebugConfig Overlay = new();
    }
}
