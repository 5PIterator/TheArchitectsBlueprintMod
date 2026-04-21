using System;
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
    /// A movable ghost of a <see cref="Blueprint"/>: carries the <see cref="Core.Payload">payload</see>
    /// (detached <c>TileData</c> snapshots per cell), aligned with <see cref="SourceArea"/> / <see cref="TargetArea"/>,
    /// and <see cref="SourceTiles"/> / <see cref="TargetTiles"/> selection masks.
    /// <see cref="SourceArea"/> and <see cref="SourceTiles"/> are fixed when the projection is constructed;
    /// fill <see cref="Payload"/> afterward via <see cref="LoadPayloadFromWorld"/> or <see cref="SetPayload"/>.
    /// </summary>
    /// <param name="cutSource">Initial cut-from-source flag (still clamped when there is no payload or no source area).</param>
    /// <param name="sourceArea">World rectangle for world capture / source clear; omit or empty when not tied to a capture region (e.g. JSON-only).</param>
    /// <param name="sourceTiles">Selection mask in payload-relative coordinates; typically from <see cref="Blueprint.SnapshotTiles"/> or an import document.</param>
    public sealed class Projection(
        bool cutSource = false,
        Rectangle? sourceArea = null,
        IReadOnlyList<Blueprint.BoundedTile>? sourceTiles = null) : Blueprint
    {
        private readonly Rectangle _sourceArea = sourceArea ?? Rectangle.Empty;
        private readonly List<BoundedTile> _sourceTiles = CloneSourceTiles(sourceTiles);

        private Payload _payload = Payload.Empty;
        private readonly List<BoundedTile> _targetTiles = [];
        private readonly List<Rectangle> _inventories = [];

        private static List<BoundedTile> CloneSourceTiles(IReadOnlyList<BoundedTile>? sourceTiles)
        {
            if (sourceTiles is null || sourceTiles.Count == 0)
            {
                return [];
            }

            var list = new List<BoundedTile>(sourceTiles.Count);
            for (int i = 0; i < sourceTiles.Count; i++)
            {
                var sourceTile = sourceTiles[i];
                list.Add(new BoundedTile(sourceTile.Relative, sourceTile.Selected));
            }

            return list;
        }

        /// <summary>
        /// Optional id tying this projection to an in-flight paste request (e.g. awaiting ack).
        /// Zero when not used.
        /// </summary>
        public int Id { get; internal set; }

        /// <summary>
        /// Owning player index. Projections are owned by the player whose Blueprint created them.
        /// </summary>
        public int OwnerPlayerIndex { get; internal set; } = -1;

        /// <summary>
        /// When true, paste planning includes a source-area clear pass at the original capture area.
        /// Use <see cref="SetCut"/> or <see cref="ToggleCut"/> so the world overlay stays in sync.
        /// </summary>
        public bool CutSource { get; private set; } = cutSource;

        /// <summary>
        /// True while a paste operation is active for this projection.
        /// Use <see cref="Lock"/> / <see cref="Unlock"/> so the world overlay stays in sync.
        /// </summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// World rectangle that was captured into the payload (live <c>Main.tile</c> under this region
        /// for cut / source-clear). Distinct from <see cref="TargetArea"/>.
        /// </summary>
        public Rectangle SourceArea => _sourceArea;

        /// <summary>
        /// World rectangle where the ghost is placed — same as <see cref="Blueprint.Bounds"/>.
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
        /// Selection mask in payload-relative coordinates (same grid as <see cref="Blueprint.Bounds"/>).
        /// </summary>
        public IReadOnlyList<BoundedTile> SourceTiles => _sourceTiles;

        /// <summary>
        /// Same relatives as <see cref="SourceTiles"/>, indexed against <see cref="TargetArea"/>; refreshed when the projection moves.
        /// Use <see cref="TryGetTargetTile"/> for live <c>Main.tile</c> references in the target area.
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
        /// Border color for the projection overlay: green when not cutting source, red when
        /// <see cref="CutSource"/> is enabled.
        /// </summary>
        public Color GetOverlayBorderColor()
        {
            return CutSource ? Color.Red : Color.LimeGreen;
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
            //LogPayloadSnapshot("LoadPayloadFromWorld", _sourceArea);
            RefreshTargetTiles();
            UpdateOverlay();
        }

        /// <summary>
        /// Replaces the payload grid from a prepared <see cref="Core.Payload"/> (stores an independent copy).
        /// Does not change <see cref="SourceArea"/> or <see cref="SourceTiles"/>.
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
            //LogPayloadSnapshot("SetPayload", _sourceArea, importEntryCount: null, selectedCount: SourceTiles.Count);
        }

        /// <summary>Debug: non-null / block-tile counts and bounds when a payload grid is first populated.</summary>
        //private void LogPayloadSnapshot(string origin, Rectangle sourceRect, int? importEntryCount = null, int? selectedCount = null)
        //{
        //    if (!ASMLog.IsDebugEnabled)
        //        return;

        //    (int nonNull, int blockTiles) = _payload.GetCellStatistics();

        //    string extra = importEntryCount.HasValue
        //        ? $" importEntries={importEntryCount} selectedTiles={selectedCount ?? 0}"
        //        : selectedCount.HasValue
        //            ? $" selectedTiles={selectedCount.Value}"
        //            : string.Empty;

        //    ASMLog.Debug(
        //        $"Projection payload created [{origin}] id={Id} owner={OwnerPlayerIndex} grid={_payload.Width}x{_payload.Height} nonNull={nonNull} blockTiles={blockTiles} sourceRect=({sourceRect.X},{sourceRect.Y},{sourceRect.Width},{sourceRect.Height}) targetBounds=({Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}){extra}",
        //        "Projection");
        //}

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
        /// Refreshes <see cref="TargetTiles"/> from <see cref="SourceTiles"/> when the projection moves.
        /// </summary>
        internal void RefreshTargetTiles()
        {
            _targetTiles.Clear();

            if (!IsDefined || _sourceTiles.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _sourceTiles.Count; i++)
            {
                var sourceBoundedTile = _sourceTiles[i];
                Point world = RelativeToWorld(sourceBoundedTile.Relative);
                if (!WorldGen.InWorld(world.X, world.Y))
                {
                    continue;
                }

                var targetBoundedTile = new BoundedTile(sourceBoundedTile.Relative, sourceBoundedTile.Selected);
                _targetTiles.Add(targetBoundedTile);
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
    }
}
