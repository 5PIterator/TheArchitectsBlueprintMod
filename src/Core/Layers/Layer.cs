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
        Custom,
        Liquid,
        Paint,
        Wiring,
        Detail,
    }

    /// <summary>
    /// Paste-order layer. Each concrete layer owns match, identify, clear, and place.
    /// After a successful place, <see cref="Place"/> calls <see cref="Consume"/> (and place-time checks use <see cref="CanConsume"/>).
    /// Identify returns which relatives (<see cref="Blueprint.BlueprintCell"/>) this layer claims for the current source tile (empty = no match).
    /// Plan only queues identified work; <see cref="PlacementValid"/> and <see cref="HasRequiredItems"/> are Place-time concerns (and identify diagnostics).
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
        public abstract LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject);

        /// <summary>
        /// Clears the given batch. Returns the remaining tiles that were not cleared.
        /// </summary>
        public abstract LayerResult Clear(TileBatch batch, in PasteContext context);

        /// <summary>
        /// Places the given batch. Returns the remaining tiles that were not placed.
        /// </summary>
        public abstract LayerResult Place(TileBatch batch, in PasteContext context);

        /// <summary>
        /// Consumes inventory for the given tile after a successful <see cref="Place"/> step. No-op layers return true.
        /// </summary>
        public abstract bool Consume(in PasteTile tile, in PasteContext context);

        /// <summary>
        /// Whether the items required for the given tile are available when <see cref="PasteContext.RequireItems"/> is set.
        /// </summary>
        public abstract bool CanConsume(in PasteTile tile, in PasteContext context);

        /// <summary>
        /// Whether this tile can be cleared at the target (for clear queue planning).
        /// </summary>
        public abstract bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>
        /// Whether world/rules allow placing this tile at Place time (ignores inventory — use <see cref="HasRequiredItems"/>).
        /// </summary>
        public abstract bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>
        /// Whether required items are available at Place time when <see cref="PasteContext.RequireItems"/> and inventory are in use.
        /// </summary>
        public abstract bool HasRequiredItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context);

        /// <summary>Both placement validity and item requirements (when applicable) for Place-time decisions.</summary>
        public bool CanPlace(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) =>
            PlacementValid(in tile, in lookup, in context) && HasRequiredItems(in tile, in lookup, in context);

        /// <summary>
        /// Returns the items that are required for the given tile. Returns null if layer does not consume items.
        /// </summary>
        public abstract IReadOnlyList<int>? GetItem(in PasteTile tile);

        /// <summary>True when this custom layer owns the given tile type (used by <see cref="CustomLayer"/> exclusion checks).</summary>
        public virtual bool IsCustom(ushort tileType) => false;
    }

    /// <summary>Marks a custom sub-layer discovered and composed by <see cref="CustomLayer"/>.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class CustomPasteLayerAttribute : Attribute { }
}
