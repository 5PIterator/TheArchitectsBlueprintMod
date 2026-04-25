using System.Collections.Generic;
using ArchitectsStencilMod.Debug;
using ArchitectsStencilMod.Core.Layers;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core
{
    internal interface IPastingTask
    {
        IReadOnlyList<PasteTile> Tiles { get; }

        /// <summary>
        /// Adds this task's world points to the list, up to maxRemaining. Returns the number added.
        /// </summary>
        int AddTilesTo(List<Point> list, int maxRemaining);

        /// <summary>
        /// Appends this task's world tile positions to <paramref name="dst"/>, up to <paramref name="maxRemaining"/>. Returns the number added.
        /// </summary>
        int CollectOverlayPoints(List<Point> dst, int maxRemaining = int.MaxValue);
    }

    internal sealed class ClearingTask(Layer layer, List<PasteTile> tiles) : IPastingTask
    {
        private readonly Layer _layer = layer;
        private readonly List<PasteTile> _tiles = tiles;

        public IReadOnlyList<PasteTile> Tiles => _tiles;

        public int AddTilesTo(List<Point> list, int maxRemaining)
        {
            int added = 0;
            for (int i = 0; i < _tiles.Count && added < maxRemaining; i++)
            {
                list.Add(_tiles[i].World);
                added++;
            }

            return added;
        }

        public int CollectOverlayPoints(List<Point> dst, int maxRemaining = int.MaxValue)
        {
            if (_tiles.Count == 0 || maxRemaining <= 0)
                return 0;

            int n = System.Math.Min(_tiles.Count, maxRemaining);
            for (int i = 0; i < n; i++)
                dst.Add(_tiles[i].World);

            return n;
        }

        /// <summary>
        /// Executes this clearing task. Returns true if the task is complete, false if it needs to be retried with the remaining tiles.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool Execute(in PasteContext context)
        {
            /// If there are no tiles, consider this task complete. This can happen if the task was created with no tiles, or if all tiles were consumed in a previous attempt.

            // Validate Tile positions.
            if (_tiles.Count == 0)
                return true;

            if (!WorldGen.InWorld(_tiles[0].World.X, _tiles[0].World.Y))
                return true;

            // Execute the clearing operation. If it fails, return false and requeue the task.
            var res = _layer.Clear(_tiles, in context);
            ASMLog.Debug($"Clear execute layer={_layer.Id} world=({_tiles[0].World.X},{_tiles[0].World.Y}) success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}", "Paste");
            if (!res.Success)
            {
                if (res.FailReason == LayerFailReason.InsufficientPickaxePower)
                    return true;
                return false;
            }

            // If there are remaining tiles, requeue the task and return false.
            if (res.RemainingTiles.Count > 0)
            {
                _tiles.Clear();
                _tiles.AddRange(res.RemainingTiles);
                return false;
            }

            return true;
        }
    }

    internal sealed class PlacingTask(Layer layer, List<PasteTile> tiles) : IPastingTask
    {
        private readonly Layer _layer = layer;
        private readonly List<PasteTile> _tiles = tiles;

        public IReadOnlyList<PasteTile> Tiles => _tiles;

        public int AddTilesTo(List<Point> list, int maxRemaining)
        {
            int added = 0;
            for (int i = 0; i < _tiles.Count && added < maxRemaining; i++)
            {
                list.Add(_tiles[i].World);
                added++;
            }

            return added;
        }

        public int CollectOverlayPoints(List<Point> dst, int maxRemaining = int.MaxValue)
        {
            if (_tiles.Count == 0 || maxRemaining <= 0)
                return 0;

            int n = System.Math.Min(_tiles.Count, maxRemaining);
            for (int i = 0; i < n; i++)
                dst.Add(_tiles[i].World);

            return n;
        }

        /// <summary>
        /// Executes this placing task. Returns true if the task is complete, false if it needs to be retried with the remaining tiles.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        internal bool Execute(in PasteContext context)
        {
            /// If there are no tiles, consider this task complete. This can happen if the task was created with no tiles, or if all tiles were consumed in a previous attempt.

            // Validate Tile positions.
            if (_tiles.Count == 0)
                return true;

            if (!WorldGen.InWorld(_tiles[0].World.X, _tiles[0].World.Y))
                return true;

            var head = _tiles[0];
            ASMLog.Debug(
                $"Place begin layer={_layer.Id} batch={_tiles.Count} rel=({head.Relative.X},{head.Relative.Y}) world=({head.World.X},{head.World.Y}) payload[hasTile={head.PayloadCell.HasTile()} type={head.PayloadCell.Block.Type} frame=({head.PayloadCell.Block.FrameX},{head.PayloadCell.Block.FrameY})] source[hasTile={head.Source.HasTile} type={head.Source.TileType}] target[hasTile={head.Target.HasTile} type={head.Target.TileType}]",
                "Paste");

            // Execute the placing operation. If it fails, return false and requeue the task.
            var res = _layer.Place(_tiles, in context);
            ASMLog.Debug($"Place execute layer={_layer.Id} world=({_tiles[0].World.X},{_tiles[0].World.Y}) success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}", "Paste");
            if (!res.Success)
            {
                if (context.IgnoreItems && res.FailReason == LayerFailReason.MissingItems)
                    return true;
                if (context.IgnoreInvalid && res.FailReason == LayerFailReason.InvalidPlacement)
                    return true;
                return false;
            }

            // If there are remaining tiles, requeue the task and return false.
            if (res.RemainingTiles.Count > 0)
            {
                _tiles.Clear();
                _tiles.AddRange(res.RemainingTiles);
                return false;
            }

            return true;
        }
    }
}
