using System.ComponentModel;
using Terraria.ModLoader.Config;
using TheArchitectsBlueprintMod.Core.Phases;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>Client clear-phase strategy preferences sent with paste requests when the server allows them.</summary>
    public sealed class ClearPasteStrategies
    {
        [DefaultValue(ClearItemDropStyle.Void)]
        public ClearItemDropStyle ItemDrop = ClearItemDropStyle.Void;
    }

    /// <summary>Client plan-phase strategy preferences sent with paste requests when the server allows them.</summary>
    public sealed class PlanPasteStrategies
    {
        [DefaultValue(ClearStyle.Transparent)]
        public ClearStyle Style = ClearStyle.Transparent;
    }

    /// <summary>Client place-phase strategy preferences sent with paste requests when the server allows them.</summary>
    public sealed class PlacePasteStrategies
    {
        [DefaultValue(false)]
        public bool RetryOnMissingItems;

        [DefaultValue(false)]
        public bool RetryOnInvalidPlacement;
    }

    /// <summary>Client sweep-phase strategy preferences sent with paste requests when the server allows them.</summary>
    public sealed class SweepPasteStrategies
    {
        [DefaultValue(SweepStyle.Detail)]
        public SweepStyle Style = SweepStyle.Detail;
    }

    /// <summary>Client clear-phase preference sent with paste requests when the server allows it.</summary>
    public sealed class ClearPastePreference
    {
        [Expand(true)]
        public ClearPasteStrategies Strategy = new();
    }

    /// <summary>Client plan-phase preference sent with paste requests when the server allows it.</summary>
    public sealed class PlanPastePreference
    {
        [Expand(true)]
        public PlanPasteStrategies Strategy = new();
    }

    /// <summary>Client place-phase tolerance preferences sent with paste requests when the server allows them.</summary>
    public sealed class PlacePastePreference
    {
        [Expand(true)]
        public PlacePasteStrategies Strategy = new();
    }

    /// <summary>Client sweep-phase preference sent with paste requests when the server allows it.</summary>
    public sealed class SweepPastePreference
    {
        [Expand(true)]
        public SweepPasteStrategies Strategy = new();
    }

    /// <summary>Client arrow flight preferences for paste projectiles.</summary>
    public sealed class ArrowPastePreference
    {
        [DefaultValue(48)]
        [Range(10, 600)]
        public int Speed = 48;

        [DefaultValue(2.5f)]
        [Range(0.5f, 48f)]
        public float MinSpeed = 2.5f;

        [DefaultValue(24f)]
        [Range(1f, 96f)]
        public float MaxSpeed = 24f;
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
        public PlanPastePreference Plan = new();
        public ClearPastePreference Clear = new();
        public PlacePastePreference Place = new();
        public SweepPastePreference Sweep = new();

        [Header("Arrow")]
        public ArrowPastePreference Arrow = new();

        [Header("Debug")]
        public PasteOverlayDebugConfig Overlay = new();
    }
}
