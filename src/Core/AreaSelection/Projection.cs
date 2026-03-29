using System;
using System.Collections.Generic;
using Terraria;
using Microsoft.Xna.Framework;

namespace ArchitectsStencilMod.Core
{
    public enum ProjectionKind
    {
        Copy,
        Cut
    }

    /// <summary>
    /// A movable ghost of a Blueprint that shows where a structure will be built.
    /// </summary>
    public sealed class Projection(ProjectionKind kind) : Blueprint
    {
        public ProjectionKind Kind { get; } = kind;

        /// <summary>
        /// True while a paste operation is active for this projection.
        /// Used to lock dragging/removal and for overlay rendering.
        /// </summary>
        public bool IsLocked { get; internal set; }

        /// <summary>
        /// SourceArea: world-space bounds that were captured into the source payload.
        /// For <see cref="ProjectionKind.Cut"/>, this is the area cleared before placing at the destination.
        /// </summary>
        public Rectangle SourceArea { get; private set; } = Rectangle.Empty;

        /// <summary>
        /// Back-compat wrapper to the canonical SourceArea property.
        /// </summary>
        [Obsolete("Use SourceArea instead.", false)]
        public Rectangle SourceBounds => SourceArea;

        /// <summary>
        /// Owning player index. Projections are owned by the player whose Blueprint created them.
        /// </summary>
        public int OwnerPlayerIndex { get; internal set; } = -1;

        /// <summary>
        /// SourcePayload: the actual tile data snapshot used in a pasting process.
        /// It is captured from SourceArea and indexed by relative tile coordinates.
        /// </summary>
        public bool HasSourcePayload => _sourcePayload is not null && _sourcePayload.Length > 0;

        public Point SourcePayloadSize => HasSourcePayload ? new Point(_sourcePayloadWidth, _sourcePayloadHeight) : Point.Zero;

        private Tile[] _sourcePayload = [];
        private int _sourcePayloadWidth;
        private int _sourcePayloadHeight;


        /// <summary>
        /// Captures the world tiles in <paramref name="sourceBounds"/> into this projection's source payload.
        /// Uses relative tile coordinates (0..width-1, 0..height-1) and does not change when the projection moves.
        /// </summary>
        public void CaptureSourceFromWorld(Rectangle sourceBounds)
        {
            SourceArea = sourceBounds;
            if (sourceBounds.IsEmpty)
            {
                _sourcePayload = [];
                _sourcePayloadWidth = 0;
                _sourcePayloadHeight = 0;
                return;
            }

            _sourcePayloadWidth = sourceBounds.Width;
            _sourcePayloadHeight = sourceBounds.Height;
            _sourcePayload = new Tile[_sourcePayloadWidth * _sourcePayloadHeight];

            for (int x = 0; x < _sourcePayloadWidth; x++)
            {
                for (int y = 0; y < _sourcePayloadHeight; y++)
                {
                    int wx = sourceBounds.X + x;
                    int wy = sourceBounds.Y + y;
                    if (!WorldGen.InWorld(wx, wy))
                    {
                        continue;
                    }

                    // Tile is a map-position view: assigning Main.tile[..] keeps the same backing cell, so Cut's
                    // clear at source empties what we read back for Place. CopyFrom copies between *positions*, so
                    // new Tile()+CopyFrom is invalid off-map. Clone() yields detached data for this buffer.
                    _sourcePayload[y * _sourcePayloadWidth + x] = (Tile)Main.tile[wx, wy].Clone();
                }
            }
        }

        public bool TryGetSourceTile(Point relative, out Tile tile)
        {
            if (!HasSourcePayload)
            {
                tile = default;
                return false;
            }

            if ((uint)relative.X >= (uint)_sourcePayloadWidth || (uint)relative.Y >= (uint)_sourcePayloadHeight)
            {
                tile = default;
                return false;
            }

            tile = _sourcePayload[relative.Y * _sourcePayloadWidth + relative.X];
            return true;
        }


        /// <summary>
        /// Gets the target (live world) tile at a relative position. Fetches from Main.tile at the current destination.
        /// </summary>
        public bool TryGetTargetTile(Point relative, out Tile tile)
        {
            if ((uint)relative.X >= (uint)_sourcePayloadWidth || (uint)relative.Y >= (uint)_sourcePayloadHeight)
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
        /// Gets the source area (original capture) tile at a relative position. Fetches from Main.tile at SourceBounds.
        /// Used by Cut to read what's at the original source location.
        /// </summary>
        public bool TryGetSourceAreaTile(Point relative, out Tile tile)
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

        private IReadOnlyList<Blueprint.BoundedTile> _sourceAreaTiles = [];
        private readonly List<Blueprint.BoundedTile> _targetAreaTiles = [];
        private Dictionary<Point, Blueprint.BoundedTile> _targetByRelative = [];

        /// <summary>
        /// SourceAreaTiles: coordinate data used to reference tiles in SourceArea (world source tiles).
        /// This list is used for selection semantics and local coordinate mapping.
        /// </summary>
        public IReadOnlyList<Blueprint.BoundedTile> SourceAreaTiles => _sourceAreaTiles;

        [Obsolete("Use SourceAreaTiles instead.", false)]
        public IReadOnlyList<Blueprint.BoundedTile> SourceTiles => _sourceAreaTiles;

        /// <summary>
        /// TargetAreaTiles: coordinate data used to reference tiles at the projection destination.
        /// Updated every time the projection moves. Use <see cref="TryGetTargetTile"/> to fetch tile data.
        /// </summary>
        public IReadOnlyList<Blueprint.BoundedTile> TargetAreaTiles => _targetAreaTiles;

        [Obsolete("Use TargetAreaTiles instead.", false)]
        public IReadOnlyList<Blueprint.BoundedTile> TargetTiles => _targetAreaTiles;

        /// <summary>
        /// Sets SourceTiles from a static snapshot. Call once at projection initialization, after
        /// Define, CaptureSourceFromWorld, and selection copy from blueprint.
        /// </summary>
        internal void SetSourceTiles(IReadOnlyList<Blueprint.BoundedTile> snapshot)
        {
            _sourceAreaTiles = snapshot ?? [];
        }

        /// <summary>
        /// Refreshes TargetAreaTiles from SourceAreaTiles (coordinates only). Call when the projection moves.
        /// </summary>
        internal void RefreshTargetTiles()
        {
            _targetAreaTiles.Clear();
            _targetByRelative.Clear();

            if (!IsDefined || _sourceAreaTiles.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _sourceAreaTiles.Count; i++)
            {
                var src = _sourceAreaTiles[i];
                Point world = RelativeToWorld(src.Relative);
                if (!WorldGen.InWorld(world.X, world.Y))
                {
                    continue;
                }

                var bt = new Blueprint.BoundedTile(src.Relative, src.Selected);
                _targetAreaTiles.Add(bt);
                _targetByRelative[src.Relative] = bt;
            }
        }

        /// <summary>
        /// SourceTiles: actual tile references for current tiles within the SourceArea (SourceBounds).
        /// TargetTiles: actual tile coordinates for current tiles within the TargetArea (projection destination).
        /// Populates output with coordinates of tiles under <see cref="SourceBounds"/>. Used by Cut projections.
        /// Use <see cref="TryGetSourceAreaTile"/> to fetch tile data by relative position.
        /// </summary>
        public void GatherSourceAreaWorldTiles(List<Blueprint.BoundedTile> output)
        {
            ArgumentNullException.ThrowIfNull(output);

            output.Clear();
            if (!IsDefined || SourceArea.IsEmpty || _sourceAreaTiles.Count == 0)
            {
                return;
            }

            Point srcTL = SourceArea.Location;

            for (int i = 0; i < _sourceAreaTiles.Count; i++)
            {
                var src = _sourceAreaTiles[i];
                Point srcWorld = new(srcTL.X + src.Relative.X, srcTL.Y + src.Relative.Y);
                if (!WorldGen.InWorld(srcWorld.X, srcWorld.Y))
                {
                    continue;
                }

                output.Add(new Blueprint.BoundedTile(src.Relative, src.Selected));
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
        }
    }
}

