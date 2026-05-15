using System;
using System.Collections.Generic;
using ArchitectsBlueprintMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Represents the primary region in the world that The Architect's Stencil operates on.
    /// </summary>
    public class Selection(int owner) : Blueprint(owner)
    {
        public Rectangle SourceArea => Area;
        public BlueprintCell[,] SourceTiles => Tiles;

        /// <summary>Independent copy of bounds and per-cell inclusion; safe to edit the original selection afterward.</summary>
        public Selection Clone()
        {
            var copy = new Selection(Owner) { Area = Area };
            if (Area.IsEmpty || Tiles is null)
            {
                copy.Tiles = new BlueprintCell[0, 0];
                if (ABPLog.IsDebugEnabled)
                    ABPLog.Debug($"Selection.Clone: owner={Owner} -> empty copy (no grid)", "Selection");
                return copy;
            }

            int w = Area.Width;
            int h = Area.Height;
            var grid = new BlueprintCell[w, h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var t = Tiles[x, y];
                    grid[x, y] = new BlueprintCell(t.Relative, t.World, t.Included);
                }
            }

            copy.Tiles = grid;
            if (ABPLog.IsDebugEnabled)
                ABPLog.Debug(
                    $"Selection.Clone: owner={Owner} -> copy area=({copy.Area.X},{copy.Area.Y}) {copy.Area.Width}x{copy.Area.Height}",
                    "Selection");
            return copy;
        }

        public Rectangle GetWorldPixelBounds(int tileSize = 16)
        {
            return new Rectangle(
                Area.X * tileSize,
                Area.Y * tileSize,
                Area.Width * tileSize,
                Area.Height * tileSize
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
