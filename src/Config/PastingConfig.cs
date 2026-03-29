using System;
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ArchitectsStencilMod.Config
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

        [DefaultValue(5)]
        [Range(1, 100)]
        public int PasteStepSize;

        [DefaultValue(false)]
        public bool CheatSweep;

        [DefaultValue(true)]
        public bool ForceTiles = true;

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

