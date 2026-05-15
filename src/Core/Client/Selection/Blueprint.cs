using System;
using System.Collections;
using System.Collections.Generic;
using ArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Shared bounded tile-mask structure used by client-side stencil data models.
    /// Stores world-space <see cref="Area"/> plus a relative 2D inclusion grid.
    /// </summary>
    public class Blueprint(int owner)
    {
        /// <summary>
        /// A tile at a relative position within a bounded region.
        /// <see cref="Relative"/> is the index into <see cref="Tiles"/>; <see cref="World"/> is the
        /// corresponding world tile (kept in sync when <see cref="Area"/> moves).
        /// </summary>
        public sealed class BlueprintCell(Point relative, Point world, bool included = true)
        {
            public Point Relative { get; } = relative;
            public Point World { get; internal set; } = world;
            public bool Included { get; set; } = included;
        }

        public int Owner { get; } = owner;

        /// <summary>Number of tile cells in bounds; zero if undefined.</summary>
        public int TileCount => IsDefined ? Area.Width * Area.Height : 0;

        /// <summary>
        /// Whether this blueprint currently has non-empty bounds.
        /// </summary>
        public bool IsDefined => !Area.IsEmpty;

        /// <summary>
        /// World-space bounds in tile coordinates.
        /// </summary>
        public Rectangle Area { get; protected set; } = Rectangle.Empty;
        public BlueprintCell[,] Tiles { get; set; } = new BlueprintCell[0, 0];

        /// <summary>Inclusive tile rectangle from two corner tiles (order-independent).</summary>
        public static Rectangle FromPoints(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int w = Math.Max(a.X, b.X) - left + 1;
            int h = Math.Max(a.Y, b.Y) - top + 1;
            return new Rectangle(left, top, w, h);
        }

        public virtual void Define(Rectangle bounds)
        {
            int oldWidth = Area.Width;
            int oldHeight = Area.Height;
            BlueprintCell[,] previous = Tiles;

            Area = bounds;

            if (Area.IsEmpty)
            {
                if (ABPLog.IsDebugEnabled)
                    ABPLog.Debug(
                        $"{GetType().Name}.Define: cleared bounds (empty rect) owner={Owner} hadGrid={previous is not null}",
                        "Blueprint");
                return;
            }

            Tiles = new BlueprintCell[Area.Width, Area.Height];
            for (int y = 0; y < Area.Height; y++)
            {
                for (int x = 0; x < Area.Width; x++)
                {
                    bool included = true;
                    if (previous is not null
                        && oldWidth > 0
                        && oldHeight > 0
                        && (uint)x < (uint)oldWidth
                        && (uint)y < (uint)oldHeight)
                    {
                        included = previous[x, y].Included;
                    }

                    var rel = new Point(x, y);
                    Tiles[x, y] = new BlueprintCell(rel, new Point(Area.X + x, Area.Y + y), included);
                }
            }

            if (ABPLog.IsDebugEnabled)
                ABPLog.Debug(
                    $"{GetType().Name}.Define: owner={Owner} area=({Area.X},{Area.Y}) {Area.Width}x{Area.Height} " +
                    $"prev={oldWidth}x{oldHeight} carriedInclusion={(previous is not null && oldWidth > 0 && oldHeight > 0)}",
                    "Blueprint");
        }

        public virtual void Define(Point pointA, Point pointB)
        {
            Define(FromPoints(pointA, pointB));
        }

        public virtual void Clear()
        {
            bool had = IsDefined;
            Area = Rectangle.Empty;
            Tiles = null;
            if (ABPLog.IsDebugEnabled)
                ABPLog.Debug($"{GetType().Name}.Clear: owner={Owner} hadBounds={had}", "Blueprint");
        }

        public bool TryGetTile(Point world, out BlueprintCell boundedTile)
        {
            if (!TryWorldToRelative(world, out var rel))
            {
                boundedTile = null!;
                return false;
            }

            return TryGetTileByRelative(rel, out boundedTile);
        }

        public bool TryGetTileByRelative(Point relative, out BlueprintCell boundedTile)
        {
            if (!IsDefined
                || Tiles is null
                || (uint)relative.X >= (uint)Area.Width
                || (uint)relative.Y >= (uint)Area.Height)
            {
                boundedTile = null!;
                return false;
            }

            boundedTile = Tiles[relative.X, relative.Y];
            return true;
        }

        /// <summary>
        /// Converts a world position to a relative position inside <see cref="Area"/>.
        /// </summary>
        public bool TryWorldToRelative(Point world, out Point relative)
        {
            relative = new Point(world.X - Area.X, world.Y - Area.Y);
            return Area.Contains(world);
        }

        /// <summary>Payload/area offset: <c>Area.Location + relative</c> (same as each grid cell’s <see cref="BlueprintCell.World"/>).</summary>
        public Point RelativeToWorld(Point relative) => new(Area.X + relative.X, Area.Y + relative.Y);

        /// <summary>
        /// World tile coordinates for cells currently excluded by this blueprint.
        /// </summary>
        public Point[] BuildExcludedTilePoints()
        {
            if (!IsDefined || Tiles is null)
            {
                return [];
            }

            int w = Area.Width;
            int h = Area.Height;
            var points = new List<Point>(w * h / 4);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (Tiles[x, y].Included)
                    {
                        continue;
                    }

                    points.Add(Tiles[x, y].World);
                }
            }

            return points.Count == 0 ? [] : points.ToArray();
        }

        public void Translate(Point delta)
        {
            if (Area.IsEmpty || (delta.X == 0 && delta.Y == 0))
            {
                return;
            }

            Area = new Rectangle(Area.X + delta.X, Area.Y + delta.Y, Area.Width, Area.Height);
            SyncTileWorlds();
            if (ABPLog.IsDebugEnabled)
                ABPLog.Debug(
                    $"{GetType().Name}.Translate: owner={Owner} delta=({delta.X},{delta.Y}) origin=({Area.X},{Area.Y})",
                    "Blueprint");
        }

        private void SyncTileWorlds()
        {
            if (!IsDefined || Tiles is null)
            {
                return;
            }

            for (int y = 0; y < Area.Height; y++)
            {
                for (int x = 0; x < Area.Width; x++)
                {
                    Tiles[x, y].World = new Point(Area.X + x, Area.Y + y);
                }
            }
        }

        /// <summary>
        /// Marks cells in <paramref name="tileRect"/> as included, clamped to <see cref="Area"/>.
        /// </summary>
        public void IncludeTiles(Rectangle tileRect)
        {
            if (Area.IsEmpty)
            {
                return;
            }

            Rectangle r = Rectangle.Intersect(Area, tileRect);
            if (r.IsEmpty)
            {
                return;
            }

            for (int x = r.Left; x < r.Right; x++)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    if (TryGetTileByRelative(new Point(x - Area.X, y - Area.Y), out var t))
                    {
                        t.Included = true;
                    }
                }
            }
        }

        /// <summary>
        /// Marks cells in <paramref name="tileRect"/> as excluded, clamped to <see cref="Area"/>.
        /// </summary>
        public void ExcludeTiles(Rectangle tileRect)
        {
            if (Area.IsEmpty)
            {
                return;
            }

            Rectangle r = Rectangle.Intersect(Area, tileRect);
            if (r.IsEmpty)
            {
                return;
            }

            for (int x = r.Left; x < r.Right; x++)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    if (TryGetTileByRelative(new Point(x - Area.X, y - Area.Y), out var t))
                    {
                        t.Included = false;
                    }
                }
            }
        }
    }
}
