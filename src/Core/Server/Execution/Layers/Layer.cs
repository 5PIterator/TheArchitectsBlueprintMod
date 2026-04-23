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
    /// Paste-order layer. Each concrete layer owns match, identify, clear, and place.
    /// After a successful place, <see cref="Place"/> calls <see cref="Consume"/> (and planning uses <see cref="CanConsume"/>).
    /// Identify returns which relatives (<see cref="Selection.BoundedTile"/>) this layer claims for the current source tile (empty = no match).
    /// CanPlace decides whether placement can be planned given inventory reservation rules.
    /// </summary>
    internal abstract class Layer(string id, LayerType layerType)
    {
        /// <summary>Stable id; must stay aligned with <see cref="PastingConfig.IsLayerIdentificationEnabled"/>.</summary>
        public string Id { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public LayerType LayerType { get; } = layerType;

        /// <summary>
        /// Whether the target already matches placement intent (<see cref="PasteTile.Source"/> vs <see cref="PasteTile.Target"/>; cut clears use empty <see cref="PasteTile.Source"/>).
        /// </summary>
        public abstract bool Match(in PasteTile tile);

        /// <summary>
        /// Classifies tiles for this layer using <see cref="PasteTile.IdentifyFace"/> (blueprint <see cref="PasteTile.PayloadCell"/> when placing, live <see cref="PasteTile.Target"/> when clearing).
        /// </summary>
        public abstract IReadOnlyList<Selection.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup lookup);

        /// <summary>
        /// Clears the given tiles. Returns the remaining tiles that were not cleared.
        /// </summary>
        public abstract LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context);

        /// <summary>
        /// Places the given tiles. Returns the remaining tiles that were not placed.
        /// </summary>
        public abstract LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context);

        /// <summary>
        /// Consumes inventory for the given tile after a successful <see cref="Place"/> step. No-op layers return true.
        /// </summary>
        public abstract bool Consume(in PasteTile tile, in PasteContext context);

        /// <summary>
        /// Whether the items required for the given tile are available when <see cref="PasteContext.NeedsItems"/> is set.
        /// </summary>
        public abstract bool CanConsume(in PasteTile tile, in PasteContext context);

        /// <summary>
        /// Whether placement for this tile can be performed.
        /// </summary>
        public abstract bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>
        /// Whether placement for this tile can be performed.
        /// </summary>
        public abstract bool CanPlace(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>
        /// Returns the items that are required for the given tile. Returns null if layer does not consume items.
        /// </summary>
        public abstract IReadOnlyList<int>? GetItem(in PasteTile tile);

        /// <summary>True when this custom layer owns the given tile type (used by <see cref="CustomLayer"/> exclusion checks).</summary>
        public virtual bool IsCustom(ushort tileType) => false;
    }

    /// <summary>Marks a <see cref="Layer"/> subclass discovered by <see cref="CustomLayer.DiscoverAttributedLayers"/>.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class CustomPasteLayerAttribute : Attribute { }
}
