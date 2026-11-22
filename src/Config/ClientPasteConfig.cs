using System.ComponentModel;
using Terraria;
using Terraria.ModLoader.Config;
using TheArchitectsBlueprintMod.Core;
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

    /// <summary>Client-only projection world-preview quality preferences.</summary>
    public sealed class ProjectionPreviewPreference
    {
        [DefaultValue(true)]
        public bool UseCachedBlit = true;

        [DefaultValue(false)]
        public bool NearbyOnly;

        [DefaultValue(48)]
        [Range(8, 200)]
        public int NearbyRadiusTiles = 48;
    }

    /// <summary>Client copy/paste safeguard preferences honored when the server allows them.</summary>
    public sealed class BlueprintSafeguardPreference
    {
        [DefaultValue(true)]
        public bool RestrictCopyAndPasteToRevealedTiles = true;

        [DefaultValue(true)]
        public bool LimitCopyAndPasteToPlayerReach = true;

        [DefaultValue(200)]
        [Range(16, 5000)]
        public int MaxReachDistanceTiles = 200;
    }

    /// <summary>Client-side paste preferences carried with each paste request. Server policy decides which are honored.</summary>
    public sealed class ClientPasteConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Preferences")]
        public PlanPastePreference Plan = new();
        public ClearPastePreference Clear = new();
        public PlacePastePreference Place = new();

        [Header("Safeguards")]
        public BlueprintSafeguardPreference Safeguards = new();

        [Header("Arrow")]
        public ArrowPastePreference Arrow = new();

        [Header("Preview")]
        public ProjectionPreviewPreference Preview = new();

        [Header("Debug")]
        public PasteOverlayDebugConfig Overlay = new();

        public override void OnChanged()
        {
            // --- Refresh or clear guard overlays when local safeguard prefs change.

            if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
                return;

            BlueprintGuard.RefreshOrClearForLocalPlayer(player);
        }
    }
}
