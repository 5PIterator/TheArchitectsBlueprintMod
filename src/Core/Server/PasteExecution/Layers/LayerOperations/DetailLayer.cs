using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class DetailLayer : Layer
    {
        public static readonly DetailLayer Definition = new();

        private DetailLayer() : base("Detail", LayerType.Detail) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup _)
        {
            Tile subjectTile = tile.IdentifyFace;
            bool hasDetail = subjectTile.IsActuated || subjectTile.BlockType != Terraria.ID.BlockType.Solid;
            return hasDetail ? [new Blueprint.BoundedTile(tile.Relative, selected: true)] : [];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.IsActuated || t.BlockType != Terraria.ID.BlockType.Solid;
        }

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile) =>
            tile.Source.IsActuated == tile.Target.IsActuated
            && tile.Source.BlockType == tile.Target.BlockType;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            ref var dest = ref ModifierOperations.GetState(tile.World.X, tile.World.Y);

            bool changed = false;
            if (dest.IsActuated)
            {
                dest.IsActuated = false;
                changed = true;
            }
            if (dest.BlockType != Terraria.ID.BlockType.Solid)
            {
                dest.BlockType = Terraria.ID.BlockType.Solid;
                changed = true;
            }

            if (changed)
                LayerSync.SyncTileSquare(tile.World.X, tile.World.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            Tile src = tile.Payload;
            int wx = tile.World.X, wy = tile.World.Y;
            ref var dest = ref ModifierOperations.GetState(wx, wy);

            bool changed = false;
            if (dest.IsActuated != src.IsActuated)
            {
                dest.IsActuated = src.IsActuated;
                changed = true;
            }
            if (Main.tile[wx, wy].HasTile && dest.BlockType != src.BlockType)
            {
                dest.BlockType = src.BlockType;
                changed = true;
            }

            if (changed)
                LayerSync.SyncTileSquare(wx, wy);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;
    }
}
