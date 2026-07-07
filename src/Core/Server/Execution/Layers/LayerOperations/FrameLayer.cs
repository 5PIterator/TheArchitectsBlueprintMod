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
    internal sealed class FrameLayer : Layer
    {
        public static readonly FrameLayer Definition = new();

        private FrameLayer() : base("Frame", LayerType.Frame) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!IsFrameTile(subjectTile))
                return LayerResult.IdentifyNone();

            string message = LayerLogFormat.FormatTile(subjectTile);
            IReadOnlyList<Blueprint.BlueprintCell>? tiles = FrameOperations.GetFrameTilesAsBounded(tile.Relative, subjectTile, lookup);
            if (tiles is { Count: > 0 })
                return LayerResult.Identified(TileBatch.FromCells(tiles), message);

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                message);
        }

        /// <summary>Returns true for tile-anchored or multi-cell objects that must use frame placement.</summary>
        internal static bool IsFrameTile(Tile tile)
        {
            // --- Reject air, mod tiles, and falling blocks; then classify anchors and multi-cell footprints.
            if (!tile.HasTile || !TileLoaderSupport.IsKnownTileType(tile.TileType))
                return false;

            if (TileID.Sets.Falling[tile.TileType])
                return false;

            if (HasFrameAnchor(tile))
                return true;

            int style = 0, alternate = 0;
            TileObjectData.GetTileInfo(tile, ref style, ref alternate);
            if (HasFrameAnchor(tile.TileType, style))
                return true;

            TileObjectData? data = TileObjectData.GetTileData(tile.TileType, style);
            return data is not null && (data.Width > 1 || data.Height > 1);
        }

        /// <summary>Type-only frame check for <see cref="CustomLayer"/> tile-type exclusion lists.</summary>
        internal static bool IsFrameTileType(ushort tileType)
        {
            if (!TileLoaderSupport.IsKnownTileType(tileType) || TileID.Sets.Falling[tileType])
                return false;

            if (HasFrameAnchor(tileType, 0))
                return true;

            TileObjectData? data = TileObjectData.GetTileData(tileType, 0);
            return data is not null && (data.Width > 1 || data.Height > 1);
        }

        private static bool HasFrameAnchor(Tile tile) =>
            FrameOperations.HasTileAnchor(tile)
            || FrameOperations.HasWallAnchor(tile)
            || FrameOperations.HasOtherAnchor(tile);

        private static bool HasFrameAnchor(ushort tileType, int style) =>
            FrameOperations.HasTileAnchor(tileType, style)
            || FrameOperations.HasWallAnchor(tileType, style)
            || FrameOperations.HasOtherAnchor(tileType, style);
        private static bool UsesBaseTilePlacement(ushort tileType, int style) =>
            InteractionMapProvider.GetRequiredBaseTile(tileType, style).HasValue;

        private static bool UsesTilePlacement(IReadOnlyList<PasteTile> tiles, in PasteTile origin)
        {
            if (!origin.PayloadCell.HasTile())
                return false;

            ushort tileType = origin.PayloadCell.Block.Type;
            if (!TileLoaderSupport.IsKnownTileType(tileType))
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
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.Detail.Style;
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
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

        public override LayerResult Clear(TileBatch batch, in PasteContext context) =>
            FrameOperations.Clear(batch.Tiles, in context);

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            var origin = tiles.Count > 0 ? tiles[0] : default;
            string message = origin.PayloadCell.HasTile()
                ? LayerLogFormat.FormatTile(origin.PayloadTile)
                : LayerLogFormat.FormatFromSubject(in origin, PasteSubject.Payload);
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
                    return LayerResult.Fail(
                        LayerFailReason.MissingItems,
                        LayerLogFormat.FormatTile(placementOrigin.PayloadTile));

                return res;
            }

            if (res.FailReason == LayerFailReason.InvalidPlacement && tiles.Count > 0)
            {
                if (!context.RetryOnInvalidPlacement)
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
                return LayerResult.OkWithRemaining(batch, message, LayerLogFormat.PlacedLabel(in context));
            }

            return res;
        }
    }
}
