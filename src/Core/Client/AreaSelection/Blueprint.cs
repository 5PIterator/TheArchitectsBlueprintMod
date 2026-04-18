using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Represents the primary region in the world that The Architect's Stencil operates on.
    /// For now this is a rectangle plus a tile selection mask.
    ///
    /// Selection semantics:
    /// - The blueprint owns a per-tile list for the current <see cref="Bounds"/>.
    /// - Each tile has a mutable <see cref="BoundedTile.Selected"/> flag.
    /// </summary>
    public class Blueprint
    {
        /// <summary>
        /// A tile at a relative position within a bounded region.
        /// Relative is the canonical position; world position is Bounds.Location + Relative.
        /// Tile data is not stored; it is gathered from coordinates when requested.
        /// </summary>
        public sealed class BoundedTile(Point relative, bool selected = true)
        {
            public Point Relative { get; } = relative;
            public bool Selected { get; set; } = selected;
        }

        /// <summary>
        /// World-space bounds of the blueprint, in tile coordinates.
        /// </summary>
        public Rectangle Bounds { get; private set; } = Rectangle.Empty;

        /// <summary>
        /// Whether this blueprint has been defined.
        /// </summary>
        public bool IsDefined => !Bounds.IsEmpty;

        /// <summary>
        /// All tiles inside <see cref="Bounds"/>.
        /// </summary>
        public IReadOnlyList<BoundedTile> Tiles => _tiles;

        private readonly List<BoundedTile> _tiles = [];

        /// <summary>
        /// Top-left of the blueprint in tile coordinates.
        /// </summary>
        public Vector2 TilePosition => new(Bounds.X, Bounds.Y);

        /// <summary>
        /// Size of the blueprint in tiles.
        /// </summary>
        public Vector2 TileSize => new(Bounds.Width, Bounds.Height);

        public void Define(Rectangle bounds)
        {
            int oldWidth = Bounds.Width;
            int oldHeight = Bounds.Height;
            BoundedTile[]? previous = _tiles.Count > 0 ? _tiles.ToArray() : null;

            Bounds = bounds;

            _tiles.Clear();

            if (Bounds.IsEmpty)
            {
                return;
            }

            // Rebuild tile list for new bounds, preserving selection state where possible.
            for (int y = 0; y < Bounds.Height; y++)
            {
                for (int x = 0; x < Bounds.Width; x++)
                {
                    bool selected = true;
                    if (previous is not null
                        && oldWidth > 0
                        && oldHeight > 0
                        && (uint)x < (uint)oldWidth
                        && (uint)y < (uint)oldHeight)
                    {
                        selected = previous[y * oldWidth + x].Selected;
                    }

                    _tiles.Add(new BoundedTile(new Point(x, y), selected));
                }
            }
        }

        public void Clear()
        {
            Bounds = Rectangle.Empty;
            _tiles.Clear();
        }

        public bool IsTileSelected(Point world)
        {
            return TryWorldToRelative(world, out var rel) && TryGetTileByRelative(rel, out var t) && t.Selected;
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
        /// Returns false if the world position is outside the blueprint.
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
        /// World tile coordinates for tiles in this blueprint that are not <see cref="BoundedTile.Selected"/>.
        /// Used for selection-mask overlays (blueprint / projection).
        /// </summary>
        public Point[] BuildUnselectedTilePoints()
        {
            IReadOnlyList<BoundedTile> tiles = Tiles;
            if (tiles.Count == 0) return [];
            Point boundsOrigin = Bounds.Location;
            var points = new List<Point>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t.Selected) continue;
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
        /// Marks all tiles in the given rectangle as selected (clamped to <see cref="Bounds"/>).
        /// </summary>
        public void SelectTiles(Rectangle tileRect)
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
                        t.Selected = true;
                    }
                }
            }
        }

        /// <summary>
        /// Marks all tiles in the given rectangle as deselected (clamped to <see cref="Bounds"/>).
        /// </summary>
        public void DeselectTiles(Rectangle tileRect)
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
                        t.Selected = false;
                    }
                }
            }
        }

        /// <summary>
        /// Snapshot of selected tile coordinates from this blueprint.
        /// Only includes tiles where getSourceTile returns a value (filters by source availability).
        /// Used when constructing a <see cref="Projection"/> from a live blueprint (before <see cref="Projection.LoadPayloadFromWorld"/>).
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
                if (!t.Selected)
                {
                    continue;
                }

                if (!getSourceTile(t.Relative).HasValue)
                {
                    continue;
                }

                result.Add(new BoundedTile(t.Relative, selected: true));
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

