using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class PaintLayer : Layer
    {
        public static readonly PaintLayer Definition = new();

        private PaintLayer() : base("Paint", LayerType.Paint) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!HasPaintOrCoating(subjectTile))
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatPaintState(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            HasPaintOrCoating(tile.Target);

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return PaintStateMatches(tile.Source, tile.Target);
            return PaintStateMatches(tile.PayloadTile, tile.Target);
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            Tile live = Main.tile[x, y];
            string message = LayerLogFormat.FormatPaintState(live);

            if (!HasPaintOrCoating(live))
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyPaintState(
                    x,
                    y,
                    PaintID.None,
                    PaintID.None,
                    tileInvisible: false,
                    wallInvisible: false,
                    tileFullbright: false,
                    wallFullbright: false,
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
            PayloadCell.PaintData src = tile.PayloadCell.Paint;
            int wx = tile.World.X, wy = tile.World.Y;
            Tile live = Main.tile[wx, wy];
            string message = LayerLogFormat.FormatPaintState(tile.PayloadCell);

            bool matching = PaintStateMatches(tile.PayloadTile, live);
            if (matching)
                return LayerResult.Ok();

            if (!TileExecutor.EnqueueApplyPaintState(
                    wx,
                    wy,
                    src.TileColor,
                    src.WallColor,
                    src.TileInvisible,
                    src.WallInvisible,
                    src.TileFullbright,
                    src.WallFullbright,
                    in context))
            {
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            return LayerResult.Ok(message, LayerLogFormat.PaintedLabel);
        }

        private static bool HasPaintOrCoating(Tile tile) =>
            tile.TileColor != PaintID.None
            || tile.WallColor != PaintID.None
            || tile.IsTileInvisible
            || tile.IsWallInvisible
            || tile.IsTileFullbright
            || tile.IsWallFullbright;

        private static bool PaintStateMatches(Tile intent, Tile target) =>
            intent.TileColor == target.TileColor
            && intent.WallColor == target.WallColor
            && intent.IsTileInvisible == target.IsTileInvisible
            && intent.IsWallInvisible == target.IsWallInvisible
            && intent.IsTileFullbright == target.IsTileFullbright
            && intent.IsWallFullbright == target.IsWallFullbright;
    }
}
