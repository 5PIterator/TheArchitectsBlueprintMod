using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.DataStructures;
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

            bool changed = false;
            if (live.HasTile && live.TileColor != PaintID.None && WorldGen.paintTile(x, y, PaintID.None))
                changed = true;
            if (live.WallType != WallID.None && live.WallColor != PaintID.None && WorldGen.paintWall(x, y, PaintID.None))
                changed = true;
            if (live.HasTile && ClearBlockCoating(x, y))
                changed = true;
            if (live.WallType != WallID.None && ClearWallCoating(x, y))
                changed = true;

            return changed ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
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

            bool changed = false;
            if (live.HasTile && live.TileColor != src.TileColor && WorldGen.paintTile(wx, wy, src.TileColor))
                changed = true;
            if (live.WallType != WallID.None && live.WallColor != src.WallColor && WorldGen.paintWall(wx, wy, src.WallColor))
                changed = true;
            if (live.HasTile && ApplyBlockCoatingIfNeeded(wx, wy, in src))
                changed = true;
            if (live.WallType != WallID.None && ApplyWallCoatingIfNeeded(wx, wy, in src))
                changed = true;

            return changed ? LayerResult.Ok(message, LayerLogFormat.PaintedLabel) : LayerResult.Ok();
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

        private static bool ClearBlockCoating(int x, int y)
        {
            Tile tile = Main.tile[x, y];
            TileColorCache cache = tile.BlockColorAndCoating();
            if (!cache.Invisible && !cache.FullBright)
                return false;

            cache.Invisible = false;
            cache.FullBright = false;
            cache.ApplyToBlock(Main.tile[x, y]);
            return true;
        }

        private static bool ClearWallCoating(int x, int y)
        {
            Tile tile = Main.tile[x, y];
            TileColorCache cache = tile.WallColorAndCoating();
            if (!cache.Invisible && !cache.FullBright)
                return false;

            cache.Invisible = false;
            cache.FullBright = false;
            cache.ApplyToWall(Main.tile[x, y]);
            return true;
        }

        private static bool ApplyBlockCoatingIfNeeded(int x, int y, in PayloadCell.PaintData src)
        {
            Tile tile = Main.tile[x, y];
            TileColorCache cache = tile.BlockColorAndCoating();
            if (cache.Invisible == src.TileInvisible && cache.FullBright == src.TileFullbright)
                return false;

            cache.Invisible = src.TileInvisible;
            cache.FullBright = src.TileFullbright;
            cache.ApplyToBlock(Main.tile[x, y]);
            return true;
        }

        private static bool ApplyWallCoatingIfNeeded(int x, int y, in PayloadCell.PaintData src)
        {
            Tile tile = Main.tile[x, y];
            TileColorCache cache = tile.WallColorAndCoating();
            if (cache.Invisible == src.WallInvisible && cache.FullBright == src.WallFullbright)
                return false;

            cache.Invisible = src.WallInvisible;
            cache.FullBright = src.WallFullbright;
            cache.ApplyToWall(Main.tile[x, y]);
            return true;
        }
    }
}
