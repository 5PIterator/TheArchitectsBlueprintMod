using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Layers
{
    internal sealed class SweepLayer : Layer
    {
        public static readonly SweepLayer Definition = new();

        private SweepLayer() : base("Sweep", LayerType.Sweep) { }

        /// <summary>
        /// Full paste footprint from the lookup; ignores the input tile content.
        /// </summary>
        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup lookup)
        {
            if (lookup.PasteTiles.Count == 0) return [];

            var bounded = new List<Blueprint.BoundedTile>(lookup.PasteTiles.Count);
            for (int i = 0; i < lookup.PasteTiles.Count; i++)
                bounded.Add(new Blueprint.BoundedTile(lookup.PasteTiles[i].Relative, selected: true));
            return bounded;
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => true;

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

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

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].World.X, y = tiles[i].World.Y;
                if (!WorldGen.InWorld(x, y))
                    continue;

                Main.tile[x, y].ClearTile();

                LayerSync.SyncTileSquare(x, y);
            }

            return LayerResult.Ok();
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                var pt = tiles[i];
                int wx = pt.World.X, wy = pt.World.Y;
                Main.tile[wx, wy].CopyFrom(pt.PayloadCell.ToTile());
                LayerSync.SyncTileSquare(wx, wy);
            }

            return LayerResult.Ok();
        }
    }
}
