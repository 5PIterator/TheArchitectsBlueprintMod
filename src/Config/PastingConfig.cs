using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>Per-client paste caps enforced on requests and during server validation.</summary>
    public sealed class ClientPasteLimitsConfig
    {
        [DefaultValue(200)]
        [Range(1, 2000)]
        public int MaxPasteWidth = 200;

        [DefaultValue(200)]
        [Range(1, 2000)]
        public int MaxPasteHeight = 200;

        [DefaultValue(5000)]
        [Range(1, 500000)]
        public int VolumeLimit = 5000;

        [DefaultValue(1)]
        [Range(1, 128)]
        public int PlayerOperationLimit = 1;
    }

    /// <summary>Server-wide paste execution caps.</summary>
    public sealed class ServerPasteLimitsConfig
    {
        [DefaultValue(8)]
        [Range(1, 512)]
        public int ServerOperationLimit = 8;

        [DefaultValue(512)]
        [Range(1, 512)]
        public int MaxPhaseTickSize = 512;

        [DefaultValue(4096)]
        [Range(1, 16384)]
        public int MaxTilesPerCategory = 4096;
    }

    /// <summary>Global paste limits grouped by enforcement scope.</summary>
    public sealed class GlobalLimitsConfig
    {
        [Expand(false)]
        public ClientPasteLimitsConfig Client = new();

        [Expand(false)]
        public ServerPasteLimitsConfig Server = new();
    }

    /// <summary>Clear-phase execution, strategy defaults, and client preference policy.</summary>
    public sealed class ClearPhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(100)]
        [Range(1, 512)]
        public int TickSize = 100;

        [DefaultValue(100)]
        [Range(0, 5000)]
        public int TickDelayMs = 100;

        [DefaultValue(ClearPhaseStrategy.Transparent)]
        public ClearPhaseStrategy Strategy = ClearPhaseStrategy.Transparent;

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        public PastePhaseSettings ToPhaseSettings() => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickDelayMs = TickDelayMs,
        };
    }

    /// <summary>Place-phase execution, strategy, item rules, defaults, and client preference policy.</summary>
    public sealed class PlacePhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(100)]
        [Range(1, 512)]
        public int TickSize = 100;

        [DefaultValue(100)]
        [Range(0, 5000)]
        public int TickDelayMs = 100;

        [DefaultValue(PlacePhaseStrategy.Place)]
        public PlacePhaseStrategy Strategy = PlacePhaseStrategy.Place;

        [DefaultValue(true)]
        public bool RequireItems = true;

        [DefaultValue(false)]
        public bool RetryOnMissingItems;

        [DefaultValue(false)]
        public bool RetryOnInvalidPlacement;

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        [DefaultValue(false)]
        public bool EnableCutPasteProcess;

        public PastePhaseSettings ToPhaseSettings() => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickDelayMs = TickDelayMs,
        };
    }

    /// <summary>Sweep-phase execution, strategy defaults, and client preference policy.</summary>
    public sealed class SweepPhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(512)]
        [Range(1, 512)]
        public int TickSize = 512;

        [DefaultValue(0)]
        [Range(0, 5000)]
        public int TickDelayMs;

        [DefaultValue(SweepPhaseStrategy.Detail)]
        public SweepPhaseStrategy Strategy = SweepPhaseStrategy.Detail;

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        public PastePhaseSettings ToPhaseSettings() => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickDelayMs = TickDelayMs,
        };
    }

    /// <summary>Per-phase paste policy: plan, clear, cut, place, and sweep.</summary>
    public sealed class PhasePolicyConfig
    {
        [Expand(false)]
        public PastePhaseSettings Plan = new() { Active = true, TickSize = 50, TickDelayMs = 0 };

        [Expand(false)]
        public ClearPhaseConfig Clear = new();

        [Expand(false)]
        public PastePhaseSettings Cut = new() { Active = true, TickSize = 100, TickDelayMs = 100 };

        [Expand(false)]
        public PlacePhaseConfig Place = new();

        [Expand(false)]
        public SweepPhaseConfig Sweep = new();
    }

    /// <summary>Per-layer toggles for paste task building. When false, that layer is not identified and generates no clear/place tasks.</summary>
    public sealed class LayerPolicySettings
    {
        [DefaultValue(true)] public bool Wall = true;
        [DefaultValue(true)] public bool Block = true;
        [DefaultValue(true)] public bool FallBlock = true;
        [DefaultValue(true)] public bool Object = true;
        [DefaultValue(true)] public bool Vegetation = true;
        [DefaultValue(true)] public bool Tree = true;
        [DefaultValue(true)] public bool Paint = true;
        [DefaultValue(true)] public bool Wiring = true;
        [DefaultValue(true)] public bool Detail = true;
        [DefaultValue(true)] public bool Fluid = true;
        [DefaultValue(true)] public bool Unknown = true;
    }

    /// <summary>Authoritative server paste policy: limits, phase execution, strategy defaults, and layer toggles.</summary>
    public sealed class PastingConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("GlobalLimits")]
        public GlobalLimitsConfig Limits = new();

        [Header("PhasePolicy")]
        public PhasePolicyConfig Phases = new();

        [Header("LayerPolicy")]
        public LayerPolicySettings LayerPolicy = new();

        /// <summary>Whether task building should run identification for the paste layer with the given <c>Layer.Id</c>.</summary>
        public bool IsLayerIdentificationEnabled(string layerId)
        {
            var s = LayerPolicy;
            return layerId switch
            {
                "Wall" => s.Wall,
                "Block" => s.Block,
                "FallBlock" => s.FallBlock,
                "Object" => s.Object,
                "Vegetation" => s.Vegetation,
                "Tree" => s.Tree,
                "Paint" => s.Paint,
                "Wiring" => s.Wiring,
                "Detail" => s.Detail,
                "Fluid" => s.Fluid,
                "Unknown" => s.Unknown,
                _ => true,
            };
        }
    }
}
