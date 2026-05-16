using System;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.Layers
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
    /// Identify returns which relatives (<see cref="Blueprint.BlueprintCell"/>) this layer claims for the current source tile (empty = no match).
    /// <see cref="PlacementValid"/> and <see cref="HasRequiredItems"/> split planning: world/rules vs inventory when <see cref="PasteContext.NeedsItems"/> applies.
    /// </summary>
    internal abstract class Layer(string id, LayerType layerType)
    {
        /// <summary>Stable id; must stay aligned with <see cref="PastingConfig.IsLayerIdentificationEnabled"/>.</summary>
        public string Id { get; } = id ?? throw new ArgumentNullException(nameof(id));
        public LayerType LayerType { get; } = layerType;

        /// <summary>
        /// Whether the target already matches intent (<see cref="PasteTile.PayloadTile"/> vs <see cref="PasteTile.Target"/>; source cut uses empty <see cref="PasteTile.Source"/> vs live source).
        /// </summary>
        public abstract bool Match(in PasteTile tile);

        /// <summary>
        /// Classifies tiles for this layer. Use <see cref="PasteTile.GetTileForIdentify"/> with <paramref name="identifySubject"/> (<see cref="PasteSubject.Payload"/> when placing, <see cref="PasteSubject.Target"/> when clearing at destination).
        /// </summary>
        public abstract IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject);

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
        /// Whether this tile can be cleared at the target (for clear queue planning).
        /// </summary>
        public abstract bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>
        /// Whether world/rules allow placing this tile (ignores inventory when items are required — use <see cref="HasRequiredItems"/>).
        /// </summary>
        public abstract bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>
        /// Whether required items are available when <see cref="PasteContext.NeedsItems"/> and inventory are in use.
        /// </summary>
        public abstract bool HasRequiredItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>Both placement validity and item requirements (when applicable).</summary>
        public bool CanPlace(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) =>
            PlacementValid(in tile, in lookup, in context) && HasRequiredItems(in tile, in lookup, in context);

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
