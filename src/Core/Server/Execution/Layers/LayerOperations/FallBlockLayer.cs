using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class FallBlockLayer : Layer
    {
        public static readonly FallBlockLayer Definition = new();

        private FallBlockLayer() : base("FallBlock", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchForPaste(in tile);

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!subjectTile.HasTile ||
                subjectTile.TileType >= TileLoader.TileCount ||
                !TileID.Sets.Falling[subjectTile.TileType])
                return [];

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
                    t.TileType >= TileLoader.TileCount ||
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
                    t.TileType >= TileLoader.TileCount ||
                    !TileID.Sets.Falling[t.TileType])
                    break;
                list.Add(new Blueprint.BlueprintCell(p, CellWorld(in tile, p, in lookupTile, sourceCut), true));
            }
            return list.Count > 0 ? list : [];
        }

        private static Point CellWorld(in PasteTile anchor, Point relative, in PasteTile lookupTile, bool sourceCut) =>
            sourceCut
                ? new Point(anchor.World.X + relative.X - anchor.Relative.X, anchor.World.Y + relative.Y - anchor.Relative.Y)
                : lookupTile.World;

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

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
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
            return remaining.Count > 0 ? LayerResult.Ok(tile.World, remaining) : LayerResult.Ok(tile.World);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            int lowestIdx = 0;
            for (int i = 1; i < tiles.Count; i++)
                if (tiles[i].Relative.Y > tiles[lowestIdx].Relative.Y) lowestIdx = i;
            var tile = tiles[lowestIdx];
            var res = TileOperations.Place([tile], in context);
            if (!res.Success) return res;
            if (!context.IsSweepCheat && !Consume(in tile, in context))
                return LayerResult.Fail(tile.World, LayerFailReason.MissingItems);
            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
                if (i != lowestIdx) remaining.Add(tiles[i]);
            return remaining.Count > 0 ? LayerResult.Ok(tile.World, remaining) : LayerResult.Ok(tile.World);
        }
    }
}
