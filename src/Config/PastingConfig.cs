using System.ComponentModel;
using Terraria.ModLoader.Config;
using TheArchitectsBlueprintMod.Core.Phases;

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

    /// <summary>Place-phase behavioral strategies that modify how placement runs.</summary>
    public sealed class PlacePhaseStrategies
    {
        [DefaultValue(true)]
        public bool RequireItems = true;

        [DefaultValue(false)]
        public bool RetryOnMissingItems;

        [DefaultValue(false)]
        public bool RetryOnInvalidPlacement;

        [DefaultValue(PlaceStyle.Place)]
        public PlaceStyle Style = PlaceStyle.Place;
    }

    /// <summary>Sweep-phase behavioral strategies that modify how sweeping runs.</summary>
    public sealed class SweepPhaseStrategies
    {
        [DefaultValue(SweepStyle.Detail)]
        public SweepStyle Style = SweepStyle.Detail;
    }

    /// <summary>Plan-phase strategies that control how clear/place task queues are built.</summary>
    public sealed class PlanPhaseStrategies
    {
        [DefaultValue(ClearStyle.Transparent)]
        public ClearStyle Style = ClearStyle.Transparent;
    }

    /// <summary>Clear-phase strategies that control item handling while tiles are destroyed.</summary>
    public sealed class ClearPhaseStrategies
    {
        [DefaultValue(ClearItemDropStyle.Void)]
        public ClearItemDropStyle ItemDrop = ClearItemDropStyle.Void;
    }

    /// <summary>Cut-phase execution strategy.</summary>
    public sealed class CutPhaseStrategies
    {
        [DefaultValue(false)]
        public bool EnableCutPasteProcess;
    }

    /// <summary>Plan-phase execution policy.</summary>
    public sealed class PlanPhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(50)]
        [Range(1, 512)]
        public int TickSize = 50;

        [DefaultValue(0)]
        [Range(0, 5000)]
        public int TickSpeed;

        [Expand(true)]
        public PlanPhaseStrategies Strategy = new();

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        public PastePhasePolicy<PlanPhaseStrategy> ToPolicy(in PlanPhaseStrategy strategy) => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickSpeed = TickSpeed,
            Strategy = strategy,
        };
    }

    /// <summary>Cut-phase execution policy.</summary>
    public sealed class CutPhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(100)]
        [Range(1, 512)]
        public int TickSize = 100;

        [DefaultValue(100)]
        [Range(0, 5000)]
        public int TickSpeed = 100;

        [Expand(true)]
        public CutPhaseStrategies Strategy = new();

        public PastePhasePolicy<CutPhaseStrategy> ToPolicy(in CutPhaseStrategy strategy) => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickSpeed = TickSpeed,
            Strategy = strategy,
        };
    }

    /// <summary>Clear-phase execution policy.</summary>
    public sealed class ClearPhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(100)]
        [Range(1, 512)]
        public int TickSize = 100;

        [DefaultValue(100)]
        [Range(0, 5000)]
        public int TickSpeed = 100;

        [Expand(true)]
        public ClearPhaseStrategies Strategy = new();

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        public PastePhasePolicy<ClearPhaseStrategy> ToPolicy(in ClearPhaseStrategy strategy) => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickSpeed = TickSpeed,
            Strategy = strategy,
        };
    }

    /// <summary>Place-phase execution, strategies, and client preference policy.</summary>
    public sealed class PlacePhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(100)]
        [Range(1, 512)]
        public int TickSize = 100;

        [DefaultValue(100)]
        [Range(0, 5000)]
        public int TickSpeed = 100;

        [Expand(true)]
        public PlacePhaseStrategies Strategy = new();

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        public PastePhasePolicy<PlacePhaseStrategy> ToPolicy(in PlacePhaseStrategy strategy) => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickSpeed = TickSpeed,
            Strategy = strategy,
        };
    }

    /// <summary>Sweep-phase execution, strategies, and client preference policy.</summary>
    public sealed class SweepPhaseConfig
    {
        [DefaultValue(true)]
        public bool Active = true;

        [DefaultValue(512)]
        [Range(1, 512)]
        public int TickSize = 512;

        [DefaultValue(0)]
        [Range(0, 5000)]
        public int TickSpeed;

        [Expand(true)]
        public SweepPhaseStrategies Strategy = new();

        [DefaultValue(true)]
        public bool AllowClientPreferences = true;

        public PastePhasePolicy<SweepPhaseStrategy> ToPolicy(in SweepPhaseStrategy strategy) => new()
        {
            Active = Active,
            TickSize = TickSize,
            TickSpeed = TickSpeed,
            Strategy = strategy,
        };
    }

    /// <summary>Per-phase paste policy: plan, clear, cut, place, and sweep.</summary>
    public sealed class PhasePolicyConfig
    {
        [Expand(false)]
        public PlanPhaseConfig Plan = new();

        [Expand(false)]
        public ClearPhaseConfig Clear = new();

        [Expand(false)]
        public CutPhaseConfig Cut = new();

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
        [DefaultValue(true)] public bool Falling = true;
        [DefaultValue(true)] public bool Frame = true;
        [DefaultValue(true)] public bool Custom = true;
        [DefaultValue(true)] public bool Paint = true;
        [DefaultValue(true)] public bool Wiring = true;
        [DefaultValue(true)] public bool Detail = true;
        [DefaultValue(true)] public bool Fluid = true;
        [DefaultValue(true)] public bool Unknown = true;
    }

    /// <summary>Per-handler toggles for attributed custom sub-layers.</summary>
    public sealed class CustomLayerPolicySettings
    {
        [DefaultValue(true)] public bool Plant = true;
        [DefaultValue(true)] public bool Vine = true;
        [DefaultValue(true)] public bool Tree = true;
    }

    /// <summary>Main pipeline layer toggles and attributed custom sub-layer toggles.</summary>
    public sealed class LayerPolicyConfig
    {
        [Expand(false)]
        public LayerPolicySettings MainLayers = new();

        [Expand(false)]
        public CustomLayerPolicySettings CustomLayers = new();
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
        public LayerPolicyConfig LayerPolicy = new();

        /// <summary>Whether task building should run identification for the paste layer with the given <c>Layer.Id</c>.</summary>
        public bool IsLayerIdentificationEnabled(string layerId)
        {
            var mainLayers = LayerPolicy.MainLayers;
            var customLayers = LayerPolicy.CustomLayers;
            return layerId switch
            {
                "Wall" => mainLayers.Wall,
                "Block" => mainLayers.Block,
                "Falling" => mainLayers.Falling,
                "Frame" => mainLayers.Frame,
                "Custom" => mainLayers.Custom,
                "Plant" => customLayers.Plant,
                "Vine" => customLayers.Vine,
                "Tree" => customLayers.Tree,
                "Paint" => mainLayers.Paint,
                "Wiring" => mainLayers.Wiring,
                "Detail" => mainLayers.Detail,
                "Fluid" => mainLayers.Fluid,
                "Unknown" => mainLayers.Unknown,
                _ => true,
            };
        }
    }
}
