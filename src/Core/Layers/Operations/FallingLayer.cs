using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.UI.Preview;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class FallingLayer : Layer
    {
        public static readonly FallingLayer Definition = new();

        private FallingLayer() : base("Falling", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchForPaste(in tile);

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!subjectTile.HasTile ||
                !TileLoaderSupport.IsKnownTileType(subjectTile.TileType) ||
                TileLoaderSupport.IsUnknownTileType(subjectTile.TileType) ||
                !TileID.Sets.Falling[subjectTile.TileType])
                return LayerResult.IdentifyNone();

            // Cut-at-source: anchor Target is live capture; lookup.Target is the offset paste destination (often empty).
            // Walk the column using lookup.Source (capture-area snapshot) and source-area world coords.
            bool sourceCut = TileComparison.IsSourceCutIntent(in tile);
            PasteSubject columnSubject = sourceCut ? PasteSubject.Source : identifySubject;

            var list = new List<Blueprint.BlueprintCell>();
            for (int dy = 0; ; dy--)
            {
                var p = new Point(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(p, out PasteTile lookupTile))
                    break;
                Tile t = PasteTile.GetTileForIdentify(in lookupTile, columnSubject);
                if (!t.HasTile ||
                    !TileLoaderSupport.IsKnownTileType(t.TileType) ||
                    !TileID.Sets.Falling[t.TileType])
                    break;
                list.Insert(0, new Blueprint.BlueprintCell(p, CellWorld(in tile, p, in lookupTile, sourceCut), true));
            }
            for (int dy = 1; ; dy++)
            {
                var p = new Point(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(p, out PasteTile lookupTile))
                    break;
                Tile t = PasteTile.GetTileForIdentify(in lookupTile, columnSubject);
                if (!t.HasTile ||
                    !TileLoaderSupport.IsKnownTileType(t.TileType) ||
                    !TileID.Sets.Falling[t.TileType])
                    break;
                list.Add(new Blueprint.BlueprintCell(p, CellWorld(in tile, p, in lookupTile, sourceCut), true));
            }

            return list.Count > 0
                ? LayerResult.Identified(TileBatch.FromCells(list), LayerLogFormat.FormatTile(subjectTile))
                : LayerResult.IdentifyNone();
        }

        private static Point CellWorld(in PasteTile anchor, Point relative, in PasteTile lookupTile, bool sourceCut) =>
            sourceCut
                ? new Point(anchor.World.X + relative.X - anchor.Relative.X, anchor.World.Y + relative.Y - anchor.Relative.Y)
                : lookupTile.World;

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0) return LayerResult.Ok();
            int highestIdx = 0;
            for (int i = 1; i < tiles.Count; i++)
                if (tiles[i].Relative.Y < tiles[highestIdx].Relative.Y) highestIdx = i;
            var tile = tiles[highestIdx];
            var res = TileOperations.Clear(tile, in context);
            if (!res.Success) return res;
            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
                if (i != highestIdx) remaining.Add(tiles[i]);
            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), res.Message, res.OkLabel)
                : res;
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0) return LayerResult.Ok();
            int lowestIdx = 0;
            for (int i = 1; i < tiles.Count; i++)
                if (tiles[i].Relative.Y > tiles[lowestIdx].Relative.Y) lowestIdx = i;
            var tile = tiles[lowestIdx];
            var res = TileOperations.Place([tile], in context);
            if (!res.Success) return res;
            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
                if (i != lowestIdx) remaining.Add(tiles[i]);
            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), res.Message, res.OkLabel)
                : res;
        }

        public override bool DrawsCells => true;

        public override void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            if (LayerPreviewSprites.HasDetailModifiedSprite(cell))
                return;

            Tile tile = cell.ToTile();
            if (!tile.HasTile || !TileLoaderSupport.IsKnownTileType(tile.TileType) || !TileID.Sets.Falling[tile.TileType])
                return;

            if (!LayerPreviewSprites.TryResolveSprite(in ctx, cell, relX, relY, out PreviewTileDraw.DrawSprite sprite))
                return;

            LayerPreviewSprites.DrawSprite(in ctx, relX, relY, in sprite);
        }
    }
}
