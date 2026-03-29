using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core.Layers
{
    internal enum LayerType
    {
        Wall,
        Block,
        Frame,
        Liquid,
        Paint,
        Wiring,
        Detail,
        Sweep,
    }

    /// <summary>
    /// Paste-order layer. Each concrete layer owns its own match, identify, clear, place, and consume behavior.
    /// Identify returns which blueprint cells this layer claims for the current source tile (empty = no match).
    /// CanPlace decides whether placement can be planned given inventory reservation rules.
    /// </summary>
    internal abstract class Layer(string id, LayerType layerType)
    {
        /// <summary>Stable id; must stay aligned with <see cref="PastingConfig.IsLayerIdentificationEnabled"/>.</summary>
        public string Id { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public LayerType LayerType { get; } = layerType;

        public abstract bool Match(in PasteTile tile);

        /// <summary>
        /// Classifies the source tile for this layer. Empty list means this layer does not own the tile.
        /// </summary>
        public abstract IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup);

        /// <summary>
        /// Whether placement for the identified cells can be planned (e.g. item availability when <see cref="PasteContext.NeedsItems"/>).
        /// </summary>
        public abstract bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context);

        /// <summary>
        /// For <see cref="CustomLayer.IsCustom"/>: tile types owned by this custom layer so built-in layers (e.g. Block) can exclude them.
        /// </summary>
        internal virtual bool IsCustomTileType(ushort tileType) => false;

        public abstract IReadOnlyList<int>? GetItem(in Tile sourceTile);
        public abstract LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context);
        public abstract LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context);
        public abstract bool Consume(in PasteTile tile, in PasteContext context);

        /// <summary>Shared empty identify result (no layer match).</summary>
        protected static IReadOnlyList<Blueprint.BoundedTile> NoIdentify() => Array.Empty<Blueprint.BoundedTile>();

        internal static bool ConsumeNoOp(in PasteTile tile, in PasteContext context) => true;
    }

    /// <summary>Marks a <see cref="Layer"/> subclass discovered by <see cref="CustomLayer.BuildCustomLayers"/>.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class CustomPasteLayerAttribute : Attribute { }
}
