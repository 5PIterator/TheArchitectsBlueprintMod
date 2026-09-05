using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>
    /// Decomposes tile point sets into axis-aligned rectangles with exact tile coverage.
    /// </summary>
    internal static class TileOverlayRects
    {
        /// <summary>
        /// Returns tile-space rectangles that cover exactly the given tiles.
        /// </summary>
        public static Rectangle[] Decompose(ReadOnlySpan<Point> tiles)
        {
            // --- Greedily peel maximal rectangles from a tile set until empty.

            if (tiles.Length == 0)
                return [];

            if (tiles.Length == 1)
                return [new Rectangle(tiles[0].X, tiles[0].Y, 1, 1)];

            var remaining = new HashSet<Point>(tiles.Length);
            for (int i = 0; i < tiles.Length; i++)
                remaining.Add(tiles[i]);

            var rects = new List<Rectangle>();

            while (remaining.Count > 0)
            {
                // Pick the top-leftmost remaining tile as the rectangle seed.
                Point seed = FindTopLeftTile(remaining);

                int x0 = seed.X;
                int y0 = seed.Y;

                // Extend right while every tile in the row segment is still present.
                int width = 1;
                while (remaining.Contains(new Point(x0 + width, y0)))
                    width++;

                // Extend down while each row below contains the full horizontal span.
                int height = 1;
                while (RowSegmentPresent(remaining, x0, y0 + height, width))
                    height++;

                // Remove every tile covered by this rectangle.
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dx = 0; dx < width; dx++)
                        remaining.Remove(new Point(x0 + dx, y0 + dy));
                }

                rects.Add(new Rectangle(x0, y0, width, height));
            }

            return rects.ToArray();
        }

        /// <summary>
        /// Finds the tile with the smallest Y, then smallest X.
        /// </summary>
        private static Point FindTopLeftTile(HashSet<Point> tiles)
        {
            // --- Scan the set once and keep the lexicographically smallest (Y, X).

            Point best = default;
            bool first = true;

            foreach (Point p in tiles)
            {
                if (first || p.Y < best.Y || (p.Y == best.Y && p.X < best.X))
                {
                    best = p;
                    first = false;
                }
            }

            return best;
        }

        /// <summary>
        /// Returns true when every tile in [x0, x0 + width) on row y exists in the set.
        /// </summary>
        private static bool RowSegmentPresent(HashSet<Point> tiles, int x0, int y, int width)
        {
            // --- Verify each column in the candidate row segment is still uncovered.

            for (int dx = 0; dx < width; dx++)
            {
                if (!tiles.Contains(new Point(x0 + dx, y)))
                    return false;
            }

            return true;
        }
    }
}
