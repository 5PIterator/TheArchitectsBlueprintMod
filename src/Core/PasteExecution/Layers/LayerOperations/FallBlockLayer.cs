using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class FallBlockLayer : Layer
    {
        public static readonly FallBlockLayer Definition = new();

        private FallBlockLayer() : base("FallBlock", LayerType.Block) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            if (!sourceTile.HasTile ||
                sourceTile.TileType >= TileLoader.TileCount ||
                !TileID.Sets.Falling[sourceTile.TileType])
                return NoIdentify();

            var list = new List<Blueprint.BoundedTile>();
            for (int dy = 0; ; dy--)
            {
                var p = new Point(rel.X, rel.Y + dy);
                if (!lookup.TryGetIdentify(p, out Tile tile) || !tile.HasTile || tile.TileType >= TileLoader.TileCount || !TileID.Sets.Falling[tile.TileType])
                    break;
                list.Insert(0, new Blueprint.BoundedTile(p, selected: true));
            }
            for (int dy = 1; ; dy++)
            {
                var p = new Point(rel.X, rel.Y + dy);
                if (!lookup.TryGetIdentify(p, out Tile tile) || !tile.HasTile || tile.TileType >= TileLoader.TileCount || !TileID.Sets.Falling[tile.TileType])
                    break;
                list.Add(new Blueprint.BoundedTile(p, selected: true));
            }
            return list.Count > 0 ? list : NoIdentify();
        }

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: true);

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile)
        {
            if (!sourceTile.HasTile) return null;
            int style = TileObjectData.GetTileStyle(sourceTile);
            return InteractionMapProvider.GetTileItemIds(sourceTile.TileType, style);
        }

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            int highestIdx = 0;
            for (int i = 1; i < tiles.Count; i++)
                if (tiles[i].Y < tiles[highestIdx].Y) highestIdx = i;
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
                if (tiles[i].Y > tiles[lowestIdx].Y) lowestIdx = i;
            var tile = tiles[lowestIdx];
            var res = TileOperations.Place([tile], in context);
            if (!res.Success) return res;
            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
                if (i != lowestIdx) remaining.Add(tiles[i]);
            return remaining.Count > 0 ? LayerResult.Ok(tile.World, remaining) : LayerResult.Ok(tile.World);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);
    }
}
