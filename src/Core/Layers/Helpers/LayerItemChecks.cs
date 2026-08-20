using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Central plan-time item availability checks mirroring <see cref="TileExecutor"/> / manifest demand families.</summary>
    internal static class LayerItemChecks
    {
        /// <summary>Returns true when item requirements are disabled, unavailable, or satisfied for this layer tile.</summary>
        public static bool HasRequiredItems(
            Layer layer,
            in PasteTile tile,
            in PasteTileLookup lookup,
            in PasteContext context)
        {
            if (!context.RequireItems || context.InventoryLookup is null || context.IsSweep)
                return true;

            return layer.Id switch
            {
                "Custom" => false,
                "Wall" => HasRequiredWallItems(in tile, in context),
                "Block" => HasRequiredBlockItems(in tile, in context),
                "Falling" => HasRequiredFallingItems(in tile, in context),
                "Frame" => HasRequiredFrameItems(in tile, in context),
                "Plant" => HasRequiredTileItems(in tile, in context),
                "Vine" => HasRequiredTileItems(in tile, in context),
                "Tree" => HasRequiredTreeItems(in tile, in lookup, in context),
                "Fluid" => HasRequiredFluidItems(in tile, in context),
                "Paint" => HasRequiredPaintItems(tile.PayloadCell.Paint, tile.Target, in context),
                "Wiring" => HasRequiredWireItems(tile.PayloadCell.Wire, tile.Target, in context),
                "Unknown" => HasRequiredUnknownItems(in tile, in context),
                _ => true,
            };
        }

        /// <summary>True when the unknown tile has a mapped interaction and inventory can supply it.</summary>
        private static bool HasRequiredUnknownItems(in PasteTile tile, in PasteContext context)
        {
            // --- Unresolved map rows count as missing so plan diagnostics match Place failure.
            if (!tile.PayloadCell.HasTile())
                return true;

            ushort tileType = (ushort)tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.ResolvePlacementStyle();
            if (!InteractionMapProvider.HasResolvedTileInteraction(tileType, style))
                return false;

            return TileExecutor.HasItemForTile(tileType, style, in context);
        }

        /// <summary>True when inventory can supply every paint color and coating the intent still needs.</summary>
        internal static bool HasRequiredPaintItems(in PayloadCell.PaintData intent, in Tile target, in PasteContext context)
        {
            if (intent.TileColor != PaintID.None
                && target.TileColor != intent.TileColor
                && !TileExecutor.HasItemForPaint(intent.TileColor, in context))
                return false;

            if (intent.WallColor != PaintID.None
                && target.WallColor != intent.WallColor
                && !TileExecutor.HasItemForPaint(intent.WallColor, in context))
                return false;

            if (NeedsEchoCoating(in intent, in target)
                && !TileExecutor.HasItemForPaint(ContentNameResolver.EchoCoatingId, in context))
                return false;

            if (NeedsIlluminantCoating(in intent, in target)
                && !TileExecutor.HasItemForPaint(ContentNameResolver.IlluminantCoatingId, in context))
                return false;

            return true;
        }

        /// <summary>True when inventory can supply every wire color and actuator the intent still needs.</summary>
        internal static bool HasRequiredWireItems(in PayloadCell.WireData intent, in Tile target, in PasteContext context)
        {
            if (intent.RedWire && !target.RedWire && !HasWireKindItems(0, in context)) return false;
            if (intent.BlueWire && !target.BlueWire && !HasWireKindItems(2, in context)) return false;
            if (intent.GreenWire && !target.GreenWire && !HasWireKindItems(1, in context)) return false;
            if (intent.YellowWire && !target.YellowWire && !HasWireKindItems(3, in context)) return false;
            if (intent.HasActuator && !target.HasActuator && !HasWireKindItems(4, in context)) return false;
            return true;
        }

        private static bool HasRequiredWallItems(in PasteTile tile, in PasteContext context)
        {
            ushort wallType = (ushort)tile.PayloadCell.Wall.Type;
            if (wallType == WallID.None)
                return true;
            return TileExecutor.HasItemForWall(wallType, in context);
        }

        private static bool HasRequiredBlockItems(in PasteTile tile, in PasteContext context)
        {
            if (!BlockLayer.IsBlock(tile.PayloadTile))
                return true;
            if (!tile.PayloadCell.HasTile())
                return true;

            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileInteractionChecks.HasTilePlacementItems(
                (ushort)tile.PayloadCell.Block.Type,
                style,
                tile.World.X,
                tile.World.Y,
                in context);
        }

        private static bool HasRequiredFallingItems(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        private static bool HasRequiredFrameItems(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            FrameOperations.FramePlacementVariant variant = FrameOperations.ResolveFramePlacementVariant(tile.PayloadCell);
            return TileInteractionChecks.HasTilePlacementItems(
                (ushort)tile.PayloadCell.Block.Type,
                variant.Style,
                tile.World.X,
                tile.World.Y,
                in context);
        }

        private static bool HasRequiredTileItems(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            int style = tile.PayloadCell.ResolvePlacementStyle();
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        private static bool HasRequiredTreeItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;
            if (!TreeLayer.TryResolveTreeInteraction(in tile, lookup, out InteractionEntry? entry) || entry is null)
                return false;
            return TileExecutor.HasItemForInteraction(entry, in context);
        }

        private static bool HasRequiredFluidItems(in PasteTile tile, in PasteContext context)
        {
            PayloadCell.LiquidData intent = tile.PayloadCell.Liquid;
            if (intent.Amount <= 0)
                return true;

            Tile target = tile.Target;
            if (target.LiquidAmount == intent.Amount && target.LiquidType == intent.Type)
                return true;

            return TileExecutor.HasItemForLiquid(intent.Type, in context);
        }

        private static bool HasWireKindItems(int wireKind, in PasteContext context)
        {
            if (context.InventoryLookup is null)
                return false;

            TileItemAvailability availability = context.InventoryLookup.GetWireItemAvailability(
                wireKind,
                context.InteractionBudget);
            if (availability == TileItemAvailability.Available)
                return true;
            if (availability != TileItemAvailability.UnresolvedInteraction)
                return false;

            Item[]? items = context.InventoryLookup.GetContents();
            if (items is null)
                return false;

            int fallbackItem = wireKind == 4 ? ItemID.Actuator : ItemID.Wire;
            for (int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                if (item.type == fallbackItem && item.stack > 0)
                    return true;
            }

            return false;
        }

        private static bool NeedsEchoCoating(in PayloadCell.PaintData intent, in Tile target) =>
            (intent.TileInvisible && target.HasTile && !target.IsTileInvisible)
            || (intent.WallInvisible && target.WallType != WallID.None && !target.IsWallInvisible);

        private static bool NeedsIlluminantCoating(in PayloadCell.PaintData intent, in Tile target) =>
            (intent.TileFullbright && target.HasTile && !target.IsTileFullbright)
            || (intent.WallFullbright && target.WallType != WallID.None && !target.IsWallFullbright);
    }
}
