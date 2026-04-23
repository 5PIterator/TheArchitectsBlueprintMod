using System;
using System.Collections;
using System.Collections.Generic;
using ArchitectsStencilMod.Debug;
using ArchitectsStencilMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ObjectData;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// A movable ghost of a <see cref="Selection"/>: carries the <see cref="Core.Payload">payload</see>
    /// (detached <c>TileData</c> snapshots per cell), aligned with <see cref="SourceArea"/> / <see cref="TargetArea"/>,
    /// and <see cref="SourceTiles"/> / <see cref="TargetTiles"/> walk orders over the 2D snapshot mask.
    /// <see cref="SourceArea"/> and the initial snapshot set are fixed when the projection is constructed;
    /// fill <see cref="Payload"/> afterward via <see cref="LoadPayloadFromWorld"/> or <see cref="SetPayload"/>.
    /// </summary>
    /// <param name="cutSource">Initial cut-from-source flag (still clamped when there is no payload or no source area).</param>
    /// <param name="sourceArea">World rectangle for world capture / source clear; omit or empty when not tied to a capture region (e.g. JSON-only).</param>
    /// <param name="sourceTiles">Sparse cell list from <see cref="Selection.SnapshotTiles"/> (payload-relative). Used to build a <see cref="bool"/>[,] snapshot mask on first <see cref="Selection.Define"/>.</param>
    public sealed class Projection : Selection
    {
        private readonly Rectangle _sourceArea;

        /// <summary>Initial sparse snapshot: consumed in <see cref="OnAfterDefine"/> to set <see cref="_inSnapshot"/>; then nulled.</summary>
        private IReadOnlyList<BoundedTile>? _pendingSourceSnapshot;

        /// <summary>True for cells that were present in the initial <see cref="Selection.SnapshotTiles"/>; same width/height as <see cref="Selection.Bounds"/>.</summary>
        private bool[,]? _inSnapshot;

        /// <summary>Row-major list of <see cref="Point"/> in <see cref="_inSnapshot"/>; paste prep walks in this order.</summary>
        private Point[] _walk = Array.Empty<Point>();

        private Payload _payload = Payload.Empty;
        private readonly List<BoundedTile> _targetTiles = [];
        private readonly List<Rectangle> _inventories = [];

        public Projection(
            bool cutSource = false,
            Rectangle? sourceArea = null,
            IReadOnlyList<BoundedTile>? sourceTiles = null)
        {
            _sourceArea = sourceArea ?? Rectangle.Empty;
            _pendingSourceSnapshot = CopySourceSnapshotForProjection(sourceTiles);
            CutSource = cutSource;
        }

        private static IReadOnlyList<BoundedTile>? CopySourceSnapshotForProjection(IReadOnlyList<BoundedTile>? sourceTiles)
        {
            if (sourceTiles is null || sourceTiles.Count == 0)
                return null;

            var list = new List<BoundedTile>(sourceTiles.Count);
            for (int i = 0; i < sourceTiles.Count; i++)
            {
                var t = sourceTiles[i];
                list.Add(new BoundedTile(t.Relative, t.Included));
            }
            return list;
        }

        private static Point[] BuildWalkFromSnapshot(bool[,] snap, int w, int h)
        {
            int count = 0;
            foreach (bool set in snap)
            {
                if (set) count++;
            }

            if (count == 0) return Array.Empty<Point>();
            var walk = new Point[count];
            int n = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (snap[x, y]) walk[n++] = new Point(x, y);
                }
            }

            return walk;
        }

        private sealed class SourceTileView(Projection p) : IReadOnlyList<BoundedTile>
        {
            public int Count => p._walk.Length;

            public BoundedTile this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)p._walk.Length)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    Point rel = p._walk[index];
                    bool inc = p.TryGetTileByRelative(rel, out var t) && t.Included;
                    return new BoundedTile(rel, inc);
                }
            }

            public IEnumerator<BoundedTile> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                    yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private SourceTileView? _sourceView;

        /// <summary>
        /// Optional id tying this projection to an in-flight paste request (e.g. awaiting ack).
        /// Zero when not used.
        /// </summary>
        public int Id { get; internal set; }

        /// <summary>
        /// Owning player index. Projections are owned by the player whose Selection created them.
        /// </summary>
        public int OwnerPlayerIndex { get; internal set; } = -1;

        /// <summary>
        /// When true, paste planning includes a source-area clear pass at the original capture area.
        /// Use <see cref="SetCut"/> or <see cref="ToggleCut"/> so the world overlay stays in sync.
        /// </summary>
        public bool CutSource { get; private set; }

        /// <summary>
        /// True while a paste operation is active for this projection.
        /// Use <see cref="Lock"/> / <see cref="Unlock"/> so the world overlay stays in sync.
        /// </summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// World rectangle that was captured into the payload (live <c>Main.tile</c> under this region
        /// for cut / source clear). Distinct from <see cref="TargetArea"/>.
        /// </summary>
        public Rectangle SourceArea => _sourceArea;

        /// <summary>
        /// World rectangle where the ghost is placed — same as <see cref="Selection.Bounds"/>.
        /// Live target tiles are <c>Main.tile</c> under this region (see <see cref="TryGetTargetTile"/>).
        /// </summary>
        public Rectangle TargetArea => Bounds;

        /// <summary>
        /// True when a payload grid is allocated (width/height &gt; 0).
        /// </summary>
        public bool HasPayload => _payload.IsDefined;

        public Point PayloadSize => _payload.Size;

        /// <summary>Tile snapshot grid and document/world conversion helpers.</summary>
        public Payload Payload => _payload;

        /// <summary>
        /// Snapshot walk for paste: one entry per true cell in the initial 2D snapshot mask; <see cref="BoundedTile.Included"/>
        /// reflects the current <see cref="Selection"/> inclusion mask at that cell.
        /// </summary>
        public IReadOnlyList<BoundedTile> SourceTiles
        {
            get
            {
                if (!IsDefined || _walk.Length == 0)
                {
                    return Array.Empty<BoundedTile>();
                }

                return _sourceView ??= new SourceTileView(this);
            }
        }

        /// <summary>
        /// Target-side walk: same <see cref="Point.Relative"/> as <see cref="SourceTiles"/>, restricted to
        /// in-bounds world targets when this list was last refreshed; refreshed when the projection moves.
        /// </summary>
        public IReadOnlyList<BoundedTile> TargetTiles => _targetTiles;

        /// <summary>
        /// Tile-footprint rectangles of containers detected within reach of this projection's
        /// <see cref="TargetArea"/>. Empty means no nearby containers (paste falls back to player inventory).
        /// Updated by <see cref="ProjectionSystem"/> whenever the projection overlay rebuilds.
        /// </summary>
        public IReadOnlyList<Rectangle> Inventories => _inventories;

        /// <summary>
        /// Replaces <see cref="Inventories"/> with the contents of <paramref name="inventories"/>.
        /// Called from <see cref="ProjectionSystem"/> during overlay rebuild; does not trigger another overlay pass.
        /// </summary>
        internal void SetInventories(List<Rectangle> inventories)
        {
            ArgumentNullException.ThrowIfNull(inventories);
            _inventories.Clear();
            _inventories.AddRange(inventories);
        }

        /// <summary>
        /// Locks this projection (paste in progress). Updates the client projection overlay.
        /// </summary>
        public void Lock()
        {
            if (IsLocked)
                return;
            IsLocked = true;
            UpdateOverlay();
        }

        /// <summary>
        /// Unlocks this projection. Updates the client projection overlay.
        /// </summary>
        public void Unlock()
        {
            if (!IsLocked)
                return;
            IsLocked = false;
            UpdateOverlay();
        }

        /// <summary>
        /// Enables or disables cut-from-source when payload and <see cref="SourceArea"/> allow it.
        /// </summary>
        public void SetCut(bool cutFromSource)
        {
            bool want = cutFromSource && !SourceArea.IsEmpty && HasPayload;
            if (CutSource == want)
                return;
            CutSource = want;
            UpdateOverlay();
        }

        /// <summary>
        /// Toggles cut-from-source when valid; otherwise forces cut off.
        /// </summary>
        public void ToggleCut()
        {
            if (SourceArea.IsEmpty || !HasPayload)
                SetCut(false);
            else
                SetCut(!CutSource);
        }

        /// <summary>
        /// Rebuilds this owner's projection overlay family on the client (no-op on server / invalid owner).
        /// </summary>
        internal void UpdateOverlay()
        {
            if (OwnerPlayerIndex < 0 || OwnerPlayerIndex >= Main.maxPlayers)
                return;
            Player owner = Main.player[OwnerPlayerIndex];
            if (owner is not { active: true })
                return;
            ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(owner);
        }

        /// <summary>
        /// Border color for the projection overlay: amethyst purple when copying, ruby red when
        /// <see cref="CutSource"/> is enabled.
        /// </summary>
        public Color GetOverlayBorderColor()
        {
            return CutSource ? ArchitectVisualPalette.ProjectionCutColor : ArchitectVisualPalette.ProjectionCopyColor;
        }

        /// <summary>
        /// Fills <see cref="Payload"/> from live <c>Main.tile</c> under <see cref="SourceArea"/>.
        /// Relative indices (0..width-1, 0..height-1) are stable when the projection moves.
        /// </summary>
        public void LoadPayloadFromWorld()
        {
            if (_sourceArea.IsEmpty)
            {
                _payload = Payload.Empty;
                CutSource = false;
                RefreshTargetTiles();
                UpdateOverlay();
                if (ASMLog.IsDebugEnabled)
                    ASMLog.Debug(
                        $"Projection payload cleared [LoadPayloadFromWorld: empty SourceArea] id={Id} owner={OwnerPlayerIndex}",
                        "Projection");
                return;
            }

            _payload = Payload.FromWorld(_sourceArea);
            RefreshTargetTiles();
            UpdateOverlay();
        }

        /// <summary>
        /// Replaces the payload grid from a prepared <see cref="Core.Payload"/> (stores an independent copy).
        /// Does not change <see cref="SourceArea"/> or the 2D snapshot walk.
        /// </summary>
        internal void SetPayload(Payload source)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!source.IsDefined)
            {
                _payload = Payload.Empty;
                CutSource = false;
                RefreshTargetTiles();
                UpdateOverlay();
                if (ASMLog.IsDebugEnabled)
                    ASMLog.Debug(
                        $"Projection payload cleared [SetPayload: empty source] id={Id} owner={OwnerPlayerIndex}",
                        "Projection");
                return;
            }

            _payload = Payload.FromClone(source);
            CutSource = CutSource && !_sourceArea.IsEmpty && HasPayload;
            RefreshTargetTiles();
            UpdateOverlay();
        }

        /// <summary>
        /// Live <c>Main.tile</c> under <see cref="TargetArea"/> at <paramref name="relative"/>.
        /// </summary>
        public bool TryGetTargetTile(Point relative, out Tile targetTile)
        {
            if ((uint)relative.X >= (uint)_payload.Width || (uint)relative.Y >= (uint)_payload.Height)
            {
                targetTile = default;
                return false;
            }

            Point world = RelativeToWorld(relative);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                targetTile = default;
                return false;
            }
            targetTile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>
        /// Live <c>Main.tile</c> under <see cref="SourceArea"/> at <paramref name="relative"/> (original capture region).
        /// </summary>
        public bool TryGetSourceTile(Point relative, out Tile sourceTile)
        {
            if (SourceArea.IsEmpty)
            {
                sourceTile = default;
                return false;
            }
            Point world = new(SourceArea.X + relative.X, SourceArea.Y + relative.Y);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                sourceTile = default;
                return false;
            }
            sourceTile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>
        /// Refreshes <see cref="TargetTiles"/> from the snapshot walk and current inclusion when the projection moves.
        /// </summary>
        internal void RefreshTargetTiles()
        {
            _targetTiles.Clear();
            _sourceView = null;

            if (!IsDefined || _walk.Length == 0)
            {
                return;
            }

            for (int i = 0; i < _walk.Length; i++)
            {
                Point rel = _walk[i];
                Point world = RelativeToWorld(rel);
                if (!WorldGen.InWorld(world.X, world.Y))
                {
                    continue;
                }

                bool inc = TryGetTileByRelative(rel, out var t) && t.Included;
                _targetTiles.Add(new BoundedTile(rel, inc));
            }
        }

        /// <summary>
        /// Scans <see cref="Main.chest"/> for entries whose full tile footprint lies within
        /// <see cref="TargetArea"/> plus two tiles outward.
        /// </summary>
        public void RefreshInventories()
        {
            _inventories.Clear();
            Rectangle searchArea = TargetArea;
            searchArea.Inflate(2, 2);

            for (int ci = 0; ci < Main.maxChests; ci++)
            {
                Chest chest = Main.chest[ci];
                if (chest is null || chest.x < 0)
                {
                    continue;
                }

                Tile tile = Main.tile[chest.x, chest.y];
                TileObjectData tileData = TileObjectData.GetTileData(tile);
                int w = tileData?.Width ?? 2;
                int h = tileData?.Height ?? 2;
                Rectangle footprint = new(chest.x, chest.y, w, h);

                if (searchArea.Contains(footprint))
                {
                    _inventories.Add(footprint);
                }
            }
        }

        /// <summary>Moves the ghost so <see cref="TargetArea"/> top-left is <paramref name="targetAreaOrigin"/>.</summary>
        public void MoveTo(Point targetAreaOrigin)
        {
            if (!IsDefined)
            {
                return;
            }

            var delta = new Point(targetAreaOrigin.X - Bounds.X, targetAreaOrigin.Y - Bounds.Y);
            Translate(delta);
            RefreshTargetTiles();
            RefreshInventories();
            UpdateOverlay();
        }

        /// <summary>
        /// Builds the 2D <see cref="_inSnapshot"/> from <see cref="_pendingSourceSnapshot"/>, or preserves overlap
        /// when bounds change without a new pending list.
        /// </summary>
        protected override void OnAfterDefine()
        {
            _sourceView = null;
            if (!IsDefined)
            {
                _inSnapshot = null;
                _walk = Array.Empty<Point>();
                _targetTiles.Clear();
                return;
            }

            int w = Bounds.Width;
            int h = Bounds.Height;
            var newSnap = new bool[w, h];
            if (_pendingSourceSnapshot is { Count: > 0 } pending)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var rel = pending[i].Relative;
                    if ((uint)rel.X < (uint)w && (uint)rel.Y < (uint)h)
                    {
                        newSnap[rel.X, rel.Y] = true;
                    }
                }
                _pendingSourceSnapshot = null;
            }
            else if (_inSnapshot is not null)
            {
                int ow = _inSnapshot.GetLength(0);
                int oh = _inSnapshot.GetLength(1);
                int cx = Math.Min(ow, w);
                int cy = Math.Min(oh, h);
                for (int y = 0; y < cy; y++)
                {
                    for (int x = 0; x < cx; x++)
                    {
                        newSnap[x, y] = _inSnapshot[x, y];
                    }
                }
            }

            _inSnapshot = newSnap;
            _walk = BuildWalkFromSnapshot(_inSnapshot, w, h);
            RefreshTargetTiles();
        }
    }
}
