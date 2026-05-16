#if DEBUG
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Saved world tiles for a rectangle; <see cref="Restore"/> puts the world back.</summary>
    internal sealed class WorldTileRegionSnapshot
    {
        private readonly List<(int X, int Y, bool HasTile, int TileType, int WallType)> _cells = [];

        internal static WorldTileRegionSnapshot Capture(Rectangle tileArea)
        {
            var snapshot = new WorldTileRegionSnapshot();
            for (int y = tileArea.Y; y < tileArea.Bottom; y++)
            {
                for (int x = tileArea.X; x < tileArea.Right; x++)
                {
                    if (!WorldGen.InWorld(x, y))
                        continue;
                    Tile tile = Main.tile[x, y];
                    snapshot._cells.Add((x, y, tile.HasTile, tile.TileType, tile.WallType));
                }
            }

            return snapshot;
        }

        internal void Restore()
        {
            foreach ((int x, int y, bool hasTile, int tileType, int wallType) in _cells)
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

    /// <summary>Captures one or more regions and restores them on dispose (reverse order).</summary>
    internal sealed class WorldTileSnapshotScope : System.IDisposable
    {
        private readonly List<WorldTileRegionSnapshot> _regions = [];
        private readonly List<Rectangle> _capturedBounds = [];

        internal void Capture(Rectangle tileArea)
        {
            if (tileArea.Width <= 0 || tileArea.Height <= 0)
                return;

            foreach (Rectangle existing in _capturedBounds)
            {
                if (existing == tileArea)
                    return;
            }

            _capturedBounds.Add(tileArea);
            _regions.Add(WorldTileRegionSnapshot.Capture(tileArea));
        }

        public void Dispose()
        {
            for (int i = _regions.Count - 1; i >= 0; i--)
                _regions[i].Restore();
            _regions.Clear();
        }
    }
}
#endif
