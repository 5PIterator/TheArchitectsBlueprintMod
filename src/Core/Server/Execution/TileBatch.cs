using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Immutable snapshot of the full identified cluster while <see cref="TileBatch.Tiles"/> holds the remaining work queue.
    /// </summary>
    internal sealed class TileBatchClusterContext
    {
        private readonly Point[] _allRelatives;

        public IReadOnlyList<Point> AllRelatives => _allRelatives;

        private TileBatchClusterContext(Point[] allRelatives) => _allRelatives = allRelatives;

        /// <summary>Builds cluster context from identified blueprint cell relatives.</summary>
        public static TileBatchClusterContext FromCells(IReadOnlyList<Blueprint.BlueprintCell> cells)
        {
            // --- Copy each cell relative into the immutable cluster footprint.
            var relatives = new Point[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                relatives[i] = cells[i].Relative;

            return new TileBatchClusterContext(relatives);
        }

        /// <summary>Builds cluster context from an explicit relative footprint list.</summary>
        public static TileBatchClusterContext FromRelatives(IReadOnlyList<Point> relatives)
        {
            // --- Copy relatives so execution batches can shrink without losing full-cluster geometry.
            var copy = new Point[relatives.Count];
            for (int i = 0; i < relatives.Count; i++)
                copy[i] = relatives[i];

            return new TileBatchClusterContext(copy);
        }
    }

    /// <summary>
    /// Mutable group of tiles processed together by a layer task, or identified blueprint cells from <see cref="Core.Layers.Layer.Identify"/>.
    /// Holds either <see cref="Tiles"/> (clear/place execution) or <see cref="Cells"/> (identify), never both.
    /// </summary>
    internal sealed class TileBatch
    {
        private readonly List<PasteTile> _tiles;
        private readonly List<Blueprint.BlueprintCell> _cells;
        private TileBatchClusterContext? _clusterContext;
        private int _completedPasses;

        public TileBatch(List<PasteTile> tiles, TileBatchClusterContext? clusterContext = null)
        {
            _tiles = tiles ?? [];
            _cells = [];
            InitialCount = _tiles.Count;
            _clusterContext = clusterContext;
        }

        private TileBatch(List<Blueprint.BlueprintCell> cells, TileBatchClusterContext? clusterContext)
        {
            _tiles = [];
            _cells = cells ?? [];
            InitialCount = _cells.Count;
            _clusterContext = clusterContext ?? TileBatchClusterContext.FromCells(_cells);
        }

        public IReadOnlyList<PasteTile> Tiles => _tiles;

        public IReadOnlyList<Blueprint.BlueprintCell> Cells => _cells;

        public TileBatchClusterContext? ClusterContext => _clusterContext;

        public int InitialCount { get; }

        public int Count => _tiles.Count > 0 ? _tiles.Count : _cells.Count;

        public bool IsEmpty => Count == 0;

        public bool HasPasteTiles => _tiles.Count > 0;

        public bool HasCells => _cells.Count > 0;

        public PasteTile Head => _tiles[0];

        /// <summary>Creates a batch of identified blueprint cells for plan/identify results.</summary>
        public static TileBatch FromCells(
            IReadOnlyList<Blueprint.BlueprintCell> cells,
            TileBatchClusterContext? clusterContext = null) =>
            new([.. cells], clusterContext);

        /// <summary>Returns a new execution batch with the same cluster context and a smaller remaining queue.</summary>
        public TileBatch WithRemainingTiles(IReadOnlyList<PasteTile> remaining) =>
            new([.. remaining], _clusterContext);

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

        /// <summary>Replaces the remaining queue and adopts cluster context when the current batch has none.</summary>
        public void ReplaceWith(TileBatch batch)
        {
            // --- Keep the original full-cluster snapshot while swapping in the next remaining queue.
            ReplaceWith(batch.Tiles);
            if (_clusterContext is null && batch._clusterContext is not null)
                _clusterContext = batch._clusterContext;
        }

        /// <summary>Returns e.g. <c> (7/8)</c> for in-progress multi-tile batches; null when <c>1/1</c>.</summary>
        public string? FormatBatchProgressSuffix(int countBefore, int remainingAfter)
        {
            if (!TryGetBatchProgress(countBefore, remainingAfter, out int pass, out int total))
                return null;

            if (pass == 1 && total == 1)
                return null;

            return $" ({pass}/{total})";
        }

        private bool TryGetBatchProgress(int countBefore, int remainingAfter, out int pass, out int total)
        {
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

            return true;
        }
    }
}
