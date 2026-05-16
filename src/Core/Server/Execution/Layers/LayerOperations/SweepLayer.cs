using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class SweepLayer : Layer
    {
        public static readonly SweepLayer Definition = new();

        private SweepLayer() : base("Sweep", LayerType.Sweep) { }

        /// <summary>
        /// Full paste footprint from the lookup; ignores the input tile content.
        /// </summary>
        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject _)
        {
            if (lookup.PasteTiles.Length == 0) return [];

            var bounded = new List<Blueprint.BlueprintCell>(lookup.PasteTiles.Length);
            for (int y = 0; y < lookup.PasteTiles.GetLength(1); y++)
            {
                for (int x = 0; x < lookup.PasteTiles.GetLength(0); x++)
                {
                    var pt = lookup.PasteTiles[x, y];
                    bounded.Add(new Blueprint.BlueprintCell(pt.Relative, pt.World, true));
                }
            }
            return bounded;
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => true;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            Tile intent = TileComparison.IsSourceCutIntent(in tile) ? tile.Source : tile.PayloadTile;
            if (intent.HasTile != tile.Target.HasTile) return false;
            if (intent.WallType != tile.Target.WallType) return false;
            if (intent.LiquidAmount != tile.Target.LiquidAmount || intent.LiquidType != tile.Target.LiquidType) return false;
            if (intent.RedWire != tile.Target.RedWire || intent.BlueWire != tile.Target.BlueWire
                || intent.GreenWire != tile.Target.GreenWire || intent.YellowWire != tile.Target.YellowWire)
                return false;
            if (intent.HasActuator != tile.Target.HasActuator || intent.IsActuated != tile.Target.IsActuated)
                return false;
            if (intent.TileColor != tile.Target.TileColor || intent.WallColor != tile.Target.WallColor)
                return false;
            if (intent.BlockType != tile.Target.BlockType)
                return false;
            if (!intent.HasTile)
                return true;

            return intent.TileType == tile.Target.TileType
                && intent.TileFrameX == tile.Target.TileFrameX
                && intent.TileFrameY == tile.Target.TileFrameY;
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
