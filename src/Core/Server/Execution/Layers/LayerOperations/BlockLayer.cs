using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Simple solid tiles: one cell per <see cref="Identify"/>; multi-tile objects and frames belong to other layers.</summary>
    internal sealed class BlockLayer : Layer
    {
        public static readonly BlockLayer Definition = new();

        private BlockLayer() : base("Block", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchForPaste(in tile);

        /// <summary>At most one <see cref="Blueprint.BlueprintCell"/> (this relative only). No multi-cell frame expansion.</summary>
        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject) =>
            IsBlock(PasteTile.GetTileForIdentify(in tile, identifySubject)) ? [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)] : [];

        /// <summary>Classifies <paramref name="subjectTile"/> (source vs target is chosen by <see cref="PasteSubject"/> in <see cref="Layer.Identify"/>).</summary>
        private static bool IsBlock(Tile subjectTile)
        {
            if (!subjectTile.HasTile) return false;
            if (subjectTile.TileType >= TileLoader.TileCount) return false;
            if (TileID.Sets.Falling[subjectTile.TileType]) return false;
            if (FrameOperations.HasTileAnchor(subjectTile)) return false;
            if (FrameOperations.HasWallAnchor(subjectTile)) return false;
            if (FrameOperations.HasOtherAnchor(subjectTile)) return false;
            return !CustomLayer.TileTypeIsClaimedByAttributedLayer(subjectTile.TileType);
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!IsBlock(tile.PayloadTile)) return true;
            return PlacementValidSingle(in tile, in context);
        }

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!IsBlock(tile.PayloadTile)) return true;
            return HasRequiredItemsSingle(in tile, in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile()) return true;
            if (!context.RequireItems || context.Items is null) return true;
            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.Detail.Style;
            return InventoryManager.FindItemByTile(context.Items, tileType, style) is not null;
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds(intent.TileType, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Clear(tiles, in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (TABLog.IsDebugEnabled && tiles.Count > 0)
            {
                var samplePasteTile = tiles[0];
                TABLog.Debug(
                    $"BlockLayer.Place batch={tiles.Count}"+
                    $"rel=({samplePasteTile.Relative.X},{samplePasteTile.Relative.Y}) "+
                    $"world=({samplePasteTile.World.X},{samplePasteTile.World.Y}) "+
                    $"payload[hasTile={samplePasteTile.PayloadCell.HasTile()} type={samplePasteTile.PayloadCell.Block.Type} "+
                    $"frame=({samplePasteTile.PayloadCell.Block.FrameX},{samplePasteTile.PayloadCell.Block.FrameY})]",
                    "Paste");
            }

            var res = TileOperations.Place(tiles, in context);
            if (!res.Success || res.RemainingTiles.Count > 0 || tiles.Count == 0)
                return res;

            var origin = FrameOperations.GetPlacementOriginTile(tiles);
            if (!context.IsSweepCheat && !Consume(in origin, in context))
                return LayerResult.Fail(origin.World, LayerFailReason.MissingItems);

            return res;
        }

        private static bool PlacementValidSingle(in PasteTile tile, in PasteContext context)
        {
            int x = tile.World.X, y = tile.World.Y;
            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return true;

            ushort tileType = intent.TileType;
            if (tileType >= TileLoader.TileCount) return false;

            int style = tile.PayloadCell.Detail.Style;
            int alternate = tile.PayloadCell.Detail.Alternate;
            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            Tile targetTile = Main.tile[x, y];

            if (targetTile.HasTile && targetTile.TileType == tileType) return true;

            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (targetTile.HasTile && targetTile.TileType != baseType)
                    return false;
                return true;
            }

            if (targetTile.HasTile && !context.IsSweepCheat)
                return false;

            return true;
        }

        private static bool HasRequiredItemsSingle(in PasteTile tile, in PasteContext context)
        {
            int x = tile.World.X, y = tile.World.Y;
            Tile intent = tile.PayloadTile;
            if (!intent.HasTile) return true;

            ushort tileType = intent.TileType;
            if (tileType >= TileLoader.TileCount) return true;

            int style = tile.PayloadCell.Detail.Style;
            int alternate = tile.PayloadCell.Detail.Alternate;
            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(tileType, style);
            Tile targetTile = Main.tile[x, y];

            if (targetTile.HasTile && targetTile.TileType == tileType) return true;

            if (baseTileId.HasValue)
            {
                ushort baseType = (ushort)baseTileId.Value;
                if (!targetTile.HasTile || targetTile.TileType != baseType)
                {
                    if (InventoryManager.FindItemByTile(context.Items!, baseType) is null) return false;
                    return InventoryManager.FindItemByTile(context.Items!, tileType, style) is not null;
                }
            }
            else if (targetTile.HasTile && !context.IsSweepCheat)
            {
                return false;
            }

            return InventoryManager.FindItemByTile(context.Items!, tileType, style) is not null;
        }
    }
}
