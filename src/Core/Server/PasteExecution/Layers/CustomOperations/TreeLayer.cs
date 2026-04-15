using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.TileMapping;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core.Layers
{
    [CustomPasteLayer]
    internal sealed class TreeLayer : Layer
    {
        public static readonly TreeLayer Definition = new();
        private const string DefinitionId = "Tree";
        private const LayerType DefinitionLayerType = LayerType.Block;

        /// <summary>Cardinal steps for 4-connected tree mass inside the paste (<see cref="PasteTileLookup.TryGetPasteTile"/>).</summary>
        private static readonly Point[] TreeNeighborOffsets = [new(0, -1), new(1, 0), new(-1, 0), new(0, 1)];

        private TreeLayer() : base(DefinitionId, DefinitionLayerType) { }

        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        /// <summary>True when this tile type is handled by the tree layer (block exclusion / identification).</summary>
        public static bool IsTreeType(ushort tileType)
        {
            return TileID.Sets.IsATreeTrunk[tileType]
                || TileID.Sets.IsShakeable[tileType]
                || TileID.Sets.CountsAsGemTree[tileType]
                || TileID.Sets.GetsCheckedForLeaves[tileType]
                || TileID.Sets.Leaves[tileType];
        }

        public override IReadOnlyList<Blueprint.BoundedTile> Identify(in PasteTile tile, in PasteTileLookup lookup)
        {
            var buffer = new List<Point>();
            CollectTreeRelatives(tile.Relative, tile.SubjectType, in lookup, buffer);
            if (buffer.Count == 0) return [];

            var bounded = new List<Blueprint.BoundedTile>(buffer.Count);
            for (int i = 0; i < buffer.Count; i++)
                bounded.Add(new Blueprint.BoundedTile(buffer[i], selected: true));
            return bounded;
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
        {
            if (!Main.tile[tile.World.X, tile.World.Y].HasTile) return false;

            var buffer = new List<Point>();
            CollectTreeRelatives(tile.Relative, PasteSubject.Target, in lookup, buffer);
            if (buffer.Count == 0) return false;

            Point trunkRel = SelectTrunkGroundRelative(buffer);
            return trunkRel == tile.Relative;
        }

        public override bool CanPlace(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
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
            PasteTile trunk = SelectTrunkPasteTile(tiles);
            return TileOperations.Clear(trunk, in context);
        }

        public override LayerResult Place(IReadOnlyList<PasteTile> tiles, in PasteContext context)
        {
            if (tiles.Count == 0) return LayerResult.Ok();

            if (AllScaffold(tiles)) return CommitTreePayloadAndFx(tiles);

            int idx = SelectNextScaffoldIndex(tiles);
            if (idx < 0) return CommitTreePayloadAndFx(tiles);

            LayerResult wood = PlaceLivingWoodScaffold(tiles[idx], in context);
            if (!wood.Success) return wood;

            if (AllScaffold(tiles)) return CommitTreePayloadAndFx(tiles);

            var remaining = new List<PasteTile>(tiles.Count - 1);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (i != idx) remaining.Add(tiles[i]);
            }

            return LayerResult.Ok(tiles[idx].World, remaining);
        }

        public override bool Consume(in PasteTile tile, in PasteContext context)
        {
            Tile world = Main.tile[tile.World.X, tile.World.Y];
            if (world.HasTile && world.TileType == TileID.LivingWood) return true;
            return TileOperations.Consume(in tile, in context);
        }

        public override bool IsCustom(ushort tileType) => IsTreeType(tileType);

        private static void CollectTreeRelatives(Point startRel, PasteSubject classify, in PasteTileLookup lookup, List<Point> outPoints)
        {
            outPoints.Clear();
            if (!lookup.TryGetPasteTile(startRel, out PasteTile seed)) return;
            Tile seedView = PasteTile.Face(classify, in seed);
            if (!seedView.HasTile || seedView.TileType >= TileLoader.TileCount || !IsTreeType(seedView.TileType)) return;

            var visited = new HashSet<Point>();
            var queue = new Queue<Point>();
            queue.Enqueue(startRel);
            visited.Add(startRel);

            while (queue.Count > 0)
            {
                Point cur = queue.Dequeue();
                outPoints.Add(cur);

                for (int i = 0; i < TreeNeighborOffsets.Length; i++)
                {
                    Point d = TreeNeighborOffsets[i];
                    Point n = new(cur.X + d.X, cur.Y + d.Y);
                    if (visited.Contains(n)) continue;
                    if (!lookup.TryGetPasteTile(n, out PasteTile neighbor)) continue;
                    Tile view = PasteTile.Face(classify, in neighbor);
                    if (!view.HasTile || view.TileType >= TileLoader.TileCount) continue;
                    if (!IsTreeType(view.TileType)) continue;

                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        private static bool IsScaffold(int x, int y) =>
            Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.LivingWood;

        private static bool AllScaffold(IReadOnlyList<PasteTile> tiles)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                Point w = tiles[i].World;
                if (!IsScaffold(w.X, w.Y)) return false;
            }

            return true;
        }

        private static LayerResult PlaceLivingWoodScaffold(in PasteTile tile, in PasteContext context)
        {
            int x = tile.World.X, y = tile.World.Y;
            if (!WorldGen.PlaceTile(x, y, TileID.LivingWood, mute: true, forced: true, plr: context.Player.whoAmI, style: 0))
                return LayerResult.Fail(tile.World);

            if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != TileID.LivingWood)
                return LayerResult.Fail(tile.World);

            LayerSync.SyncTileSquare(x, y);
            return LayerResult.Ok(tile.World);
        }

        private static LayerResult CommitTreePayloadAndFx(IReadOnlyList<PasteTile> tiles)
        {
            PasteTile trunk = SelectTrunkPasteTile(tiles);
            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile t = tiles[i];
                int x = t.World.X, y = t.World.Y;
                ApplyPayloadTileBlockOnly(x, y, t.Payload);
                LayerSync.SyncTileSquare(x, y);
            }

            PlayTreePasteFx(tiles, trunk.World);
            return LayerResult.Ok(trunk.World);
        }

        /// <summary>
        /// Applies only solid-tile identity + framing from the blueprint payload. Leaves wall, liquid, wiring,
        /// paint, and detail (actuation/slope) to their own layers so <see cref="Tile.CopyFrom"/> does not clobber them.
        /// </summary>
        private static void ApplyPayloadTileBlockOnly(int x, int y, Tile payload)
        {
            if (!payload.HasTile || payload.TileType >= TileLoader.TileCount) return;

            Tile dest = Main.tile[x, y];
            dest.HasTile = true;
            dest.TileType = payload.TileType;

            ref TileWallWireStateData state = ref ModifierOperations.GetState(x, y);
            state.TileFrameX = payload.TileFrameX;
            state.TileFrameY = payload.TileFrameY;
            state.BlockType = payload.BlockType;
        }

        private static void PlayTreePasteFx(IReadOnlyList<PasteTile> tiles, Point trunkWorld)
        {
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            for (int i = 0; i < tiles.Count; i++)
            {
                int y = tiles[i].World.Y;
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            int height = Math.Max(1, maxY - minY + 1);
            WorldGen.TreeGrowFX(trunkWorld.X, trunkWorld.Y, height, treeGore: 0, hitTree: true);
        }

        /// <summary>Bottom-up among cells that still need living-wood scaffold.</summary>
        private static int SelectNextScaffoldIndex(IReadOnlyList<PasteTile> tiles)
        {
            int trunkX = MedianRelativeX(tiles);
            int best = -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                Point w = tiles[i].World;
                if (IsScaffold(w.X, w.Y)) continue;
                if (best < 0 || ComparePlaceOrder(tiles[i], tiles[best], trunkX) < 0) best = i;
            }

            return best;
        }

        /// <summary>Trunk tile for felling / FX: deepest on median column, then closest to median X.</summary>
        private static Point SelectTrunkGroundRelative(IReadOnlyList<Point> relatives)
        {
            int medX = MedianFromPoints(relatives, static p => p.X);
            int best = 0;
            for (int i = 1; i < relatives.Count; i++)
            {
                if (CompareTrunkGroundPriority(relatives[i], relatives[best], medX) < 0) best = i;
            }

            return relatives[best];
        }

        private static PasteTile SelectTrunkPasteTile(IReadOnlyList<PasteTile> tiles)
        {
            int trunkX = MedianRelativeX(tiles);
            int best = 0;
            for (int i = 1; i < tiles.Count; i++)
            {
                if (CompareTrunkGroundPriority(tiles[i].Relative, tiles[best].Relative, trunkX) < 0) best = i;
            }

            return tiles[best];
        }

        /// <summary>Negative if <paramref name="a"/> is a better trunk anchor than <paramref name="b"/>.</summary>
        private static int CompareTrunkGroundPriority(Point aRel, Point bRel, int medX)
        {
            int y = bRel.Y.CompareTo(aRel.Y);
            if (y != 0) return y;
            int ax = Math.Abs(aRel.X - medX);
            int bx = Math.Abs(bRel.X - medX);
            int tie = ax.CompareTo(bx);
            return tie != 0 ? tie : aRel.X.CompareTo(bRel.X);
        }

        /// <summary>Negative if <paramref name="a"/> should be placed before <paramref name="b"/>.</summary>
        private static int ComparePlaceOrder(in PasteTile a, in PasteTile b, int trunkX)
        {
            int y = b.World.Y.CompareTo(a.World.Y);
            if (y != 0) return y;
            int ax = Math.Abs(a.Relative.X - trunkX);
            int bx = Math.Abs(b.Relative.X - trunkX);
            int tie = ax.CompareTo(bx);
            return tie != 0 ? tie : a.Relative.X.CompareTo(b.Relative.X);
        }

        private static int MedianRelativeX(IReadOnlyList<PasteTile> tiles) =>
            MedianFromPoints(tiles, static t => t.Relative.X);

        private static int MedianFromPoints<T>(IReadOnlyList<T> items, Func<T, int> selectX)
        {
            int n = items.Count;
            if (n == 0) return 0;
            var xs = new int[n];
            for (int i = 0; i < n; i++)
                xs[i] = selectX(items[i]);
            Array.Sort(xs);
            return xs[n / 2];
        }
    }
}
