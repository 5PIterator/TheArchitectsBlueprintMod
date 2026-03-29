using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class PaintLayer : Layer
    {
        public static readonly PaintLayer Definition = new();

        private PaintLayer() : base("Paint", LayerType.Paint) { }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup)
        {
            if (sourceTile.TileColor == PaintID.None && sourceTile.WallColor == PaintID.None)
                return NoIdentify();

            return [new Blueprint.BoundedTile(rel, selected: true)];
        }

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override bool Match(in PasteTile tile) =>
            tile.Source.TileColor == tile.Target.TileColor
            && tile.Source.WallColor == tile.Target.WallColor;

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            ref var dest = ref ModifierOperations.GetState(tile.X, tile.Y);

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
                LayerSync.SyncTileSquare(tile.X, tile.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();
            var tile = tiles[0];
            Tile src = tile.Source;
            ref var dest = ref ModifierOperations.GetState(tile.X, tile.Y);

            bool changed = false;
            if (Main.tile[tile.X, tile.Y].HasTile && dest.TileColor != src.TileColor)
            {
                dest.TileColor = src.TileColor;
                changed = true;
            }
            if (Main.tile[tile.X, tile.Y].WallType != WallID.None && dest.WallColor != src.WallColor)
            {
                dest.WallColor = src.WallColor;
                changed = true;
            }

            if (changed)
                LayerSync.SyncTileSquare(tile.X, tile.Y);

            return changed ? LayerResult.Ok(tile.World) : LayerResult.Ok();
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;
    }
}
