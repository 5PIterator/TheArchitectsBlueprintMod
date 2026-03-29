using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class SweepLayer : Layer
    {
        public static readonly SweepLayer Definition = new();

        private SweepLayer() : base("Sweep", LayerType.Sweep) { }

        /// <summary>
        /// Full paste footprint from the lookup; ignores <paramref name="rel"/> and <paramref name="sourceTile"/> content.
        /// </summary>
        public override IReadOnlyList<Blueprint.BoundedTile> Identify(Point rel, in Tile sourceTile, in PasteLookupQuery lookup) =>
            lookup.SelectedTiles.Count > 0 ? lookup.SelectedTiles : NoIdentify();

        public override bool CanPlace(IReadOnlyList<Blueprint.BoundedTile> identifiedTiles, in PasteLookupQuery lookup, in PasteContext context) =>
            LayerPlacementPlan.TryReserveForPlacement(this, identifiedTiles, in lookup, in context, reserveEachTileInStack: false);

        public override bool Match(in PasteTile tile)
        {
            if (tile.Source.HasTile != tile.Target.HasTile) return false;
            if (tile.Source.WallType != tile.Target.WallType) return false;
            if (tile.Source.LiquidAmount != tile.Target.LiquidAmount || tile.Source.LiquidType != tile.Target.LiquidType) return false;
            if (tile.Source.RedWire != tile.Target.RedWire || tile.Source.BlueWire != tile.Target.BlueWire
                || tile.Source.GreenWire != tile.Target.GreenWire || tile.Source.YellowWire != tile.Target.YellowWire)
                return false;
            if (tile.Source.HasActuator != tile.Target.HasActuator || tile.Source.IsActuated != tile.Target.IsActuated)
                return false;
            if (tile.Source.TileColor != tile.Target.TileColor || tile.Source.WallColor != tile.Target.WallColor)
                return false;
            if (tile.Source.BlockType != tile.Target.BlockType)
                return false;
            if (!tile.Source.HasTile)
                return true;

            return tile.Source.TileType == tile.Target.TileType
                && tile.Source.TileFrameX == tile.Target.TileFrameX
                && tile.Source.TileFrameY == tile.Target.TileFrameY;
        }

        public override IReadOnlyList<int>? GetItem(in Tile sourceTile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].X, y = tiles[i].Y;
                if (!WorldGen.InWorld(x, y))
                    continue;

                Main.tile[x, y].ClearEverything();

                LayerSync.SyncTileSquare(x, y);
            }

            return LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                var pt = tiles[i];

                Main.tile[pt.X, pt.Y].CopyFrom(pt.Source);
                LayerSync.SyncTileSquare(pt.X, pt.Y);
            }

            return LayerResult.Ok();
        }

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;
    }
}
