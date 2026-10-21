#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core.TestCases.LayerTests
{
    /// <summary>
    /// Test Fixture chain for layer-classify world columns:
    /// ChamberOrigin → LayerColumnsPlaced → (Identify via LayerIdentifyFixture) → Teardown.
    /// </summary>
    internal static class LayerColumnFixture
    {
        /// <summary>
        /// Join at LayerColumnsPlaced: stamp a solid support and a subject tile one cell above.
        /// </summary>
        public static bool TryJoinSupportSubject(
            Point support,
            ushort supportType,
            ushort subjectType,
            out Point subject,
            out string? error)
        {
            // --- Clear both cells, stamp support, then stamp the subject one tile above.

            subject = new Point(support.X, support.Y - 1);
            error = null;

            // Reject coordinates outside the loaded world.
            if (!WorldGen.InWorld(support.X, support.Y) || !WorldGen.InWorld(subject.X, subject.Y))
            {
                error = $"Tile out of world: support=({support.X},{support.Y}) subject=({subject.X},{subject.Y}).";
                return false;
            }

            ClearTile(support);
            ClearTile(subject);

            // Stamp support directly so classifiers see a known type regardless of PlaceTile quirks.
            Tile supportTile = Main.tile[support.X, support.Y];
            supportTile.HasTile = true;
            supportTile.TileType = supportType;
            supportTile.IsHalfBlock = false;
            supportTile.BlockType = BlockType.Solid;

            // Stamp subject on the cell above the support.
            Tile subjectTile = Main.tile[subject.X, subject.Y];
            subjectTile.HasTile = true;
            subjectTile.TileType = subjectType;
            subjectTile.IsHalfBlock = false;
            subjectTile.BlockType = BlockType.Solid;
            subjectTile.TileFrameX = 0;
            subjectTile.TileFrameY = 0;

            // Verify support stamp landed with the requested type.
            if (!Main.tile[support.X, support.Y].HasTile || Main.tile[support.X, support.Y].TileType != supportType)
            {
                error = $"Failed to stamp support type {supportType} at ({support.X},{support.Y}).";
                return false;
            }

            // Verify subject stamp landed with the requested type.
            if (!Main.tile[subject.X, subject.Y].HasTile || Main.tile[subject.X, subject.Y].TileType != subjectType)
            {
                error = $"Failed to stamp subject type {subjectType} at ({subject.X},{subject.Y}).";
                return false;
            }

            return true;
        }

        /// <summary>Join at LayerColumnsPlaced: stamp a solid stone control tile at the given cell.</summary>
        public static bool TryJoinStoneControl(Point world, out string? error)
        {
            // --- Clear the cell, then stamp solid stone for the Block control column.

            error = null;
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                error = $"Tile out of world: ({world.X},{world.Y}).";
                return false;
            }

            ClearTile(world);

            Tile stoneTile = Main.tile[world.X, world.Y];
            stoneTile.HasTile = true;
            stoneTile.TileType = TileID.Stone;
            stoneTile.IsHalfBlock = false;
            stoneTile.BlockType = BlockType.Solid;

            if (!Main.tile[world.X, world.Y].HasTile || Main.tile[world.X, world.Y].TileType != TileID.Stone)
            {
                error = $"Failed to stamp stone at ({world.X},{world.Y}).";
                return false;
            }

            return true;
        }

        /// <summary>Teardown: clear a single world tile without dropping items.</summary>
        public static void ClearTile(Point tile)
        {
            // --- Kill without item drops when the coordinate is in-world.
            if (WorldGen.InWorld(tile.X, tile.Y))
                WorldGen.KillTile(tile.X, tile.Y, fail: false, effectOnly: false, noItem: true);
        }
    }
}
#endif
