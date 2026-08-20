using System;
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
        Entity,
        Custom,
        Liquid,
        Paint,
        Wiring,
        Detail,
    }

    /// <summary>
    /// Paste-order layer. Each concrete layer owns match, identify, clear, and place.
    /// Identify returns which relatives (<see cref="Blueprint.BlueprintCell"/>) this layer claims for the current source tile (empty = no match).
    /// Plan only queues identified work; <see cref="PlacementValid"/> and <see cref="HasRequiredItems"/> are Place-time concerns (and identify diagnostics).
    /// Item reservation and consumption happen in <see cref="TileExecutor"/> via <see cref="PasteContext.InventoryLookup"/>, not layer hooks.
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
        /// <paramref name="context"/> supplies payload sidecar data for layers that are not tile-backed.
        /// </summary>
        public abstract LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context, PasteSubject identifySubject);

        /// <summary>
        /// Clears the given batch. Returns the remaining tiles that were not cleared.
        /// </summary>
        public abstract LayerResult Clear(TileBatch batch, in PasteContext context);

        /// <summary>
        /// Places the given batch. Returns the remaining tiles that were not placed.
        /// </summary>
        public abstract LayerResult Place(TileBatch batch, in PasteContext context);

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
        /// Plan-time hint only; place-time authority is <see cref="TileExecutor"/> reservation checks.
        /// </summary>
        public virtual bool HasRequiredItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) =>
            LayerItemChecks.HasRequiredItems(this, in tile, in lookup, in context);

        /// <summary>Both placement validity and item requirements (when applicable) for Place-time decisions.</summary>
        public bool CanPlace(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) =>
            PlacementValid(in tile, in lookup, in context) && HasRequiredItems(in tile, in lookup, in context);

        /// <summary>True when this custom layer owns the given tile type (used by <see cref="CustomLayer"/> exclusion checks).</summary>
        public virtual bool IsCustom(ushort tileType) => false;

        /// <summary>When true, preview walks visible payload cells and calls <see cref="DrawCell"/>.</summary>
        public virtual bool DrawsCells => false;

        /// <summary>Optional per-pass setup before cell or region drawing in payload preview.</summary>
        public virtual void BeginPass(in PreviewDrawContext ctx) { }

        /// <summary>Draws one visible payload cell during preview. No-op for region-only layers.</summary>
        public virtual void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY) { }

        /// <summary>Runs after the cell walk (or instead of it) during preview.</summary>
        public virtual void DrawPass(in PreviewDrawContext ctx) { }
    }

    /// <summary>Marks a custom sub-layer discovered and composed by <see cref="CustomLayer"/>.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class CustomPasteLayerAttribute : Attribute { }
}
