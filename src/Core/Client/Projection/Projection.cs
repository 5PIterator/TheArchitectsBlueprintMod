using System;
using System.Collections.Generic;
using System.Threading;
using TheArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// A movable ghost of a <see cref="Selection"/>: carries the <see cref="Core.Payload">payload</see>
    /// (detached <c>TileData</c> snapshots per cell), aligned with <see cref="SourceArea"/> / <see cref="TargetArea"/>.
    /// <see cref="SourceTiles"/> is a snapshot of the capture region (clone); <see cref="TargetTiles"/> is this projection’s
    /// grid (moves with the ghost) and is where include/exclude is edited after construction.
    /// A projection always retains a selection snapshot; <see cref="SetPayload"/> resizes both target and source footprints
    /// to the payload grid at the current ghost anchor when applying a defined payload.
    /// Fill <see cref="Payload"/> via <see cref="LoadPayloadFromWorld"/> or <see cref="SetPayload"/>.
    /// </summary>
    /// <param name="selection">Selection blueprint used to seed initial target bounds and inclusion mask.</param>
    /// <param name="cutSource">Initial cut-from-source flag (clamped when there is no payload).</param>
    public class Projection : Blueprint
    {
        private static int _nextOverlayInstanceKey;

        private Payload _payload = Payload.Empty;
        private Selection _selection = null!;

        /// <summary>
        /// Per-instance value for <c>OverlaySystem</c> registration keys. <see cref="Id"/> is often 0 for multiple live projections.
        /// </summary>
        internal int OverlayInstanceKey { get; } = Interlocked.Increment(ref _nextOverlayInstanceKey);

        /// <summary>
        /// Builds a projection from a selection snapshot (e.g. server paste shell). Does not apply client cursor/size gates;
        /// use <see cref="ProjectionSystem.NewProjection"/> for in-world copy from the current selection.
        /// </summary>
        public Projection(int owner, Selection selection, bool cutSource = false) : base(owner)
        {
            ArgumentNullException.ThrowIfNull(selection);
            AssignSelection(selection);
        }

        /// <summary>
        /// Optional id tying this projection to an in-flight paste request (e.g. awaiting ack).
        /// Zero when not used.
        /// </summary>
        public int Id { get; internal set; }

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
        /// World rectangle for live <c>Main.tile</c> under the source snapshot (cut / source clear). Kept in lockstep with
        /// <see cref="TargetArea"/> when <see cref="SetPayload"/> applies a defined payload.
        /// </summary>
        public Rectangle SourceArea => _selection.SourceArea;

        public BlueprintCell[,] SourceTiles => _selection.SourceTiles;

        /// <summary>
        /// World rectangle where the ghost is placed — same as <see cref="Blueprint.Area"/>.
        /// Live target tiles are <c>Main.tile</c> under this region (see <see cref="TryGetTargetTile"/>).
        /// </summary>
        public Rectangle TargetArea => Area; // same as Area
        public BlueprintCell[,] TargetTiles => Tiles;

        /// <summary>
        /// True when a payload grid is allocated (width/height &gt; 0).
        /// </summary>
        public bool HasPayload => _payload.IsDefined;

        public Point PayloadSize => _payload.Size;

        /// <summary>Tile snapshot grid and document/world conversion helpers.</summary>
        public Payload Payload => _payload;

        /// <summary>
        /// Tile-footprint rectangles of containers detected within reach of this projection's
        /// <see cref="TargetArea"/>. Empty means no nearby containers; paste may still use the owner
        /// player's inventory when that player is inside the same reach window.
        /// </summary>
        public List<Rectangle> Inventories { get; private set; } = [];

        /// <summary>
        /// Locks this projection (paste in progress). Updates the client projection overlay.
        /// </summary>
        public void Lock()
        {
            if (IsLocked)
                return;
            IsLocked = true;
            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"Projection.Lock: id={Id} owner={Owner}", "Projection");
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
            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"Projection.Unlock: id={Id} owner={Owner}", "Projection");
            UpdateOverlay();
        }

        /// <summary>Moves the ghost so <see cref="TargetArea"/> top-left is <paramref name="targetAreaOrigin"/>.</summary>
        /// <param name="isDragFrame">When true, skips expensive inventory/overlay refresh until <see cref="CommitDragRefresh"/>.</param>
        public void MoveTo(Point targetAreaOrigin, bool isDragFrame = false)
        {
            if (!IsDefined)
                return;

            var delta = new Point(targetAreaOrigin.X - Area.X, targetAreaOrigin.Y - Area.Y);
            if (delta.X == 0 && delta.Y == 0)
                return;

            Translate(delta);
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"Projection.MoveTo: id={Id} owner={Owner} origin=({targetAreaOrigin.X},{targetAreaOrigin.Y}) delta=({delta.X},{delta.Y}) drag={isDragFrame}",
                    "Projection");

            if (isDragFrame)
            {
                UpdateOverlay();
                return;
            }

            RefreshInventories();
            UpdateOverlay();
        }

        /// <summary>Runs deferred work after a COPY-tool drag ends (inventories + overlay).</summary>
        internal void CommitDragRefresh()
        {
            if (!IsDefined)
                return;

            RefreshInventories();
            UpdateOverlay();
        }

        /// <summary>
        /// Enables or disables cut-from-source when payload and <see cref="SourceArea"/> allow it.
        /// </summary>
        public void SetCut(bool cutFromSource)
        {
            bool want = cutFromSource && HasPayload;
            if (CutSource == want)
                return;
            CutSource = want;
            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"Projection.SetCut: id={Id} owner={Owner} cutSource={CutSource} hasPayload={HasPayload}", "Projection");
            UpdateOverlay();
        }

        /// <summary>
        /// Toggles cut-from-source when valid; otherwise forces cut off.
        /// </summary>
        public bool ToggleCut()
        {
            if (!HasPayload)
            {
                SetCut(false);
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug($"Projection.ToggleCut: id={Id} owner={Owner} skipped (no payload)", "Projection");
                return false;
            }

            SetCut(!CutSource);
            return true;
        }

        /// <summary>
        /// Sets the selection snapshot (replaces the frozen source footprint).
        /// </summary>
        internal void AssignSelection(Selection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);
            _selection = selection.Clone();
            Define(_selection.Area);
            // Define seeds every cell as included; copy the selection snapshot mask onto target tiles.
            SyncInclusion();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"Projection.AssignSelection (init): id={Id} owner={Owner} area=({Area.X},{Area.Y}) {Area.Width}x{Area.Height}",
                    "Projection");
        }

        /// <summary>
        /// Applies a prepared payload for paste execution without redefining the frozen source selection footprint.
        /// </summary>
        internal void ApplyPayloadForPasteExecution(Payload source, IReadOnlyList<string>? metadataModsLoaded = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!source.IsDefined)
            {
                _payload = Payload.Empty;
                CutSource = false;
                RefreshTargetTiles();
                return;
            }

            Payload prepared = PayloadCompatibility.Prepare(
                source,
                PayloadPrepareMode.SkipIncompatible,
                metadataModsLoaded,
                out PayloadCompatibilityReport report);

            if (report.IncompatibleCells > 0 && Main.netMode != NetmodeID.Server)
            {
                string msg = $"Payload: {report.IncompatibleCells} of {report.TotalNonEmptyCells} cells skipped (unresolvable tile/wall).";
                if (report.MissingModsFromMetadata.Count > 0)
                    msg += $" Missing mods: {string.Join(", ", report.MissingModsFromMetadata)}.";
                Main.NewText(msg, Microsoft.Xna.Framework.Color.Orange);
            }

            _payload = Payload.FromClone(prepared);
            CutSource = CutSource && HasPayload;

            var newBounds = new Rectangle(Area.X, Area.Y, _payload.Width, _payload.Height);
            Define(newBounds);
            RefreshTargetTiles();
        }

        /// <summary>
        /// Replaces the payload grid from a prepared <see cref="Core.Payload"/> (stores an independent copy).
        /// When <paramref name="source"/> is defined, resizes the projection target and the selection snapshot to the payload
        /// size at the current top-left anchor so source and target footprints stay aligned.
        /// </summary>
        internal void SetPayload(Payload source, IReadOnlyList<string>? metadataModsLoaded = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!source.IsDefined)
            {
                _payload = Payload.Empty;
                CutSource = false;
                RefreshTargetTiles();
                UpdateOverlay();
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug(
                        $"Projection payload cleared [SetPayload: empty source] id={Id} owner={Owner}",
                        "Projection");
                return;
            }

            Payload prepared = PayloadCompatibility.Prepare(
                source,
                PayloadPrepareMode.SkipIncompatible,
                metadataModsLoaded,
                out PayloadCompatibilityReport report);

            if (report.IncompatibleCells > 0 && Main.netMode != NetmodeID.Server)
            {
                string msg = $"Payload: {report.IncompatibleCells} of {report.TotalNonEmptyCells} cells skipped (unresolvable tile/wall).";
                if (report.MissingModsFromMetadata.Count > 0)
                    msg += $" Missing mods: {string.Join(", ", report.MissingModsFromMetadata)}.";
                Main.NewText(msg, Microsoft.Xna.Framework.Color.Orange);
            }

            _payload = Payload.FromClone(prepared);
            CutSource = CutSource && HasPayload;

            var newBounds = new Rectangle(Area.X, Area.Y, _payload.Width, _payload.Height);

            Selection resized = _selection.Clone();
            resized.Define(newBounds);
            _selection = resized;

            Define(newBounds);
            RefreshTargetTiles();
            UpdateOverlay();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"Projection.SetPayload: applied id={Id} owner={Owner} payload={_payload.Width}x{_payload.Height} " +
                    $"targetOrigin=({Area.X},{Area.Y}) cutSource={CutSource}",
                    "Projection");
        }

        /// <summary>
        /// Fills <see cref="Payload"/> from live <c>Main.tile</c> under <see cref="SourceArea"/>.
        /// Relative indices (0..width-1, 0..height-1) are stable when the projection moves.
        /// </summary>
        public void LoadPayloadFromWorld()
        {
            _payload = Payload.FromWorld(SourceArea);
            RefreshTargetTiles();
            UpdateOverlay();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"Projection.LoadPayloadFromWorld: id={Id} owner={Owner} source=({SourceArea.X},{SourceArea.Y}) " +
                    $"{SourceArea.Width}x{SourceArea.Height} payloadDefined={_payload.IsDefined} payload={_payload.Width}x{_payload.Height}",
                    "Projection");
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

            Point world = TryGetTileByRelative(relative, out BlueprintCell? cell) && cell is not null
                ? cell.World
                : RelativeToWorld(relative);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                targetTile = default;
                return false;
            }
            targetTile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>
        /// Live <c>Main.tile</c> under <see cref="SourceArea"/> at <paramref name="relative"/> (source snapshot region).
        /// </summary>
        public bool TryGetSourceTile(Point relative, out Tile sourceTile)
        {
            if (SourceTiles is null
                || (uint)relative.X >= (uint)SourceArea.Width
                || (uint)relative.Y >= (uint)SourceArea.Height)
            {
                sourceTile = default;
                return false;
            }

            Point world = SourceTiles[relative.X, relative.Y].World;
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                sourceTile = default;
                return false;
            }
            sourceTile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>
        /// Rebuilds <see cref="TargetTiles"/> (this blueprint’s <see cref="Blueprint.Tiles"/>) for the current
        /// <see cref="Blueprint.Area"/>, preserving <see cref="BlueprintCell.Included"/> from the previous grid when
        /// dimensions allow; otherwise copies inclusion once from the selection snapshot captured in the constructor.
        /// Payload and inclusion are decoupled: <see cref="LoadPayloadFromWorld"/> does not change which cells are included.
        /// </summary>
        private void RefreshTargetTiles()
        {
            if (Area.IsEmpty)
            {
                return;
            }

            int w = Area.Width;
            int h = Area.Height;
            var previousTiles = Tiles;
            Tiles = new BlueprintCell[w, h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Point rel = new(x, y);
                    Point world = RelativeToWorld(rel);
                    if (!WorldGen.InWorld(world.X, world.Y))
                    {
                        continue;
                    }

                    bool included;
                    if (previousTiles is not null
                        && (uint)x < (uint)previousTiles.GetLength(0)
                        && (uint)y < (uint)previousTiles.GetLength(1))
                    {
                        included = previousTiles[x, y].Included;
                    }
                    else if (_selection.Tiles is not null
                        && w == _selection.Area.Width
                        && h == _selection.Area.Height
                        && (uint)x < (uint)_selection.Area.Width
                        && (uint)y < (uint)_selection.Area.Height)
                    {
                        included = _selection.Tiles[x, y].Included;
                    }
                    else
                    {
                        included = true;
                    }

                    Tiles[x, y] = new BlueprintCell(rel, world, included);
                }
            }
        }

        /// <summary>
        /// Scans <see cref="Main.chest"/> for entries whose full tile footprint lies in the
        /// <see cref="Inventory.ReachTiles"/> ring around <see cref="TargetArea"/>, not inside it.
        /// </summary>
        public void RefreshInventories()
        {
            // --- Rebuild nearby chest footprints from authoritative world chest state.

            Inventories.Clear();
            Rectangle targetArea = TargetArea;

            for (int ci = 0; ci < Main.maxChests; ci++)
            {
                Chest chest = Main.chest[ci];
                if (chest is null || chest.x < 0)
                    continue;

                Tile tile = Main.tile[chest.x, chest.y];
                TileObjectData tileData = TileObjectData.GetTileData(tile);
                int w = tileData?.Width ?? 2;
                int h = tileData?.Height ?? 2;
                Rectangle footprint = new(chest.x, chest.y, w, h);

                if (InventoryManager.IsChestInInventoryReachRing(footprint, targetArea))
                    Inventories.Add(footprint);
            }
        }

        /// <summary>
        /// Synchronizes inclusion from the selection snapshot to the projection tiles.
        /// </summary>
        public void SyncInclusion()
        {
            if (_selection.Tiles is null || Tiles is null)
            {
                return;
            }

            foreach (var tile in Tiles)
            {
                tile.Included = _selection.Tiles[tile.Relative.X, tile.Relative.Y].Included;
            }

            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"Projection.SyncInclusion: id={Id} owner={Owner} grid={Area.Width}x{Area.Height}", "Projection");
        }

        /// <summary>
        /// Rebuilds this owner's projection overlay family on the client (no-op on server / invalid owner).
        /// </summary>
        internal void UpdateOverlay()
        {
            if (Owner < 0 || Owner >= Main.maxPlayers)
                return;
            Player owner = Main.player[Owner];
            if (owner is not { active: true })
                return;
            ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(owner);
        }
    }
}
