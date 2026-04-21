using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class PaintLayer : Layer
    {
        public static readonly PaintLayer Definition = new();

        private PaintLayer() : base("Paint", LayerType.Paint) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup _)
        {
            Tile subjectTile = tile.Subject;
            if (subjectTile.TileColor == PaintID.None && subjectTile.WallColor == PaintID.None)
                return [];

            return [new Blueprint.BoundedTile(tile.Relative, selected: true)];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.TileColor != PaintID.None || t.WallColor != PaintID.None;
        }

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile) =>
            tile.Source.TileColor == tile.Target.TileColor
            && tile.Source.WallColor == tile.Target.WallColor;

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            ref var dest = ref DetailOperations.GetState(tile.World.X, tile.World.Y);

            bool changed = false;
            if (dest.TileColor != PaintID.None)
            {
                dest.TileColor = PaintID.None;
                changed = true;
            }
            if (dest.WallColor != PaintID.None)
            {
                dest.WallColor = PaintID.None;
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
            var src = tile.PayloadCell.Paint;
            int wx = tile.World.X, wy = tile.World.Y;
            ref var dest = ref DetailOperations.GetState(wx, wy);

            bool changed = false;
            if (Main.tile[wx, wy].HasTile && dest.TileColor != src.TileColor)
            {
                dest.TileColor = src.TileColor;
                changed = true;
            }
            if (Main.tile[wx, wy].WallType != WallID.None && dest.WallColor != src.WallColor)
            {
                dest.WallColor = src.WallColor;
                changed = true;
            }

            if (changed)
                LayerSync.SyncTileSquare(wx, wy);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }
    }
}
