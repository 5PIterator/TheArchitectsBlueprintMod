using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class FluidLayer : Layer
    {
        public static readonly FluidLayer Definition = new();

        private static readonly Point[] PoolNeighborOffsets = [new(0, -1), new(1, 0), new(-1, 0), new(0, 1)];

        private FluidLayer() : base("Fluid", LayerType.Liquid) { }

        public override IReadOnlyList<Blueprint.BlueprintCell> Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            var relatives = new List<Point>();
            CollectFluidPool(tile.Relative, identifySubject, in lookup, relatives);
            if (relatives.Count == 0)
                return [];

            var cells = new List<Blueprint.BlueprintCell>(relatives.Count);
            for (int i = 0; i < relatives.Count; i++)
            {
                Point rel = relatives[i];
                PasteTile member = lookup.GetPasteTile(rel);
                cells.Add(new Blueprint.BlueprintCell(rel, member.World, true));
            }

            return cells;
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            tile.Target.LiquidAmount > 0;

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool CanConsume(in PasteTile tile, in PasteContext context) => true;

        public override bool Consume(in PasteTile tile, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.LiquidAmount == tile.Target.LiquidAmount && tile.Source.LiquidType == tile.Target.LiquidType;
            Tile intent = tile.PayloadTile;
            return intent.LiquidAmount == tile.Target.LiquidAmount && intent.LiquidType == tile.Target.LiquidType;
        }

        public override IReadOnlyList<int>? GetItem(in PasteTile tile) => null;

        public override LayerResult Clear(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0)
                return LayerResult.Ok();

            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].World.X, y = tiles[i].World.Y;
                if (Main.tile[x, y].LiquidAmount == 0)
                    continue;

                SuppressTileDrop.EmptyLiquid(x, y);

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, y);
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].World.X, y = tiles[i].World.Y;
                if (Main.tile[x, y].LiquidAmount != 0)
                    return LayerResult.Fail(tiles[i].World, LayerFailReason.InvalidPlacement);
            }

            return LayerResult.Ok(tiles[0].World);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0)
                return LayerResult.Ok();

            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile tile = tiles[i];
                int x = tile.World.X, y = tile.World.Y;
                var src = tile.PayloadCell.Liquid;
                if (!TryWriteLiquid(x, y, src.Type, src.Amount))
                    return LayerResult.Fail(tile.World, LayerFailReason.InvalidPlacement);
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile tile = tiles[i];
                int x = tile.World.X, y = tile.World.Y;
                var src = tile.PayloadCell.Liquid;
                Tile worldTile = Main.tile[x, y];
                if (worldTile.LiquidAmount != src.Amount || worldTile.LiquidType != src.Type)
                    return LayerResult.Fail(tile.World, LayerFailReason.InvalidPlacement);

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.LiquidUpdate, -1, -1, null, x, y);
            }

            return LayerResult.Ok(tiles[0].World);
        }

        /// <summary>
        /// Writes liquid through the live tile handle; raw <see cref="Main.tile.GetData{T}()"/> writes do not update <see cref="Tile.LiquidAmount"/>.
        /// </summary>
        private static bool TryWriteLiquid(int x, int y, int liquidType, byte amount)
        {
            if (!WorldGen.InWorld(x, y))
                return false;

            if (WorldGen.PlaceLiquid(x, y, (byte)liquidType, amount))
                return true;

            Tile dest = Main.tile[x, y];
            ref LiquidData liquid = ref dest.Get<LiquidData>();
            liquid.LiquidType = liquidType;
            liquid.Amount = amount;
            liquid.SkipLiquid = false;
            liquid.CheckingLiquid = false;
            return Main.tile[x, y].LiquidAmount == amount && Main.tile[x, y].LiquidType == liquidType;
        }

        /// <summary>
        /// Flood-fills a fluid pool from <paramref name="startRel"/> within <paramref name="lookup"/>.
        /// Tiles with a block are singleton pools; open fluid connects 4-adjacently by matching liquid type.
        /// </summary>
        private static void CollectFluidPool(Point startRel, PasteSubject identifySubject, in PasteTileLookup lookup, List<Point> outRelatives)
        {
            outRelatives.Clear();
            if (!lookup.TryGetPasteTile(startRel, out PasteTile seed))
                return;

            Tile seedView = PasteTile.GetTileForIdentify(in seed, identifySubject);
            if (seedView.LiquidAmount <= 0)
                return;

            if (seedView.HasTile)
            {
                outRelatives.Add(startRel);
                return;
            }

            int poolType = seedView.LiquidType;
            var visited = new HashSet<Point>();
            var queue = new Queue<Point>();
            queue.Enqueue(startRel);
            visited.Add(startRel);

            while (queue.Count > 0)
            {
                Point cur = queue.Dequeue();
                outRelatives.Add(cur);

                for (int i = 0; i < PoolNeighborOffsets.Length; i++)
                {
                    Point offset = PoolNeighborOffsets[i];
                    Point neighborRel = new(cur.X + offset.X, cur.Y + offset.Y);
                    if (visited.Contains(neighborRel))
                        continue;
                    if (!lookup.TryGetPasteTile(neighborRel, out PasteTile neighbor))
                        continue;

                    Tile neighborView = PasteTile.GetTileForIdentify(in neighbor, identifySubject);
                    if (neighborView.LiquidAmount <= 0 || neighborView.HasTile || neighborView.LiquidType != poolType)
                        continue;

                    visited.Add(neighborRel);
                    queue.Enqueue(neighborRel);
                }
            }
        }
    }
}
