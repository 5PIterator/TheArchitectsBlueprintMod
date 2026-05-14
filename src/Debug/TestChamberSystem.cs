using System.Collections.Generic;
using ArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Isolated world-tile sandbox used by compilation discovery (demonite ring + single-cell scratch).
    /// </summary>
    public sealed class TestChamberSystem : ModSystem
    {
        public static TestChamberSystem Instance => ModContent.GetInstance<TestChamberSystem>();

        /// <summary>Neighborhood offsets around the focal tile (excluding the center).</summary>
        public static readonly (int Dx, int Dy)[] RingOffsets =
        [
            (-1, -1), (0, -1), (1, -1),
            (-1, 0), (1, 0),
            (-1, 1), (0, 1), (1, 1),
        ];

        public readonly struct CellSnapshot(bool hasTile, int tileType, int wallType)
        {
            public bool HasTile { get; } = hasTile;
            public int TileType { get; } = tileType;
            public int WallType { get; } = wallType;
        }

        /// <summary>Saved ring cell positions and prior tile/wall state for <see cref="TeardownRing"/>.</summary>
        public sealed class RingSavedState : List<(int x, int y, bool hasTile, int tileType, int wallType)>
        {
        }

        /// <summary>Snapshot one cell (tile presence, types, wall).</summary>
        public CellSnapshot SaveCell(int x, int y)
        {
            var t = Main.tile[x, y];
            return new CellSnapshot(t.HasTile, t.TileType, t.WallType);
        }

        /// <summary>Clear tile and wall, then place <paramref name="tileType"/> with style 0.</summary>
        public void ClearAndSetTile(int x, int y, int tileType, int style = 0)
        {
            SuppressTileDrop.KillTile(x, y);
            WorldGen.KillWall(x, y, fail: false);
            WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true, plr: -1, style: style);
        }

        /// <summary>Restore a cell from <see cref="SaveCell"/>.</summary>
        public void RestoreCell(int x, int y, CellSnapshot saved)
        {
            if (!WorldGen.InWorld(x, y))
                return;
            SuppressTileDrop.KillTile(x, y);
            WorldGen.KillWall(x, y, fail: false);
            if (saved.WallType != WallID.None)
                WorldGen.PlaceWall(x, y, (ushort)saved.WallType, mute: true);
            if (saved.HasTile)
                WorldGen.PlaceTile(x, y, saved.TileType, mute: true, forced: true, plr: -1, style: 0);
        }

        /// <summary>Clears and demonite-bricks the 8-neighbor ring around <paramref name="centerX"/>/<paramref name="centerY"/>.</summary>
        public RingSavedState SetupRing(int centerX, int centerY)
        {
            var saved = new RingSavedState();
            foreach ((int dx, int dy) in RingOffsets)
            {
                int x = centerX + dx;
                int y = centerY + dy;
                if (!WorldGen.InWorld(x, y))
                    continue;

                var t = Main.tile[x, y];
                saved.Add((x, y, t.HasTile, t.TileType, t.WallType));
                SuppressTileDrop.KillTile(x, y);
                WorldGen.KillWall(x, y, fail: false);
                WorldGen.PlaceTile(x, y, TileID.DemoniteBrick, mute: true, forced: true, plr: -1, style: 0);
            }

            return saved;
        }

        /// <summary>Restores tiles saved by <see cref="SetupRing"/>.</summary>
        public void TeardownRing(int centerX, int centerY, RingSavedState? saved)
        {
            if (saved is null)
                return;

            foreach ((int x, int y, bool hasTile, int tileType, int wallType) in saved)
            {
                if (!WorldGen.InWorld(x, y))
                    continue;

                SuppressTileDrop.KillTile(x, y);
                WorldGen.KillWall(x, y, fail: false);
                if (wallType != WallID.None)
                    WorldGen.PlaceWall(x, y, (ushort)wallType, mute: true);
                if (hasTile)
                    WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true, plr: -1, style: 0);
            }
        }
    }
}
