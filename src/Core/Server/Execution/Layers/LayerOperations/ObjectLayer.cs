using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Tile-anchored frames and multitiles (doors, furniture, etc.).
    /// </summary>
    internal sealed class ObjectLayer : Layer
    {
        public static readonly ObjectLayer Definition = new();

        private ObjectLayer() : base("Object", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsFrameObjectTile(subjectTile))
                return [];

            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            if (tiles is { Count: > 0 })
                return tiles;

            return [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)];
        }

        internal static bool IsFrameObjectTile(Tile tile)
        {
            if (!tile.HasTile || tile.TileType >= TileLoader.TileCount)
                return false;

            if (TileID.Sets.Falling[tile.TileType])
                return false;

            if (VegetationLayer.IsVegetationTile(tile))
                return false;

            return FrameOperations.HasTileAnchor(tile)
                || FrameOperations.HasWallAnchor(tile)
                || FrameOperations.HasOtherAnchor(tile);
        }

        /// <summary>Used by <see cref="BlockLayer"/> to avoid claiming framed objects.</summary>
        internal static bool IsObjectTile(Tile tile) => IsFrameObjectTile(tile);

        private static bool UsesBaseTilePlacement(ushort tileType, int style) =>
            InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue;

        private static bool UsesTilePlacement(IReadOnlyList<PasteTile> tiles, in PasteTile origin)
        {
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (tileType >= TileLoader.TileCount)
                return false;

            int style = origin.PayloadCell.Detail.Style;
            if (UsesBaseTilePlacement(tileType, style) || TileID.Sets.Grass[tileType])
                return true;

            if (tiles.Count != 1)
                return false;

            var data = TileObjectData.GetTileData(tileType, style);
            return data is null || (data.Width <= 1 && data.Height <= 1);
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.Detail.Style;
            return InventoryManager.FindItemByTile(context.Items, (ushort)tile.PayloadCell.Block.Type, style) is not null;
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.Detail.Style;
            return InventoryManager.FindItemByTile(context.Items, (ushort)tile.PayloadCell.Block.Type, style) is not null;
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile())
                return true;

            ushort tileType = tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.Detail.Style;
            return UsesBaseTilePlacement(tileType, style) || TileID.Sets.Grass[tileType]
                ? TileOperations.Consume(in tile, in context)
                : FrameOperations.Consume(in tile, in context);
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context) =>
            FrameOperations.Clear(tiles, in context);

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            var origin = tiles.Count > 0 ? tiles[0] : default;
            bool useTilePlace = UsesTilePlacement(tiles, in origin);
            var res = useTilePlace
                ? TileOperations.Place(tiles, in context)
                : FrameOperations.Place(tiles, in context);
            if (res.Success)
            {
                if (res.RemainingTiles.Count > 0 || tiles.Count == 0)
                    return res;

                var placementOrigin = useTilePlace ? origin : FrameOperations.GetPlacementOriginTile(tiles);
                if (!context.IsSweepCheat && !Consume(in placementOrigin, in context))
                    return LayerResult.Fail(placementOrigin.World, LayerFailReason.MissingItems);

                return res;
            }

            if (res.FailReason == LayerFailReason.InvalidPlacement && tiles.Count > 0)
            {
                if (!context.RetryOnInvalidPlacement)
                    return LayerResult.Fail(origin.World, LayerFailReason.InvalidPlacement);
                return LayerResult.OkWithRemaining(tiles);
            }

            return res;
        }
    }
}
