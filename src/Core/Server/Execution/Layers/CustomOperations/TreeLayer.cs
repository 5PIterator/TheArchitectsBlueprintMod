using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    [CustomPasteLayer]
    /// <summary>Identifies and places trees, gem trees, and branched cactus clusters.</summary>
    internal sealed class TreeLayer : Layer
    {
        public static readonly TreeLayer Definition = new();

        /// <summary>Cardinal steps for 4-connected tree mass inside the paste lookup bounds.</summary>
        private static readonly Point[] TreeNeighborOffsets = [new(0, -1), new(1, 0), new(-1, 0), new(0, 1)];

        private TreeLayer() : base("Tree", LayerType.Custom) { }

        /// <summary>Checks tile-type equality between payload/source intent and target world.</summary>
        public override bool Match(in PasteTile tile) => TileComparison.MatchTileType(in tile);

        /// <summary>Returns true when this tile type is handled by tree logic.</summary>
        public static bool IsTreeType(ushort tileType)
        {
            if (tileType >= TileLoader.TileCount)
                return false;

            if (tileType == TileID.Cactus)
                return true;

            return TileID.Sets.IsATreeTrunk[tileType]
                || TileID.Sets.CountsAsGemTree[tileType]
                || TileID.Sets.GetsCheckedForLeaves[tileType];
        }

        /// <summary>Returns the connected tree/cactus footprint around this tile.</summary>
        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup lookup, PasteSubject identifySubject)
        {
            // --- BFS 4-connected tree tiles inside paste bounds, then emit blueprint cells for the cluster.
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            if (!subjectTile.HasTile || !IsTreeType(subjectTile.TileType))
                return LayerResult.IdentifyNone();

            var relatives = new List<Point>();
            CollectTreeRelatives(tile.Relative, identifySubject, in lookup, relatives);
            if (relatives.Count == 0)
                return LayerResult.IdentifyNone();

            bool sourceCut = TileComparison.IsSourceCutIntent(in tile);
            var cells = new List<Blueprint.BlueprintCell>(relatives.Count);
            for (int i = 0; i < relatives.Count; i++)
            {
                Point rel = relatives[i];
                if (!lookup.TryGetPasteTile(rel, out PasteTile neighbor))
                    continue;

                if (identifySubject == PasteSubject.Payload && !neighbor.PayloadCell.HasTile())
                    continue;

                cells.Add(new Blueprint.BlueprintCell(
                    rel,
                    ClusterCellWorld(in tile, rel, in neighbor, sourceCut),
                    true));
            }

            return cells.Count > 0
                ? LayerResult.Identified(
                    TileBatch.FromCells(cells, TileBatchClusterContext.FromRelatives(relatives)),
                    LayerLogFormat.FormatTile(subjectTile))
                : LayerResult.IdentifyNone();
        }

        /// <summary>Determines whether this target tile still needs clearing.</summary>
        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context) =>
            Main.tile[tile.World.X, tile.World.Y].HasTile;

        /// <summary>Validates the next ground-up cluster segment using TileLoader and support rules.</summary>
        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
        {
            // --- Only the bottom-most trunk anchor requires live base support; upper segments defer to bottom-up placement.
            if (!tile.PayloadCell.HasTile())
                return true;

            var relatives = new List<Point>();
            CollectTreeRelatives(tile.Relative, PasteSubject.Payload, in lookup, relatives);
            if (relatives.Count == 0)
                return true;

            Point trunkGround = SelectTrunkGroundRelative(relatives);
            if (trunkGround != tile.Relative)
                return true;

            return HasValidTrunkBaseBelow(in tile, in context);
        }

        /// <summary>Checks whether inventory has the required item for this tile when item checks are enabled.</summary>
        public override bool HasRequiredItems(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
        {
            // --- Skip checks when requirements are disabled; otherwise query interaction-map item ids for payload tile + style.
            if (!context.RequireItems || context.Items is null || context.IsSweepCheat) return true;
            if (!tile.PayloadCell.HasTile()) return true;
            int style = tile.PayloadCell.Detail.Style;
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        /// <summary>Checks whether this tile can consume an inventory item now.</summary>
        public override bool CanConsume(in PasteTile tile, in PasteContext context)
        {
            // --- Use the same tile+style item lookup as planning checks to keep queue behavior deterministic.
            if (!tile.PayloadCell.HasTile() || !context.RequireItems || context.Items is null) return true;
            int style = tile.PayloadCell.Detail.Style;
            return TileExecutor.HasItemForTile((ushort)tile.PayloadCell.Block.Type, style, in context);
        }

        /// <summary>Consumes one item for a successful placement.</summary>
        public override bool Consume(in PasteTile tile, in PasteContext context) =>
            TileOperations.Consume(in tile, in context);

        /// <summary>Returns candidate inventory items that can place this payload tile.</summary>
        public override IReadOnlyList<int>? GetItem(in PasteTile tile)
        {
            // --- Resolve tile-item mappings from the interaction map using payload tile type + style.
            if (!tile.PayloadCell.HasTile()) return null;
            int style = tile.PayloadCell.Detail.Style;
            return InteractionMapProvider.GetTileItemIds((ushort)tile.PayloadCell.Block.Type, style);
        }

        /// <summary>Clears one cluster tile per pass: branches top-down, then trunk column top-down.</summary>
        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            // --- Remove branches before the trunk column; within each column clear top-down.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            int trunkMedX = ResolveTrunkColumnX(batch);
            int index = SelectNextClearIndex(batch.Tiles, trunkMedX);
            PasteTile segment = batch.Tiles[index];
            LayerResult result = TileOperations.Clear(segment, in context);
            if (!result.Success)
                return result;

            return RemoveSegmentAt(batch, index, result.Message, result.OkLabel);
        }

        /// <summary>Places one cluster tile per pass: trunk column first, then branches, each bottom-up.</summary>
        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Pick the next trunk-column or branch segment using trunk-first, bottom-up ordering.
            if (batch.IsEmpty)
                return LayerResult.Ok();

            int trunkMedX = ResolveTrunkColumnX(batch);
            int index = SelectNextPlaceIndex(batch.Tiles, trunkMedX);
            PasteTile segment = batch.Tiles[index];
            string message = LayerLogFormat.FormatTile(segment.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);

            if (!segment.PayloadCell.HasTile() || IsSegmentAlreadyPlaced(in segment))
                return RemoveSegmentAt(batch, index, message, okLabel);

            if (!HasSegmentPlacementSupport(in segment, in context))
            {
                if (context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
            }

            LayerResult result = PlaceTreeSegment(in segment, in context);
            if (!result.Success)
            {
                if (result.FailReason == LayerFailReason.InvalidPlacement && context.RetryOnInvalidPlacement)
                    return LayerResult.OkWithRemaining(batch, message, okLabel);
                return result;
            }

            if (!context.IsSweepCheat && !Consume(in segment, in context))
                return LayerResult.Fail(LayerFailReason.MissingItems, message);

            return RemoveSegmentAt(batch, index, result.Message ?? message, result.OkLabel ?? okLabel);
        }

        public override bool IsCustom(ushort tileType) => IsTreeType(tileType);

        /// <summary>Collects all 4-connected tree relatives inside paste bounds via BFS.</summary>
        private static void CollectTreeRelatives(
            Point startRel,
            PasteSubject classify,
            in PasteTileLookup lookup,
            List<Point> outPoints)
        {
            // --- Flood-fill cardinal neighbors while tile type remains a tree trunk/cactus member.
            outPoints.Clear();
            if (!lookup.TryGetPasteTile(startRel, out PasteTile seed))
                return;

            Tile seedView = PasteTile.GetTileForIdentify(in seed, classify);
            if (!seedView.HasTile || !IsTreeType(seedView.TileType))
                return;

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
                    Point offset = TreeNeighborOffsets[i];
                    Point neighborRel = new(cur.X + offset.X, cur.Y + offset.Y);
                    if (visited.Contains(neighborRel))
                        continue;
                    if (!lookup.TryGetPasteTile(neighborRel, out PasteTile neighbor))
                        continue;

                    Tile view = PasteTile.GetTileForIdentify(in neighbor, classify);
                    if (!view.HasTile || !IsTreeType(view.TileType))
                        continue;

                    visited.Add(neighborRel);
                    queue.Enqueue(neighborRel);
                }
            }
        }

        /// <summary>Resolves world coordinates for identified cluster cells during normal and source-cut passes.</summary>
        private static Point ClusterCellWorld(in PasteTile anchor, Point relative, in PasteTile lookupTile, bool sourceCut) =>
            sourceCut
                ? new Point(anchor.World.X + relative.X - anchor.Relative.X, anchor.World.Y + relative.Y - anchor.Relative.Y)
                : lookupTile.World;

        /// <summary>Returns the bottom-most trunk relative position using trunk-column tie-breaking.</summary>
        private static Point SelectTrunkGroundRelative(IReadOnlyList<Point> relatives)
        {
            // --- Deepest Y wins; ties break toward the cluster trunk column.
            int medX = ResolveTrunkColumnXFromRelatives(relatives);
            int best = 0;
            for (int i = 1; i < relatives.Count; i++)
            {
                if (CompareTrunkGroundPriority(relatives[i], relatives[best], medX) < 0)
                    best = i;
            }

            return relatives[best];
        }

        /// <summary>Returns true when ground or connected trunk support allows placing this segment.</summary>
        private static bool HasSegmentPlacementSupport(in PasteTile segment, in PasteContext context) =>
            HasValidTrunkBaseBelow(in segment, in context) || HasConnectedTrunkSupport(in segment);

        /// <summary>Returns true when a live tree trunk sits below or horizontally adjacent to this segment.</summary>
        private static bool HasConnectedTrunkSupport(in PasteTile segment)
        {
            // --- Branches attach to the trunk column via same-Y neighbors or a trunk segment directly below.
            int x = segment.World.X;
            int y = segment.World.Y;

            int belowY = y + 1;
            if (WorldGen.InWorld(x, belowY, 0))
            {
                Tile below = Main.tile[x, belowY];
                if (below.HasTile && IsTreeType(below.TileType))
                    return true;
            }

            for (int dx = -1; dx <= 1; dx += 2)
            {
                int nx = x + dx;
                if (!WorldGen.InWorld(nx, y, 0))
                    continue;

                Tile neighbor = Main.tile[nx, y];
                if (neighbor.HasTile && IsTreeType(neighbor.TileType))
                    return true;
            }

            return false;
        }

        /// <summary>Resolves the trunk column X from the batch cluster context.</summary>
        private static int ResolveTrunkColumnX(TileBatch batch)
        {
            // --- Use the immutable full-cluster footprint so branch-only remaining queues stay ordered correctly.
            IReadOnlyList<Point>? relatives = batch.ClusterContext?.AllRelatives;
            if (relatives is not null && relatives.Count > 0)
                return ResolveTrunkColumnXFromRelatives(relatives);

            return ResolveTrunkColumnXFromPending(batch.Tiles);
        }

        /// <summary>Picks the trunk column from cluster relatives by tile count, then deepest reach.</summary>
        private static int ResolveTrunkColumnXFromRelatives(IReadOnlyList<Point> relatives)
        {
            // --- The tallest column with the most segments is treated as the trunk column.
            var countByX = new Dictionary<int, int>();
            var maxYByX = new Dictionary<int, int>();
            for (int i = 0; i < relatives.Count; i++)
            {
                int x = relatives[i].X;
                int y = relatives[i].Y;
                countByX[x] = countByX.GetValueOrDefault(x) + 1;
                if (!maxYByX.TryGetValue(x, out int maxY) || y > maxY)
                    maxYByX[x] = y;
            }

            int bestX = relatives[0].X;
            int bestCount = -1;
            int bestMaxY = -1;
            foreach (var kv in countByX)
            {
                int x = kv.Key;
                int count = kv.Value;
                int maxY = maxYByX[x];
                if (count > bestCount || (count == bestCount && maxY > bestMaxY))
                {
                    bestCount = count;
                    bestMaxY = maxY;
                    bestX = x;
                }
            }

            return bestX;
        }

        /// <summary>Picks the trunk column from a pending batch when lookup is unavailable.</summary>
        private static int ResolveTrunkColumnXFromPending(IReadOnlyList<PasteTile> tiles)
        {
            // --- Fall back to pending geometry using the same count-and-depth heuristic.
            var relatives = new List<Point>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
                relatives.Add(tiles[i].Relative);

            return ResolveTrunkColumnXFromRelatives(relatives);
        }

        /// <summary>Removes one segment from the batch and returns remaining work or completion.</summary>
        private static LayerResult RemoveSegmentAt(
            TileBatch batch,
            int index,
            string? message,
            string? okLabel)
        {
            // --- Drop the handled segment and continue with the rest of the cluster batch.
            var remaining = new List<PasteTile>(batch.Tiles.Count - 1);
            for (int i = 0; i < batch.Tiles.Count; i++)
            {
                if (i != index)
                    remaining.Add(batch.Tiles[i]);
            }

            return remaining.Count > 0
                ? LayerResult.OkWithRemaining(batch.WithRemainingTiles(remaining), message, okLabel)
                : LayerResult.Ok(message, okLabel);
        }

        /// <summary>Returns true when the live tile already matches the payload tree segment.</summary>
        private static bool IsSegmentAlreadyPlaced(in PasteTile segment)
        {
            if (!segment.PayloadCell.HasTile())
                return false;

            Tile live = Main.tile[segment.World.X, segment.World.Y];
            if (!live.HasTile || live.TileType != segment.PayloadCell.Block.Type)
                return false;

            return live.TileFrameX == segment.PayloadCell.Block.FrameX
                && live.TileFrameY == segment.PayloadCell.Block.FrameY;
        }

        /// <summary>Places one tree trunk tile by writing payload framing directly.</summary>
        private static LayerResult PlaceTreeSegment(in PasteTile segment, in PasteContext context)
        {
            // --- Support is validated before this call; commit payload type and frames without CanPlace.
            int x = segment.World.X;
            int y = segment.World.Y;
            if (!segment.PayloadCell.HasTile())
                return LayerResult.Ok();

            string message = LayerLogFormat.FormatTile(segment.PayloadTile);
            string okLabel = LayerLogFormat.PlacedLabel(in context);
            ushort tileType = (ushort)segment.PayloadCell.Block.Type;
            Tile dest = Main.tile[x, y];

            if (dest.HasTile && dest.TileType != tileType && context.UseReplacePlace)
                TileExecutor.EnqueueVoidKillTile(x, y, in context);

            ApplyPayloadCellBlockOnly(x, y, in segment.PayloadCell);
            LayerSync.SyncTileSquare(x, y);
            return LayerResult.Ok(message, okLabel);
        }

        /// <summary>Applies solid-tile identity and framing from the payload without clobbering other layers.</summary>
        private static void ApplyPayloadCellBlockOnly(int x, int y, in PayloadCell payload)
        {
            if (payload.IsEmpty || !payload.Detail.HasTile || payload.Block.Type >= TileLoader.TileCount)
                return;

            Tile dest = Main.tile[x, y];
            dest.HasTile = true;
            dest.TileType = (ushort)payload.Block.Type;

            TileWallWireStateData state = DetailOperations.ReadState(x, y);
            var block = payload.Block;
            state.TileFrameX = (short)block.FrameX;
            state.TileFrameY = (short)block.FrameY;
            state.BlockType = payload.Detail.BlockType;
            DetailOperations.WriteState(x, y, in state);
        }

        /// <summary>Returns the next clear index: branch columns top-down, then trunk column top-down.</summary>
        private static int SelectNextClearIndex(IReadOnlyList<PasteTile> pending, int trunkMedX)
        {
            // --- Clear branches before the trunk column; within each column remove the highest segment first.
            int selected = -1;
            for (int i = 0; i < pending.Count; i++)
            {
                if (HasPendingSegmentAboveInColumn(pending, pending[i]))
                    continue;

                if (selected < 0
                    || CompareClearOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            if (selected >= 0)
                return selected;

            selected = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (CompareClearOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns true when a higher segment in the same column still needs clearing.</summary>
        private static bool HasPendingSegmentAboveInColumn(IReadOnlyList<PasteTile> pending, in PasteTile segment)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Relative.X == segment.Relative.X
                    && pending[i].Relative.Y < segment.Relative.Y)
                    return true;
            }

            return false;
        }

        /// <summary>Negative if <paramref name="aRel"/> should be cleared before <paramref name="bRel"/>.</summary>
        private static int CompareClearOrder(Point aRel, Point bRel, int trunkMedX) =>
            -ComparePlaceOrder(aRel, bRel, trunkMedX);

        /// <summary>Returns the next place index: trunk column bottom-up, then branch columns bottom-up.</summary>
        private static int SelectNextPlaceIndex(IReadOnlyList<PasteTile> pending, int trunkMedX)
        {
            // --- Exhaust the median trunk column before any branch column; within each column go bottom-up.
            int selected = -1;
            for (int i = 0; i < pending.Count; i++)
            {
                if (HasPendingSegmentBelowInColumn(pending, pending[i]))
                    continue;

                if (selected < 0
                    || ComparePlaceOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            if (selected >= 0)
                return selected;

            selected = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (ComparePlaceOrder(pending[i].Relative, pending[selected].Relative, trunkMedX) < 0)
                    selected = i;
            }

            return selected;
        }

        /// <summary>Returns true when a deeper segment in the same column still needs placement.</summary>
        private static bool HasPendingSegmentBelowInColumn(IReadOnlyList<PasteTile> pending, in PasteTile segment)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Relative.X == segment.Relative.X
                    && pending[i].Relative.Y > segment.Relative.Y)
                    return true;
            }

            return false;
        }

        /// <summary>Negative if <paramref name="aRel"/> should be placed before <paramref name="bRel"/>.</summary>
        private static int ComparePlaceOrder(Point aRel, Point bRel, int trunkMedX)
        {
            bool aTrunk = aRel.X == trunkMedX;
            bool bTrunk = bRel.X == trunkMedX;
            if (aTrunk != bTrunk)
                return aTrunk ? -1 : 1;

            int y = bRel.Y.CompareTo(aRel.Y);
            if (y != 0)
                return y;

            return aRel.X.CompareTo(bRel.X);
        }

        /// <summary>Returns true when the tile below provides valid support for the bottom-most trunk.</summary>
        private static bool HasValidTrunkBaseBelow(in PasteTile tile, in PasteContext context)
        {
            // --- Accept mapped base tiles, solid ground below, surface blocks at the trunk foot, or sweep placement.
            int x = tile.World.X;
            int y = tile.World.Y;
            ushort tileType = (ushort)tile.PayloadCell.Block.Type;
            int style = tile.PayloadCell.Detail.Style;

            if (IsValidSupportTile(Main.tile[x, y], tileType, style))
                return true;

            int belowY = y + 1;
            if (!WorldGen.InWorld(x, belowY, 0))
                return false;

            if (IsValidSupportTile(Main.tile[x, belowY], tileType, style))
                return true;

            if (WorldGen.SolidOrSlopedTile(x, belowY))
                return true;

            return context.IsSweepCheat && TileLoader.CanPlace(x, y, tileType);
        }

        /// <summary>Returns true when a live tile satisfies tree ground support for the payload type.</summary>
        private static bool IsValidSupportTile(in Tile support, ushort treeTileType, int treeStyle)
        {
            if (!support.HasTile || IsTreeType(support.TileType))
                return false;

            int? baseTileId = InteractionMapProvider.GetRequiredBaseTile(treeTileType, treeStyle);
            if (baseTileId.HasValue)
                return support.TileType == (ushort)baseTileId.Value;

            return WorldGen.SolidTile(support);
        }

        /// <summary>Negative if <paramref name="aRel"/> is a better trunk anchor than <paramref name="bRel"/>.</summary>
        private static int CompareTrunkGroundPriority(Point aRel, Point bRel, int medX)
        {
            int y = bRel.Y.CompareTo(aRel.Y);
            if (y != 0)
                return y;

            int ax = Math.Abs(aRel.X - medX);
            int bx = Math.Abs(bRel.X - medX);
            int tie = ax.CompareTo(bx);
            return tie != 0 ? tie : aRel.X.CompareTo(bRel.X);
        }
    }
}
