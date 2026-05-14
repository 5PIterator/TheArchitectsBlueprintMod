using System;
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ArchitectsBlueprintMod.Config
{
    /// <summary>Per-layer toggles for paste task building. When false, that layer is not identified and generates no clear/place tasks.</summary>
    public sealed class LayerIdentificationSettings
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
        [DefaultValue(true)] public bool Sweep = true;
        [DefaultValue(true)] public bool Fluid = true;
        [DefaultValue(true)] public bool Unknown = true;
    }

    public sealed class PastingConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("General")]

        [DefaultValue(true)]
        public bool NeedsItems;

        [DefaultValue(100)]
        [Range(0, 5000)]
        public int PasteDelayMs;

        [DefaultValue(100)]
        [Range(1, 512)]
        public int MaxPasteStepSize = 100;

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

        [DefaultValue(false)]
        public bool CheatSweep;

        [Header("Debug")]

        [DefaultValue(false)]
        public bool EnableCutPasteProcess;

        [Header("LayerIdentification")]
        public LayerIdentificationSettings LayerIdentification = new();

        /// <summary>Whether task building should run identification for the paste layer with the given <c>Layer.Id</c>.</summary>
        public bool IsLayerIdentificationEnabled(string layerId)
        {
            var s = LayerIdentification;
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
                "Sweep" => s.Sweep,
                "Fluid" => s.Fluid,
                "Unknown" => s.Unknown,
                _ => true,
            };
        }
    }
}

