using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    internal sealed class FluidLayer : Layer
    {
        public static readonly FluidLayer Definition = new();

        private static readonly Point[] PoolNeighborOffsets = [new(0, -1), new(1, 0), new(-1, 0), new(0, 1)];

        private FluidLayer() : base("Fluid", LayerType.Liquid) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            var relatives = new List<Point>();
            CollectFluidPool(tile.Relative, identifySubject, in lookup, relatives);
            if (relatives.Count == 0)
                return LayerResult.IdentifyNone();

            var cells = new List<Blueprint.BlueprintCell>(relatives.Count);
            for (int i = 0; i < relatives.Count; i++)
            {
                Point rel = relatives[i];
                PasteTile member = lookup.GetPasteTile(rel);
                cells.Add(new Blueprint.BlueprintCell(rel, member.World, true));
            }

            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            return LayerResult.Identified(TileBatch.FromCells(cells), LayerLogFormat.FormatLiquid(subjectTile));
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

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            string message = LayerLogFormat.FormatLiquid(tiles[0].Target);
            bool changed = false;

            for (int i = 0; i < tiles.Count; i++)
            {
                int x = tiles[i].World.X, y = tiles[i].World.Y;
                if (Main.tile[x, y].LiquidAmount == 0)
                    continue;

                changed = true;
                if (!TileExecutor.EnqueueSetLiquid(x, y, liquidType: 0, amount: 0, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            return changed ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel) : LayerResult.Ok();
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            string okLabel = LayerLogFormat.PlacedLabel(in context);
            bool changed = false;

            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile tile = tiles[i];
                int x = tile.World.X, y = tile.World.Y;
                var src = tile.PayloadCell.Liquid;
                string message = LayerLogFormat.FormatLiquid(tile.PayloadTile);
                Tile worldTile = Main.tile[x, y];
                if (worldTile.LiquidAmount == src.Amount && worldTile.LiquidType == src.Type)
                    continue;

                changed = true;
                if (!TileExecutor.EnqueueSetLiquid(x, y, src.Type, src.Amount, in context))
                    return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            return changed
                ? LayerResult.Ok(LayerLogFormat.FormatLiquid(tiles[0].PayloadTile), okLabel)
                : LayerResult.Ok();
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
