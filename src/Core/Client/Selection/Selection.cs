using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Represents the primary region in the world that The Architect's Stencil operates on.
    /// For now this is a rectangle plus a tile inclusion mask.
    ///
    /// Selection semantics:
    /// - The selection owns a per-tile list for the current <see cref="Bounds"/>.
    /// - Each tile has a mutable <see cref="BoundedTile.Included"/> flag.
    /// </summary>
    public class Selection
    {
        /// <summary>
        /// A tile at a relative position within a bounded region.
        /// Relative is the canonical position; world position is Bounds.Location + Relative.
        /// Tile data is not stored; it is gathered from coordinates when requested.
        /// </summary>
        public sealed class BoundedTile(Point relative, bool included = true)
        {
            public Point Relative { get; } = relative;
            public bool Included { get; set; } = included;
        }

        /// <summary>
        /// World-space bounds of the selection, in tile coordinates.
        /// </summary>
        public Rectangle Bounds { get; private set; } = Rectangle.Empty;

        /// <summary>
        /// Whether this selection has been defined.
        /// </summary>
        public bool IsDefined => !Bounds.IsEmpty;

        /// <summary>
        /// All tiles inside <see cref="Bounds"/>.
        /// </summary>
        public IReadOnlyList<BoundedTile> Tiles => _tiles;

        private readonly List<BoundedTile> _tiles = [];

        /// <summary>
        /// Top-left of the selection in tile coordinates.
        /// </summary>
        public Vector2 TilePosition => new(Bounds.X, Bounds.Y);

        /// <summary>
        /// Size of the selection in tiles.
        /// </summary>
        public Vector2 TileSize => new(Bounds.Width, Bounds.Height);

        public void Define(Rectangle bounds)
        {
            int oldWidth = Bounds.Width;
            int oldHeight = Bounds.Height;
            BoundedTile[]? previous = _tiles.Count > 0 ? [.. _tiles] : null;

            Bounds = bounds;

            _tiles.Clear();

            if (Bounds.IsEmpty)
            {
                return;
            }

            // Rebuild tile list for new bounds, preserving inclusion state where possible.
            for (int y = 0; y < Bounds.Height; y++)
            {
                for (int x = 0; x < Bounds.Width; x++)
                {
                    bool included = true;
                    if (previous is not null
                        && oldWidth > 0
                        && oldHeight > 0
                        && (uint)x < (uint)oldWidth
                        && (uint)y < (uint)oldHeight)
                    {
                        included = previous[y * oldWidth + x].Included;
                    }

                    _tiles.Add(new BoundedTile(new Point(x, y), included));
                }
            }
        }

        public void Clear()
        {
            Bounds = Rectangle.Empty;
            _tiles.Clear();
        }

        public bool IsTileIncluded(Point world)
        {
            return TryWorldToRelative(world, out var rel) && TryGetTileByRelative(rel, out var t) && t.Included;
        }

        public bool TryGetTile(Point world, out BoundedTile boundedTile)
        {
            if (!TryWorldToRelative(world, out var rel))
            {
                boundedTile = null!;
                return false;
            }
            return TryGetTileByRelative(rel, out boundedTile);
        }

        public bool TryGetTileByRelative(Point relative, out BoundedTile boundedTile)
        {
            if (!TryGetTileIndex(relative, out int index))
            {
                boundedTile = null!;
                return false;
            }

            boundedTile = _tiles[index];
            return true;
        }

        private bool TryGetTileIndex(Point relative, out int index)
        {
            if (!IsDefined
                || (uint)relative.X >= (uint)Bounds.Width
                || (uint)relative.Y >= (uint)Bounds.Height)
            {
                index = 0;
                return false;
            }

            index = relative.Y * Bounds.Width + relative.X;
            return true;
        }

        /// <summary>
        /// Converts a world position to the relative position within <see cref="Bounds"/>.
        /// Returns false if the world position is outside the selection.
        /// </summary>
        public bool TryWorldToRelative(Point world, out Point relative)
        {
            relative = new Point(world.X - Bounds.X, world.Y - Bounds.Y);
            return Bounds.Contains(world);
        }

        /// <summary>
        /// Converts a relative position to the world position within <see cref="Bounds"/>.
        /// </summary>
        public Point RelativeToWorld(Point relative)
        {
            return new Point(Bounds.X + relative.X, Bounds.Y + relative.Y);
        }

        /// <summary>
        /// Creates a rectangle from two corner points (inclusive of both endpoints).
        /// </summary>
        public static Rectangle FromPoints(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        /// <summary>
        /// World tile coordinates for tiles in this selection that are not <see cref="BoundedTile.Included"/>.
        /// Used for exclusion-mask overlays (selection / projection).
        /// </summary>
        public Point[] BuildExcludedTilePoints()
        {
            IReadOnlyList<BoundedTile> tiles = Tiles;
            if (tiles.Count == 0) return [];
            Point boundsOrigin = Bounds.Location;
            var points = new List<Point>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t.Included) continue;
                points.Add(new Point(boundsOrigin.X + t.Relative.X, boundsOrigin.Y + t.Relative.Y));
            }
            return points.Count == 0 ? [] : points.ToArray();
        }

        public void Translate(Point delta)
        {
            if (Bounds.IsEmpty)
            {
                return;
            }

            if (delta.X == 0 && delta.Y == 0)
            {
                return;
            }

            Bounds = new Rectangle(Bounds.X + delta.X, Bounds.Y + delta.Y, Bounds.Width, Bounds.Height);
        }

        /// <summary>
        /// Marks all tiles in the given rectangle as included (clamped to <see cref="Bounds"/>).
        /// </summary>
        public void IncludeTiles(Rectangle tileRect)
        {
            if (Bounds.IsEmpty)
            {
                return;
            }

            Rectangle r = Rectangle.Intersect(Bounds, tileRect);
            if (r.IsEmpty)
            {
                return;
            }

            for (int x = r.Left; x < r.Right; x++)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    if (TryGetTileByRelative(new Point(x - Bounds.X, y - Bounds.Y), out var t))
                    {
                        t.Included = true;
                    }
                }
            }
        }

        /// <summary>
        /// Marks all tiles in the given rectangle as excluded (clamped to <see cref="Bounds"/>).
        /// </summary>
        public void ExcludeTiles(Rectangle tileRect)
        {
            if (Bounds.IsEmpty)
            {
                return;
            }

            Rectangle r = Rectangle.Intersect(Bounds, tileRect);
            if (r.IsEmpty)
            {
                return;
            }

            for (int x = r.Left; x < r.Right; x++)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    if (TryGetTileByRelative(new Point(x - Bounds.X, y - Bounds.Y), out var t))
                    {
                        t.Included = false;
                    }
                }
            }
        }

        /// <summary>
        /// Snapshot of included tile coordinates from this selection.
        /// Only includes tiles where getSourceTile returns a value (filters by source availability).
        /// Used when constructing a <see cref="Projection"/> from a live selection (before <see cref="Projection.LoadPayloadFromWorld"/>).
        /// </summary>
        public IReadOnlyList<BoundedTile> SnapshotTiles(Func<Point, Tile?> getSourceTile)
        {
            ArgumentNullException.ThrowIfNull(getSourceTile);

            var result = new List<BoundedTile>();
            if (!IsDefined)
            {
                return result;
            }

            for (int i = 0; i < Tiles.Count; i++)
            {
                var t = Tiles[i];
                if (!t.Included)
                {
                    continue;
                }

                if (!getSourceTile(t.Relative).HasValue)
                {
                    continue;
                }

                result.Add(new BoundedTile(t.Relative, included: true));
            }
            return result;
        }

        public Rectangle GetWorldPixelBounds(int tileSize = 16)
        {
            return new Rectangle(
                Bounds.X * tileSize,
                Bounds.Y * tileSize,
                Bounds.Width * tileSize,
                Bounds.Height * tileSize
            );
        }

        public Rectangle GetScreenPixelBounds(Vector2 screenPosition, int tileSize = 16)
        {
            var world = GetWorldPixelBounds(tileSize);
            world.Offset(-(int)screenPosition.X, -(int)screenPosition.Y);
            return world;
        }
    }
}
