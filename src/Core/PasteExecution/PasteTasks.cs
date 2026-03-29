using System.Collections.Generic;
using ArchitectsStencilMod.Debug;
using ArchitectsStencilMod.Core.Layers;
using ArchitectsStencilMod.UI;
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
        /// Pushes this task's tiles to the overlay. Returns the number of tiles pushed.
        /// </summary>
        int PushOverlay(Color color, int maxTiles = int.MaxValue);
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

        public int PushOverlay(Color color, int maxTiles = int.MaxValue)
        {
            if (_tiles.Count == 0 || maxTiles <= 0)
                return 0;

            int n = System.Math.Min(_tiles.Count, maxTiles);
            var points = new Point[n];
            for (int i = 0; i < n; i++)
                points[i] = _tiles[i].World;

            Overlay.DrawTile(points, color, "pasting");
            return n;
        }

        public bool Execute(in PasteContext context)
        {
            if (_tiles.Count == 0)
                return true;

            if (!WorldGen.InWorld(_tiles[0].X, _tiles[0].Y))
                return true;

            var res = _layer.Clear(_tiles, in context);
            ASMLog.Debug($"Clear execute layer={_layer.Id} world=({_tiles[0].X},{_tiles[0].Y}) success={res.Success} remaining={res.RemainingTiles.Count}", "Paste");
            if (!res.Success)
                return false;

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

        public int PushOverlay(Color color, int maxTiles = int.MaxValue)
        {
            if (_tiles.Count == 0 || maxTiles <= 0)
                return 0;

            int n = System.Math.Min(_tiles.Count, maxTiles);
            var points = new Point[n];
            for (int i = 0; i < n; i++)
                points[i] = _tiles[i].World;

            Overlay.DrawTile(points, color, "pasting");
            return n;
        }

        internal bool Execute(in PasteContext context)
        {
            if (_tiles.Count == 0)
                return true;

            if (!WorldGen.InWorld(_tiles[0].X, _tiles[0].Y))
                return true;

            var res = _layer.Place(_tiles, in context);
            ASMLog.Debug($"Place execute layer={_layer.Id} world=({_tiles[0].X},{_tiles[0].Y}) success={res.Success} remaining={res.RemainingTiles.Count}", "Paste");
            if (!res.Success)
                return false;

            var remainingSet = new HashSet<(int X, int Y)>();
            for (int i = 0; i < res.RemainingTiles.Count; i++)
                remainingSet.Add((res.RemainingTiles[i].X, res.RemainingTiles[i].Y));

            if (context.NeedsItems)
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    var t = _tiles[i];
                    if (!remainingSet.Contains((t.X, t.Y)) && !_layer.Consume(in t, in context))
                    {
                        ASMLog.Debug($"Place consume failed layer={_layer.Id} world=({t.X},{t.Y})", "Paste");
                        return false;
                    }
                }
            }

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
