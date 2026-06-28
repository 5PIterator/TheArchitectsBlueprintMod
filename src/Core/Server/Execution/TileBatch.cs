using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Mutable group of tiles processed together by a layer task.
    /// </summary>
    internal sealed class TileBatch
    {
        private readonly List<PasteTile> _tiles;
        private int _completedPasses;

        public TileBatch(List<PasteTile> tiles)
        {
            _tiles = tiles ?? [];
            InitialCount = _tiles.Count;
        }

        public IReadOnlyList<PasteTile> Tiles => _tiles;

        public int InitialCount { get; }

        public int Count => _tiles.Count;

        public bool IsEmpty => _tiles.Count == 0;

        public PasteTile Head => _tiles[0];

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

        public void ReplaceWith(IReadOnlyList<PasteTile> tiles)
        {
            _tiles.Clear();
            for (int i = 0; i < tiles.Count; i++)
                _tiles.Add(tiles[i]);
        }

        /// <summary>Formats partial-batch retry detail, e.g. <c>Batch (1/2)</c>.</summary>
        public string FormatBatchRetryDetail(int countBefore, int remainingAfter)
        {
            int pass;
            int total;

            if (remainingAfter < countBefore)
            {
                pass = InitialCount - remainingAfter;
                total = InitialCount;
            }
            else
            {
                _completedPasses++;
                pass = _completedPasses;
                total = InitialCount == 1 ? 2 : InitialCount;
            }

            return $"Batch ({pass}/{total})";
        }
    }
}
