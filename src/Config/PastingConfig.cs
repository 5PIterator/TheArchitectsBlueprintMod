using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>Global paste limits enforced for every phase and operation.</summary>
    public sealed class PasteLimitsConfig
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

        [DefaultValue(8)]
        [Range(1, 512)]
        public int ServerOperationLimit = 8;

        [DefaultValue(512)]
        [Range(1, 512)]
        public int MaxPhaseTickSize = 512;
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

        [DefaultValue(true)]
        public bool IgnoreMissingItems = true;

        [DefaultValue(true)]
        public bool IgnoreInvalidPlacement = true;

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

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

    /// <summary>Per-layer toggles for paste task building. When false, that layer is not identified and generates no clear/place tasks.</summary>
    public sealed class LayerPolicySettings
    {
        [DefaultValue(true)] public bool Wall = true;
        [DefaultValue(true)] public bool Block = true;
        [DefaultValue(true)] public bool FallBlock = true;
        [DefaultValue(true)] public bool TileAnchored = true;
        [DefaultValue(true)] public bool WallAnchored = true;
        [DefaultValue(true)] public bool OtherAnchored = true;
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

        [Header("Limits")]
        public PasteLimitsConfig Limits = new();

        [Header("PhaseExecution")]
        public PastePhaseSettings Plan = new() { Active = true, TickSize = 50, TickDelayMs = 0 };
        public PastePhaseSettings Cut = new() { Active = true, TickSize = 100, TickDelayMs = 100 };

        [Header("ClearPolicy")]
        public ClearPhaseConfig Clear = new();

        [Header("PlacePolicy")]
        public PlacePhaseConfig Place = new();

        [Header("SweepPolicy")]
        public SweepPhaseConfig Sweep = new();

        [Header("Advanced")]
        [DefaultValue(false)]
        public bool EnableCutPasteProcess;

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
                "TileAnchored" => s.TileAnchored,
                "WallAnchored" => s.WallAnchored,
                "OtherAnchored" => s.OtherAnchored,
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
