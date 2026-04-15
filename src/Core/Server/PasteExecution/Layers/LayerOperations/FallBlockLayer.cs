using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
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

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup lookup)
        {
            PasteSubject classify = tile.SubjectType;
            Tile subjectTile = tile.IdentifyFace;
            if (!subjectTile.HasTile ||
                subjectTile.TileType >= TileLoader.TileCount ||
                !TileID.Sets.Falling[subjectTile.TileType])
                return [];

            var list = new List<Blueprint.BoundedTile>();
            for (int dy = 0; ; dy--)
            {
                var p = new Microsoft.Xna.Framework.Point(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(p, out PasteTile lookupTile))
                    break;
                Tile t = PasteTile.Face(classify, in lookupTile);
                if (!t.HasTile ||
                    t.TileType >= TileLoader.TileCount ||
                    !TileID.Sets.Falling[t.TileType])
                    break;
                list.Insert(0, new Blueprint.BoundedTile(p, selected: true));
            }
            for (int dy = 1; ; dy++)
            {
                var p = new Microsoft.Xna.Framework.Point(tile.Relative.X, tile.Relative.Y + dy);
                if (!lookup.TryGetPasteTile(p, out PasteTile lookupTile))
                    break;
                Tile t = PasteTile.Face(classify, in lookupTile);
                if (!t.HasTile ||
                    t.TileType >= TileLoader.TileCount ||
                    !TileID.Sets.Falling[t.TileType])
                    break;
                list.Add(new Blueprint.BoundedTile(p, selected: true));
            }
            return list.Count > 0 ? list : [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            if (!context.NeedsItems || context.Slots is null || context.CheatSweep) return true;
            if (!tile.Payload.HasTile) return true;
            int style = TileObjectData.GetTileStyle(tile.Payload);
            return context.Slots.FindTileSlot(tile.Payload.TileType, style) >= 0;
        }

        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            if (!tile.Payload.HasTile || !context.NeedsItems || context.Slots is null) return true;
            int style = TileObjectData.GetTileStyle(tile.Payload);
            return context.Slots.FindTileSlot(tile.Payload.TileType, style) >= 0;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            Tile blueprint = tile.Payload;
            if (!blueprint.HasTile) return null;
            int style = TileObjectData.GetTileStyle(blueprint);
            return InteractionMapProvider.GetTileItemIds(blueprint.TileType, style);
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
            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
                if (i != lowestIdx) remaining.Add(tiles[i]);
            return remaining.Count > 0 ? LayerResult.Ok(tile.World, remaining) : LayerResult.Ok(tile.World);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);
    }
}
