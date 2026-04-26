using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ObjectData;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// A movable ghost of a <see cref="Selection"/>: carries the <see cref="Core.Payload">payload</see>
    /// (detached <c>TileData</c> snapshots per cell), aligned with <see cref="SourceArea"/> / <see cref="TargetArea"/>,
    /// <see cref="SourceTiles"/> is the original capture region (fixed clone); <see cref="TargetTiles"/> is this projection’s
    /// grid (moves with the ghost) and is the only place where include/exclude is stored after construction.
    /// <see cref="SourceArea"/> and the initial capture bounds are fixed when the projection is constructed;
    /// fill <see cref="Payload"/> afterward via <see cref="LoadPayloadFromWorld"/> or <see cref="SetPayload"/>.
    /// </summary>
    /// <param name="selection">Selection blueprint used to seed initial target bounds and inclusion mask.</param>
    /// <param name="cutSource">Initial cut-from-source flag (still clamped when there is no payload or no source area).</param>
    /// <param name="sourceArea">World rectangle for world capture / source clear; omit or empty when not tied to a capture region (e.g. JSON-only).</param>
    /// <param name="pendingSnapshotOverride">Optional sparse snapshot override (payload-relative) used to build the initial source walk.</param>
    public class Projection : Blueprint
    {
        private Payload _payload = Payload.Empty;
        private readonly Selection _selection;

        /// <summary>
        /// Builds a projection from a selection snapshot (e.g. server paste shell). Does not apply client cursor/size gates;
        /// use <see cref="FromSelection"/> for in-world copy from the current selection.
        /// </summary>
        public Projection(Selection selection, bool cutSource = false) : base(selection.Owner)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (!selection.IsDefined)
                throw new Exception("Selection is not defined");
            if (selection.Owner < 0 || selection.Owner >= Main.maxPlayers)
                throw new Exception("Selection owner is invalid");



            _selection = selection.Clone();
            CutSource = cutSource;
            Define(_selection.Area);
            SyncInclusion();
            RefreshTargetTiles();
        }

        private void SyncInclusion()
        {
            if (!IsDefined || _selection.Tiles is null || Tiles is null)
            {
                return;
            }

            foreach (var tile in Tiles)
            {
                tile.Included = _selection.Tiles[tile.Relative.X, tile.Relative.Y].Included;
            }
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
        /// World rectangle that was captured into the payload (live <c>Main.tile</c> under this region
        /// for cut / source clear). Distinct from <see cref="TargetArea"/>.
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
        /// <see cref="TargetArea"/>. Empty means no nearby containers (paste falls back to player inventory).
        /// Updated by <see cref="ProjectionSystem"/> whenever the projection overlay rebuilds.
        /// </summary>
        public List<Rectangle> Inventories { get; private set; } = [];

        /// <summary>
        /// Replaces <see cref="Inventories"/> with the contents of <paramref name="inventories"/>.
        /// Called from <see cref="ProjectionSystem"/> during overlay rebuild; does not trigger another overlay pass.
        /// </summary>
        internal void SetInventories(List<Rectangle> inventories)
        {
            ArgumentNullException.ThrowIfNull(inventories);
            Inventories.Clear();
            Inventories.AddRange(inventories);
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
            if (Owner < 0 || Owner >= Main.maxPlayers)
                return;
            Player owner = Main.player[Owner];
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
            if (SourceArea.IsEmpty)
            {
                _payload = Payload.Empty;
                CutSource = false;
                RefreshTargetTiles();
                UpdateOverlay();
                if (ASMLog.IsDebugEnabled)
                    ASMLog.Debug(
                        $"Projection payload cleared [LoadPayloadFromWorld: empty SourceArea] id={Id} owner={Owner}",
                        "Projection");
                return;
            }

            _payload = Payload.FromWorld(SourceArea);
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
                        $"Projection payload cleared [SetPayload: empty source] id={Id} owner={Owner}",
                        "Projection");
                return;
            }

            _payload = Payload.FromClone(source);
            CutSource = CutSource && !SourceArea.IsEmpty && HasPayload;
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
        /// Live <c>Main.tile</c> under <see cref="SourceArea"/> at <paramref name="relative"/> (original capture region).
        /// </summary>
        public bool TryGetSourceTile(Point relative, out Tile sourceTile)
        {
            if (SourceArea.IsEmpty || SourceTiles is null
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
        /// Scans <see cref="Main.chest"/> for entries whose full tile footprint lies within
        /// <see cref="TargetArea"/> plus two tiles outward.
        /// </summary>
        public void RefreshInventories()
        {
            Inventories.Clear();
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
                    Inventories.Add(footprint);
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

            var delta = new Point(targetAreaOrigin.X - Area.X, targetAreaOrigin.Y - Area.Y);
            Translate(delta);
            RefreshTargetTiles();
            RefreshInventories();
            UpdateOverlay();
        }
    }
}
