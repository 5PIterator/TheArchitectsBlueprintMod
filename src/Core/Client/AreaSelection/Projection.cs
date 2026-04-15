using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Core.Client.PayloadImport;
using ArchitectsStencilMod.Debug;
using ArchitectsStencilMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// A movable ghost of a <see cref="Blueprint"/>: carries the <see cref="Payload">payload</see>
    /// (layer facets per cell), aligned with <see cref="SourceArea"/> / <see cref="TargetArea"/>,
    /// and <see cref="SourceTiles"/> / <see cref="TargetTiles"/> selection masks.
    /// </summary>
    public sealed class Projection(bool cutSource = false) : Blueprint
    {
        /// <summary>
        /// One sparse cell in the payload grid: relative index and optional payload data (null = empty cell).
        /// </summary>
        internal readonly struct PayloadEntry(Point relative, PayloadFacets? payloadData)
        {
            public Point Relative { get; } = relative;
            public PayloadFacets? PayloadData { get; } = payloadData;
        }

        private PayloadFacets?[] _payloadData = [];
        private int _payloadWidth;
        private int _payloadHeight;
        private IReadOnlyList<BoundedTile> _sourceTiles = [];
        private readonly List<BoundedTile> _targetTiles = [];

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
        public Rectangle SourceArea { get; private set; } = Rectangle.Empty;

        /// <summary>
        /// World rectangle where the ghost is placed — same as <see cref="Blueprint.Bounds"/>.
        /// Live target tiles are <c>Main.tile</c> under this region (see <see cref="TryGetTargetTile"/>).
        /// </summary>
        public Rectangle TargetArea => Bounds;

        /// <summary>
        /// True when a payload grid is allocated (width/height &gt; 0).
        /// </summary>
        public bool HasPayload => _payloadData is not null && _payloadData.Length > 0 && _payloadWidth > 0 && _payloadHeight > 0;

        public Point PayloadSize => HasPayload ? new Point(_payloadWidth, _payloadHeight) : Point.Zero;

        /// <summary>
        /// Selection mask in payload-relative coordinates: which cells participate, aligned with <see cref="SourceArea"/>.
        /// </summary>
        public IReadOnlyList<BoundedTile> SourceTiles => _sourceTiles;

        /// <summary>
        /// Same relatives as <see cref="SourceTiles"/>, indexed against <see cref="TargetArea"/>; refreshed when the projection moves.
        /// Use <see cref="TryGetTargetTile"/> for live <c>Main.tile</c> references in the target area.
        /// </summary>
        public IReadOnlyList<BoundedTile> TargetTiles => _targetTiles;

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
        /// Loads the payload from live world tiles in <paramref name="sourceBounds"/> as payload data.
        /// Relative indices (0..width-1, 0..height-1) are stable when the projection moves.
        /// </summary>
        public void LoadPayloadFromWorld(Rectangle sourceBounds)
        {
            SourceArea = sourceBounds;
            if (sourceBounds.IsEmpty)
            {
                _payloadData = [];
                _payloadWidth = 0;
                _payloadHeight = 0;
                return;
            }

            _payloadWidth = sourceBounds.Width;
            _payloadHeight = sourceBounds.Height;
            _payloadData = new PayloadFacets?[_payloadWidth * _payloadHeight];

            for (int x = 0; x < _payloadWidth; x++)
            {
                for (int y = 0; y < _payloadHeight; y++)
                {
                    int wx = sourceBounds.X + x;
                    int wy = sourceBounds.Y + y;
                    if (!WorldGen.InWorld(wx, wy))
                    {
                        continue;
                    }

                    Tile live = Main.tile[wx, wy];
                    PayloadFacets? split = PayloadToTile.SplitFromTile(live);
                    _payloadData[y * _payloadWidth + x] = split;

                    // #region agent log
                    if (_payloadWidth * _payloadHeight <= 64)
                    {
                        AgentNdjsonLog.Write(
                            "H1",
                            "Projection.LoadPayloadFromWorld",
                            "capture_cell",
                            new
                            {
                                rx = x,
                                ry = y,
                                wx,
                                wy,
                                liveHas = live.HasTile,
                                liveType = live.HasTile ? live.TileType : (ushort?)null,
                                facetBlockType = split?.Block?.TileType,
                                facetHasTile = split?.Block?.HasTile,
                            });
                    }
                    // #endregion
                }
            }
        }

        /// <summary>
        /// Loads the payload from imported data (JSON, network, etc.): sparse payload data cells and selection mask.
        /// <paramref name="sourceArea"/> is only used when <paramref name="cutSource"/> is true: world
        /// rectangle to clear when cutting from source. Omit for JSON and other imports with no capture region.
        /// </summary>
        internal void LoadPayloadFromImport(
            IReadOnlyList<PayloadEntry>? payload,
            int payloadWidth,
            int payloadHeight,
            IReadOnlyList<BoundedTile>? selectedTiles,
            bool cutSource = false,
            Rectangle? sourceArea = null)
        {
            SourceArea = cutSource && sourceArea is { IsEmpty: false } area ? area : Rectangle.Empty;

            if (payloadWidth <= 0 || payloadHeight <= 0)
            {
                _payloadData = [];
                _payloadWidth = 0;
                _payloadHeight = 0;
                _sourceTiles = [];
                SourceArea = Rectangle.Empty;
                CutSource = false;
                RefreshTargetTiles();
                UpdateOverlay();
                return;
            }

            _payloadWidth = payloadWidth;
            _payloadHeight = payloadHeight;
            _payloadData = new PayloadFacets?[payloadWidth * payloadHeight];

            if (payload is not null)
            {
                for (int i = 0; i < payload.Count; i++)
                {
                    var entry = payload[i];
                    Point relative = entry.Relative;
                    if ((uint)relative.X >= (uint)_payloadWidth || (uint)relative.Y >= (uint)_payloadHeight)
                    {
                        continue;
                    }

                    _payloadData[relative.Y * _payloadWidth + relative.X] = entry.PayloadData;
                }
            }

            if (selectedTiles is null || selectedTiles.Count == 0)
            {
                _sourceTiles = [];
            }
            else
            {
                var snapshot = new List<BoundedTile>(selectedTiles.Count);
                for (int i = 0; i < selectedTiles.Count; i++)
                {
                    var t = selectedTiles[i];
                    snapshot.Add(new BoundedTile(t.Relative, t.Selected));
                }
                _sourceTiles = snapshot;
            }

            CutSource = cutSource && !SourceArea.IsEmpty && HasPayload;
            RefreshTargetTiles();
            UpdateOverlay();
        }

        /// <summary>
        /// Payload data at <paramref name="relative"/> in the payload grid (not live world tiles).
        /// </summary>
        public bool TryGetPayloadData(Point relative, out PayloadFacets? payloadData)
        {
            if (!HasPayload)
            {
                payloadData = null;
                return false;
            }

            if ((uint)relative.X >= (uint)_payloadWidth || (uint)relative.Y >= (uint)_payloadHeight)
            {
                payloadData = null;
                return false;
            }

            payloadData = _payloadData[relative.Y * _payloadWidth + relative.X];
            return true;
        }

        /// <summary>
        /// <see cref="PayloadToTile.MergeToTile"/> for the cell at <paramref name="relative"/> — a
        /// <see cref="Tile"/> with no map position until pasted.
        /// </summary>
        public bool TryGetPayloadTile(Point relative, out Tile payloadTile)
        {
            if (!TryGetPayloadData(relative, out PayloadFacets? payloadData))
            {
                payloadTile = PayloadToTile.MergeToTile(null);
                return false;
            }

            payloadTile = PayloadToTile.MergeToTile(payloadData);
            return true;
        }

        /// <summary>
        /// Live <c>Main.tile</c> under <see cref="TargetArea"/> at <paramref name="relative"/>.
        /// </summary>
        public bool TryGetTargetTile(Point relative, out Tile tile)
        {
            if ((uint)relative.X >= (uint)_payloadWidth || (uint)relative.Y >= (uint)_payloadHeight)
            {
                tile = default;
                return false;
            }

            Point world = RelativeToWorld(relative);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                tile = default;
                return false;
            }
            tile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>
        /// Live <c>Main.tile</c> under <see cref="SourceArea"/> at <paramref name="relative"/> (original capture region).
        /// </summary>
        public bool TryGetSourceTile(Point relative, out Tile tile)
        {
            if (SourceArea.IsEmpty)
            {
                tile = default;
                return false;
            }
            Point world = new(SourceArea.X + relative.X, SourceArea.Y + relative.Y);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                tile = default;
                return false;
            }
            tile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>
        /// Sets <see cref="SourceTiles"/> from a static snapshot after capture / blueprint selection copy.
        /// </summary>
        internal void SetSourceTiles(IReadOnlyList<BoundedTile> snapshot)
        {
            _sourceTiles = snapshot ?? [];
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
                var src = _sourceTiles[i];
                Point world = RelativeToWorld(src.Relative);
                if (!WorldGen.InWorld(world.X, world.Y))
                {
                    continue;
                }

                var bt = new BoundedTile(src.Relative, src.Selected);
                _targetTiles.Add(bt);
            }
        }

        public void MoveTo(Point origin)
        {
            if (!IsDefined)
            {
                return;
            }

            var delta = new Point(origin.X - Bounds.X, origin.Y - Bounds.Y);
            Translate(delta);
            RefreshTargetTiles();

            UpdateOverlay();
        }
    }
}
