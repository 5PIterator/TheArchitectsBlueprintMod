using System;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Debug
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

        /// <summary>Saved scaffold cells for <see cref="RestoreAnchorScaffold"/>.</summary>
        public sealed class ScaffoldSavedState : List<(int X, int Y, CellSnapshot Snapshot)>
        {
        }

        /// <summary>Layout signature so similar TileObjectData footprints can share one installed scaffold.</summary>
        public readonly struct ScaffoldKey : IEquatable<ScaffoldKey>
        {
            public int Width { get; }
            public int Height { get; }
            public int OriginX { get; }
            public int OriginY { get; }
            public bool NeedsWall { get; }
            public bool NeedsBottom { get; }
            public bool NeedsTop { get; }
            public bool NeedsLeft { get; }
            public bool NeedsRight { get; }
            public int SupportTileType { get; }

            public ScaffoldKey(
                int width,
                int height,
                int originX,
                int originY,
                bool needsWall,
                bool needsBottom,
                bool needsTop,
                bool needsLeft,
                bool needsRight,
                int supportTileType)
            {
                Width = width;
                Height = height;
                OriginX = originX;
                OriginY = originY;
                NeedsWall = needsWall;
                NeedsBottom = needsBottom;
                NeedsTop = needsTop;
                NeedsLeft = needsLeft;
                NeedsRight = needsRight;
                SupportTileType = supportTileType;
            }

            /// <summary>Builds a scaffold key from TileObjectData for <paramref name="tileType"/>/<paramref name="style"/>.</summary>
            public static ScaffoldKey FromTile(int tileType, int style)
            {
                // --- Encode footprint, support sides, and the concrete support tile CanPlace will accept.

                TileObjectData? data = TileObjectData.GetTileData(tileType, style)
                    ?? TileObjectData.GetTileData(tileType, 0);

                int width = data?.Width ?? 1;
                int height = data?.Height ?? 1;
                int originX = data?.Origin.X ?? 0;
                int originY = data?.Origin.Y ?? 0;
                bool needsWall = data is not null && data.AnchorWall;
                bool needsBottom = data is not null && data.AnchorBottom != default;
                bool needsTop = data is not null && data.AnchorTop != default;
                bool needsLeft = data is not null && data.AnchorLeft != default;
                bool needsRight = data is not null && data.AnchorRight != default;

                // AnchorValidTiles-only objects still need a neighbor below the footprint.
                if (data is { AnchorValidTiles: { Length: > 0 } }
                    && !needsBottom && !needsTop && !needsLeft && !needsRight && !needsWall)
                {
                    needsBottom = true;
                }

                int supportTileType = ResolveSupportTileType(data);

                return new ScaffoldKey(
                    width,
                    height,
                    originX,
                    originY,
                    needsWall,
                    needsBottom,
                    needsTop,
                    needsLeft,
                    needsRight,
                    supportTileType);
            }

            /// <summary>Picks the first AnchorValidTiles entry when present; otherwise DemoniteBrick.</summary>
            public static int ResolveSupportTileType(TileObjectData? data)
            {
                // --- Specific anchors (grass, tables, platforms) reject generic solid brick.

                if (data?.AnchorValidTiles is { Length: > 0 } valid)
                {
                    int candidate = valid[0];
                    if (candidate > 0 && candidate < TileLoader.TileCount)
                        return candidate;
                }

                return TileID.DemoniteBrick;
            }

            public bool Equals(ScaffoldKey other) =>
                Width == other.Width
                && Height == other.Height
                && OriginX == other.OriginX
                && OriginY == other.OriginY
                && NeedsWall == other.NeedsWall
                && NeedsBottom == other.NeedsBottom
                && NeedsTop == other.NeedsTop
                && NeedsLeft == other.NeedsLeft
                && NeedsRight == other.NeedsRight
                && SupportTileType == other.SupportTileType;

            public override bool Equals(object? obj) => obj is ScaffoldKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(
                    Width,
                    Height,
                    OriginX,
                    OriginY,
                    NeedsWall,
                    NeedsBottom,
                    HashCode.Combine(NeedsTop, NeedsLeft, NeedsRight, SupportTileType));
        }

        /// <summary>
        /// Keeps one installed scaffold alive across CanPlace checks that share the same layout key.
        /// </summary>
        public sealed class FrameScaffoldCache
        {
            private readonly TestChamberSystem _chamber;
            private readonly int _originX;
            private readonly int _originY;
            private ScaffoldKey? _activeKey;
            private ScaffoldSavedState? _activeSaved;

            public FrameScaffoldCache(TestChamberSystem chamber, int originX, int originY)
            {
                _chamber = chamber;
                _originX = originX;
                _originY = originY;
            }

            /// <summary>Ensures a scaffold matching <paramref name="tileType"/>/<paramref name="style"/> is installed.</summary>
            public void Ensure(int tileType, int style)
            {
                // --- Rebuild only when the footprint/anchor layout changes.

                ScaffoldKey key = ScaffoldKey.FromTile(tileType, style);
                if (_activeKey is ScaffoldKey active && active.Equals(key) && _activeSaved is not null)
                    return;

                Clear();
                _activeSaved = _chamber.SetupAnchorScaffold(_originX, _originY, tileType, style);
                _activeKey = key;
            }

            /// <summary>Restores any active scaffold back to the pre-scaffold world.</summary>
            public void Clear()
            {
                // --- Drop the cached install so the next Ensure builds fresh.

                if (_activeSaved is not null)
                    _chamber.RestoreAnchorScaffold(_activeSaved);

                _activeSaved = null;
                _activeKey = null;
            }
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
            SuppressTileSound.Run(() =>
            {
                SuppressTileDrop.KillTile(x, y);
                SuppressTileDrop.KillWall(x, y);
                WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true, plr: -1, style: style);
            });
        }

        /// <summary>Restore a cell from <see cref="SaveCell"/>.</summary>
        public void RestoreCell(int x, int y, CellSnapshot saved)
        {
            if (!WorldGen.InWorld(x, y))
                return;

            SuppressTileSound.Run(() =>
            {
                SuppressTileDrop.KillTile(x, y);
                SuppressTileDrop.KillWall(x, y);
                if (saved.WallType != WallID.None)
                    WorldGen.PlaceWall(x, y, (ushort)saved.WallType, mute: true);
                if (saved.HasTile)
                    WorldGen.PlaceTile(x, y, saved.TileType, mute: true, forced: true, plr: -1, style: 0);
            });
        }

        /// <summary>
        /// Clears the PlaceObject footprint at <paramref name="originX"/>/<paramref name="originY"/> and
        /// installs minimal wall/solid support for TileObject.CanPlace checks.
        /// </summary>
        public ScaffoldSavedState SetupAnchorScaffold(int originX, int originY, int tileType, int style)
        {
            // --- Snapshot touched cells, clear the footprint, then place wall/solid supports from TileObjectData.

            var saved = new ScaffoldSavedState();
            ScaffoldKey key = ScaffoldKey.FromTile(tileType, style);
            int left = originX - key.OriginX;
            int top = originY - key.OriginY;

            SuppressTileSound.Run(() =>
            {
                // Clear every footprint cell so CanPlace sees an empty object area.
                for (int dx = 0; dx < key.Width; dx++)
                {
                    for (int dy = 0; dy < key.Height; dy++)
                        SnapshotClear(left + dx, top + dy, saved);
                }

                // Wall-anchored objects need a backing wall on the footprint.
                if (key.NeedsWall)
                {
                    for (int dx = 0; dx < key.Width; dx++)
                    {
                        for (int dy = 0; dy < key.Height; dy++)
                        {
                            int x = left + dx;
                            int y = top + dy;
                            if (!WorldGen.InWorld(x, y))
                                continue;
                            EnsureSnapshotted(x, y, saved);
                            if (Main.tile[x, y].WallType == WallID.None)
                                WorldGen.PlaceWall(x, y, WallID.Wood, mute: true);
                        }
                    }
                }

                // Directional supports around the footprint edge (use AnchorValidTiles when required).
                int supportTile = key.SupportTileType;
                if (key.NeedsBottom)
                {
                    for (int dx = 0; dx < key.Width; dx++)
                        EnsureSolidSupport(left + dx, top + key.Height, supportTile, saved);
                }

                if (key.NeedsTop)
                {
                    for (int dx = 0; dx < key.Width; dx++)
                        EnsureSolidSupport(left + dx, top - 1, supportTile, saved);
                }

                if (key.NeedsLeft)
                {
                    for (int dy = 0; dy < key.Height; dy++)
                        EnsureSolidSupport(left - 1, top + dy, supportTile, saved);
                }

                if (key.NeedsRight)
                {
                    for (int dy = 0; dy < key.Height; dy++)
                        EnsureSolidSupport(left + key.Width, top + dy, supportTile, saved);
                }
            });

            return saved;
        }

        /// <summary>Restores cells touched by <see cref="SetupAnchorScaffold"/> (reverse order).</summary>
        public void RestoreAnchorScaffold(ScaffoldSavedState? saved)
        {
            // --- Restore last-touched first so nested support cells unwind cleanly.

            if (saved is null)
                return;

            SuppressTileSound.Run(() =>
            {
                for (int i = saved.Count - 1; i >= 0; i--)
                {
                    (int x, int y, CellSnapshot snapshot) = saved[i];
                    if (!WorldGen.InWorld(x, y))
                        continue;

                    SuppressTileDrop.KillTile(x, y);
                    SuppressTileDrop.KillWall(x, y);
                    if (snapshot.WallType != WallID.None)
                        WorldGen.PlaceWall(x, y, (ushort)snapshot.WallType, mute: true);
                    if (snapshot.HasTile)
                        WorldGen.PlaceTile(x, y, snapshot.TileType, mute: true, forced: true, plr: -1, style: 0);
                }
            });
        }

        /// <summary>Clears and demonite-bricks the 8-neighbor ring around <paramref name="centerX"/>/<paramref name="centerY"/>.</summary>
        public RingSavedState SetupRing(int centerX, int centerY)
        {
            var saved = new RingSavedState();
            SuppressTileSound.Run(() =>
            {
                foreach ((int dx, int dy) in RingOffsets)
                {
                    int x = centerX + dx;
                    int y = centerY + dy;
                    if (!WorldGen.InWorld(x, y))
                        continue;

                    var t = Main.tile[x, y];
                    saved.Add((x, y, t.HasTile, t.TileType, t.WallType));
                    SuppressTileDrop.KillTile(x, y);
                    SuppressTileDrop.KillWall(x, y);
                    WorldGen.PlaceTile(x, y, TileID.DemoniteBrick, mute: true, forced: true, plr: -1, style: 0);
                }
            });

            return saved;
        }

        /// <summary>Restores tiles saved by <see cref="SetupRing"/>.</summary>
        public void TeardownRing(int centerX, int centerY, RingSavedState? saved)
        {
            if (saved is null)
                return;

            SuppressTileSound.Run(() =>
            {
                foreach ((int x, int y, bool hasTile, int tileType, int wallType) in saved)
                {
                    if (!WorldGen.InWorld(x, y))
                        continue;

                    SuppressTileDrop.KillTile(x, y);
                    SuppressTileDrop.KillWall(x, y);
                    if (wallType != WallID.None)
                        WorldGen.PlaceWall(x, y, (ushort)wallType, mute: true);
                    if (hasTile)
                        WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true, plr: -1, style: 0);
                }
            });
        }

        /// <summary>Snapshots a cell once, then clears tile and wall.</summary>
        private void SnapshotClear(int x, int y, ScaffoldSavedState saved)
        {
            // --- Record prior state before emptying the cell for CanPlace.

            if (!WorldGen.InWorld(x, y))
                return;

            EnsureSnapshotted(x, y, saved);
            SuppressTileDrop.KillTile(x, y);
            SuppressTileDrop.KillWall(x, y);
        }

        /// <summary>Snapshots a cell once, then forces a single-cell support tile of <paramref name="supportTileType"/>.</summary>
        private void EnsureSolidSupport(int x, int y, int supportTileType, ScaffoldSavedState saved)
        {
            // --- Write one cell of the anchor-legal type (avoids multi-tile PlaceTile for tables/platforms).

            if (!WorldGen.InWorld(x, y))
                return;

            EnsureSnapshotted(x, y, saved);
            SuppressTileDrop.KillTile(x, y);
            SuppressTileDrop.KillWall(x, y);

            // CanPlace only checks neighbor TileType against AnchorValidTiles; a one-cell stub is enough.
            Tile tile = Main.tile[x, y];
            tile.ResetToType((ushort)supportTileType);
        }

        /// <summary>Records the cell snapshot if this scaffold has not touched it yet.</summary>
        private void EnsureSnapshotted(int x, int y, ScaffoldSavedState saved)
        {
            // --- First write wins so RestoreAnchorScaffold returns to the pre-scaffold world.

            for (int i = 0; i < saved.Count; i++)
            {
                if (saved[i].X == x && saved[i].Y == y)
                    return;
            }

            saved.Add((x, y, SaveCell(x, y)));
        }
    }
}
