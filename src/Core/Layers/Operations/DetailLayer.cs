using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using Terraria;
using Terraria.Enums;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class DetailLayer : Layer
    {
        public static readonly DetailLayer Definition = new();

        private DetailLayer() : base("Detail", LayerType.Detail) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            bool hasDetail = subjectTile.IsActuated || subjectTile.BlockType != BlockType.Solid;
            if (!hasDetail)
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatDetailState(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.IsActuated || t.BlockType != BlockType.Solid;
        }

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.IsActuated == tile.Target.IsActuated && tile.Source.BlockType == tile.Target.BlockType;
            Tile intent = tile.PayloadTile;
            return intent.IsActuated == tile.Target.IsActuated && intent.BlockType == tile.Target.BlockType;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int wx = tile.World.X, wy = tile.World.Y;
            Tile live = Main.tile[wx, wy];
            string message = LayerLogFormat.FormatDetailState(live);

            bool alreadyCleared = !live.IsActuated
                && !live.IsHalfBlock
                && live.Slope == SlopeType.Solid
                && live.BlockType == BlockType.Solid;
            if (alreadyCleared)
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyDetailState(
                    wx,
                    wy,
                    isActuated: false,
                    isHalfBlock: false,
                    slope: SlopeType.Solid,
                    blockType: BlockType.Solid,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.ClearBlocked, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.ClearedLabel);
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            var src = tile.PayloadCell.Detail;
            int wx = tile.World.X, wy = tile.World.Y;
            if (!Main.tile[wx, wy].HasTile)
                return LayerResult.Ok();

            string message = LayerLogFormat.FormatDetailState(in src);

            Tile live = Main.tile[wx, wy];
            bool alreadyMatching = src.IsHalfBlock == live.IsHalfBlock
                && src.Slope == live.Slope
                && src.IsActuated == live.IsActuated
                && src.BlockType == live.BlockType;
            if (alreadyMatching)
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyDetailState(
                    wx,
                    wy,
                    isActuated: src.IsActuated,
                    isHalfBlock: src.IsHalfBlock,
                    slope: src.Slope,
                    blockType: src.BlockType,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.DetailedLabel);
        }
    }
}
