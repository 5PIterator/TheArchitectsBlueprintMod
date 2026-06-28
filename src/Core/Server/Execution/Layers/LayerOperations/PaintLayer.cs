using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class PaintLayer : Layer
    {
        public static readonly PaintLayer Definition = new();

        private PaintLayer() : base("Paint", LayerType.Paint) { }

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup _, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (subjectTile.TileColor == PaintID.None && subjectTile.WallColor == PaintID.None)
                return [];

            return [new Blueprint.BlueprintCell(tile.Relative, tile.World, true)];
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.TileColor != PaintID.None || t.WallColor != PaintID.None;
        }

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.TileColor == tile.Target.TileColor && tile.Source.WallColor == tile.Target.WallColor;
            Tile intent = tile.PayloadTile;
            return intent.TileColor == tile.Target.TileColor && intent.WallColor == tile.Target.WallColor;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            Tile live = Main.tile[x, y];

            bool changed = false;
            if (live.HasTile && live.TileColor != PaintID.None && WorldGen.paintTile(x, y, PaintID.None))
                changed = true;
            if (live.WallType != WallID.None && live.WallColor != PaintID.None && WorldGen.paintWall(x, y, PaintID.None))
                changed = true;

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            var src = tile.PayloadCell.Paint;
            int wx = tile.World.X, wy = tile.World.Y;
            Tile live = Main.tile[wx, wy];

            bool changed = false;
            if (live.HasTile && live.TileColor != src.TileColor && WorldGen.paintTile(wx, wy, src.TileColor))
                changed = true;
            if (live.WallType != WallID.None && live.WallColor != src.WallColor && WorldGen.paintWall(wx, wy, src.WallColor))
                changed = true;

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }
    }
}
