using System;
using System.Collections;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
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

            /// <summary>
            /// Guard classification for this cell. Ignored when <see cref="Included"/> is false.
            /// Refreshed on included cells by <see cref="BlueprintGuard.RefreshProtection"/>.
            /// </summary>
            public bool Protected { get; set; }
        }

        public int Owner { get; } = owner;

        /// <summary>Number of tile cells in bounds; zero if undefined.</summary>
        public int TileCount => IsDefined ? Area.Width * Area.Height : 0;

        /// <summary>
        /// Whether this blueprint currently has non-empty bounds.
        /// </summary>
        public bool IsDefined => !Area.IsEmpty;

        /// <summary>
        /// Monotonic counter bumped whenever inclusion (or grid reshape that carries inclusion) changes.
        /// </summary>
        public int InclusionVersion { get; private set; }

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
            if (bounds.IsEmpty)
            {
                if (!IsDefined)
                    return;

                bool hadGrid = Tiles is not null;
                Area = bounds;
                Tiles = null;
                BumpInclusionVersion();
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug(
                        $"{GetType().Name}.Define: cleared bounds (empty rect) owner={Owner} hadGrid={hadGrid}",
                        "Blueprint");
                return;
            }

            if (bounds == Area && Tiles is not null)
                return;

            int oldWidth = Area.Width;
            int oldHeight = Area.Height;
            BlueprintCell[,] previous = Tiles;

            Area = bounds;

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

            BumpInclusionVersion();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
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
            if (had)
                BumpInclusionVersion();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"{GetType().Name}.Clear: owner={Owner} hadBounds={had}", "Blueprint");
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
        /// World-space axis-aligned rectangles covering every currently excluded cell.
        /// </summary>
        public Rectangle[] BuildExcludedTileRects()
        {
            // --- Greedy row-runs of excluded cells, then merge identical spans vertically.

            if (!IsDefined || Tiles is null)
                return [];

            int w = Area.Width;
            int h = Area.Height;
            var open = new List<(int X, int Width, int Y, int Height)>();
            var nextOpen = new List<(int X, int Width, int Y, int Height)>();
            var closed = new List<Rectangle>();
            var rowRuns = new List<(int Start, int Width)>();

            for (int y = 0; y < h; y++)
            {
                // Collect contiguous excluded spans on this relative row.
                rowRuns.Clear();
                int runStart = -1;
                for (int x = 0; x <= w; x++)
                {
                    bool excluded = x < w && !Tiles[x, y].Included;
                    if (excluded)
                    {
                        if (runStart < 0)
                            runStart = x;
                        continue;
                    }

                    if (runStart < 0)
                        continue;

                    rowRuns.Add((runStart, x - runStart));
                    runStart = -1;
                }

                // Extend open rects that match a current-row span; close the rest.
                nextOpen.Clear();
                for (int i = 0; i < open.Count; i++)
                {
                    (int x, int width, int openY, int height) = open[i];
                    int match = -1;
                    for (int r = 0; r < rowRuns.Count; r++)
                    {
                        if (rowRuns[r].Start == x && rowRuns[r].Width == width)
                        {
                            match = r;
                            break;
                        }
                    }

                    if (match >= 0)
                    {
                        nextOpen.Add((x, width, openY, height + 1));
                        rowRuns.RemoveAt(match);
                    }
                    else
                    {
                        closed.Add(new Rectangle(Area.X + x, Area.Y + openY, width, height));
                    }
                }

                // Unmatched current-row spans become new open rectangles.
                for (int r = 0; r < rowRuns.Count; r++)
                    nextOpen.Add((rowRuns[r].Start, rowRuns[r].Width, y, 1));

                open.Clear();
                open.AddRange(nextOpen);
            }

            for (int i = 0; i < open.Count; i++)
            {
                (int x, int width, int y, int height) = open[i];
                closed.Add(new Rectangle(Area.X + x, Area.Y + y, width, height));
            }

            return closed.Count == 0 ? [] : closed.ToArray();
        }

        /// <summary>Increments <see cref="InclusionVersion"/> after inclusion or grid reshape mutations.</summary>
        public void BumpInclusionVersion()
        {
            // --- Monotonic bump so preview/map caches can detect inclusion changes.
            InclusionVersion++;
        }

        public void Translate(Point delta)
        {
            if (Area.IsEmpty || (delta.X == 0 && delta.Y == 0))
            {
                return;
            }

            Area = new Rectangle(Area.X + delta.X, Area.Y + delta.Y, Area.Width, Area.Height);
            SyncTileWorlds();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
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

            bool changed = false;
            for (int x = r.Left; x < r.Right; x++)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    if (TryGetTileByRelative(new Point(x - Area.X, y - Area.Y), out var t) && !t.Included)
                    {
                        t.Included = true;
                        changed = true;
                    }
                }
            }

            if (changed)
                BumpInclusionVersion();
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

            bool changed = false;
            for (int x = r.Left; x < r.Right; x++)
            {
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    if (TryGetTileByRelative(new Point(x - Area.X, y - Area.Y), out var t) && t.Included)
                    {
                        t.Included = false;
                        changed = true;
                    }
                }
            }

            if (changed)
                BumpInclusionVersion();
        }
    }
}
