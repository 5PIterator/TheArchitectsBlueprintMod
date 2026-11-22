using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>
    /// World-anchored positioning authority for discovery and Test Cases (origin, layout slots, ring/scaffold).
    /// </summary>
    public sealed class TestChamberSystem : ModSystem
    {
        public static TestChamberSystem Instance => ModContent.GetInstance<TestChamberSystem>();

        /// <summary>Top-level <see cref="Mod.HandlePacket"/> discriminator for chamber-origin sync.</summary>
        public const byte SyncPacketDiscriminator = 249;

        /// <summary>Neighborhood offsets around the focal tile (excluding the center).</summary>
        public static readonly (int Dx, int Dy)[] RingOffsets =
        [
            (-1, -1), (0, -1), (1, -1),
            (-1, 0), (1, 0),
            (-1, 1), (0, 1), (1, 1),
        ];

        private bool _hasOrigin;
        private int _originX;
        private int _originY;

        private bool _sessionActive;
        private int _dirtyMinX;
        private int _dirtyMinY;
        private int _dirtyMaxX;
        private int _dirtyMaxY;
        private List<(int X, int Y, CellSnapshot Snapshot)>? _sessionSnapshots;

        /// <summary>True when a chamber origin has been set for this world.</summary>
        public bool HasOrigin => _hasOrigin;

        /// <summary>Configured chamber origin, or (0,0) when unset.</summary>
        public Point Origin => _hasOrigin ? new Point(_originX, _originY) : Point.Zero;

        /// <summary>Stone copy cell relative to the chamber origin (inside paste reach).</summary>
        public Point StoneSource => Cell(-1, -2);

        /// <summary>Stone paste destination relative to the chamber origin.</summary>
        public Point StoneTarget => Cell(1, -2);

        /// <summary>Second paste target used by multiplayer lifecycle scenarios.</summary>
        public Point StoneTargetB => Cell(2, -2);

        /// <summary>
        /// Wood-chest top-left in the paste reach ring of both <see cref="StoneTarget"/> and <see cref="StoneTargetB"/>.
        /// Offset (1,-4) keeps the 2×2 footprint clear of stone cells while remaining inside ReachTiles=2.
        /// </summary>
        public Point MaterialChestOrigin => Cell(1, -4);

        /// <summary>Base origin for selection API scenarios (non-overlapping with stone slots).</summary>
        public Point SelectionOrigin => Cell(8, -4);

        /// <summary>Base origin for projection API scenarios (non-overlapping with selection slots).</summary>
        public Point ProjectionOrigin => Cell(20, -6);

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

        /// <summary>Persists chamber origin into world save data.</summary>
        public override void SaveWorldData(TagCompound tag)
        {
            // --- Write HasOrigin and optional X/Y so joined peers reload the same anchor.

            tag["chamberHasOrigin"] = _hasOrigin;
            if (!_hasOrigin)
                return;

            tag["chamberOriginX"] = _originX;
            tag["chamberOriginY"] = _originY;
        }

        /// <summary>Restores chamber origin from world save data.</summary>
        public override void LoadWorldData(TagCompound tag)
        {
            // --- Read HasOrigin first; coordinates are required only when set.

            _hasOrigin = tag.ContainsKey("chamberHasOrigin") && tag.GetBool("chamberHasOrigin");
            if (!_hasOrigin)
            {
                _originX = 0;
                _originY = 0;
                return;
            }

            _originX = tag.ContainsKey("chamberOriginX") ? tag.GetInt("chamberOriginX") : 0;
            _originY = tag.ContainsKey("chamberOriginY") ? tag.GetInt("chamberOriginY") : 0;
        }

        /// <summary>Clears chamber origin and any open mutation session when leaving the world.</summary>
        public override void ClearWorld()
        {
            // --- Drop in-memory origin/session so the next world starts unset.

            if (_sessionActive)
                EndSession(restore: false);

            ClearOrigin();
        }

        /// <summary>Sets the chamber origin to the given tile coordinate.</summary>
        public void SetOrigin(Point origin)
        {
            // --- Store the tile point and mark the chamber as configured.

            _originX = origin.X;
            _originY = origin.Y;
            _hasOrigin = true;
        }

        /// <summary>Clears the configured chamber origin.</summary>
        public void ClearOrigin()
        {
            // --- Unset HasOrigin and zero the stored coordinates.

            _hasOrigin = false;
            _originX = 0;
            _originY = 0;
        }

        /// <summary>Returns the configured origin when set.</summary>
        public bool TryGetOrigin(out Point origin)
        {
            // --- Expose Origin only when HasOrigin is true.

            if (!_hasOrigin)
            {
                origin = Point.Zero;
                return false;
            }

            origin = new Point(_originX, _originY);
            return true;
        }

        /// <summary>
        /// Resolves the chamber origin when configured; otherwise falls back to (player tile X, player tile Y - 5).
        /// </summary>
        public bool TryResolveOrigin(Player fallbackPlayer, out Point origin)
        {
            // --- Prefer the pinned chamber; else use the historical discovery offset above the player.

            if (TryGetOrigin(out origin))
                return true;

            if (fallbackPlayer is null)
            {
                origin = Point.Zero;
                return false;
            }

            Point playerTile = fallbackPlayer.Center.ToTileCoordinates();
            origin = new Point(playerTile.X, playerTile.Y - 5);
            return WorldGen.InWorld(origin.X, origin.Y);
        }

        /// <summary>Fails when the chamber origin is unset; used to gate scenario runs.</summary>
        public bool RequireConfigured(out string? error)
        {
            // --- Scenario runners must pin CHM before TileNearPlayer-free layout slots are valid.

            if (_hasOrigin)
            {
                error = null;
                return true;
            }

            error = "Test chamber origin is not set. Use Debug Stick CHM on a flat area first.";
            return false;
        }

        /// <summary>Returns a tile coordinate at the given offset from the chamber origin.</summary>
        public Point Cell(int dx, int dy)
        {
            // --- Offset from Origin; callers must RequireConfigured / HasOrigin first for scenarios.
            return new Point(_originX + dx, _originY + dy);
        }

        /// <summary>Bottom-left of a payload footprint placed at the chamber origin.</summary>
        public Point FixtureOrigin(int width, int height)
        {
            // --- Anchor the fixture bottom-left on the chamber origin (width/height reserved for layout docs).
            _ = width;
            _ = height;
            return Cell(0, 0);
        }

        /// <summary>Support cell for layer classification columns (0 → x=-2, 1 → x=0, 2 → x=+2).</summary>
        public Point LayerSupport(int columnIndex)
        {
            // --- Space columns two tiles apart on the y=-1 row relative to the chamber origin.
            return Cell(-2 + columnIndex * 2, -1);
        }

        /// <summary>Starts a mutation session that snapshots touched cells and tracks a dirty AABB.</summary>
        public void BeginSession()
        {
            // --- Reset dirty bounds and allocate a fresh snapshot list.

            if (_sessionActive)
                EndSession(restore: false);

            _sessionActive = true;
            _sessionSnapshots = [];
            _dirtyMinX = int.MaxValue;
            _dirtyMinY = int.MaxValue;
            _dirtyMaxX = int.MinValue;
            _dirtyMaxY = int.MinValue;
        }

        /// <summary>Ends the mutation session, optionally restoring snapshotted cells.</summary>
        public void EndSession(bool restore = true)
        {
            // --- Restore last-touched first when requested, then drop session state.

            if (!_sessionActive)
                return;

            if (restore && _sessionSnapshots is not null)
            {
                SuppressTileSound.Run(() =>
                {
                    for (int i = _sessionSnapshots.Count - 1; i >= 0; i--)
                    {
                        (int x, int y, CellSnapshot snapshot) = _sessionSnapshots[i];
                        RestoreCell(x, y, snapshot);
                    }
                });
            }

            _sessionActive = false;
            _sessionSnapshots = null;
            _dirtyMinX = 0;
            _dirtyMinY = 0;
            _dirtyMaxX = 0;
            _dirtyMaxY = 0;
        }

        /// <summary>Dirty AABB of cells touched since <see cref="BeginSession"/>, or empty when inactive.</summary>
        public Rectangle SessionDirtyBounds
        {
            get
            {
                if (!_sessionActive || _dirtyMaxX < _dirtyMinX)
                    return Rectangle.Empty;

                return new Rectangle(
                    _dirtyMinX,
                    _dirtyMinY,
                    _dirtyMaxX - _dirtyMinX + 1,
                    _dirtyMaxY - _dirtyMinY + 1);
            }
        }

        /// <summary>Clears tile and wall at a cell, snapshotting into the active session when present.</summary>
        public void ClearCell(int x, int y)
        {
            // --- Snapshot once, then kill tile and wall without drops.

            if (!WorldGen.InWorld(x, y))
                return;

            NoteSessionTouch(x, y);
            SuppressTileSound.Run(() =>
            {
                SuppressTileDrop.KillTile(x, y);
                SuppressTileDrop.KillWall(x, y);
            });
        }

        /// <summary>Clears a cell then places <paramref name="tileType"/> with the given style.</summary>
        public void PlaceTile(int x, int y, int tileType, int style = 0)
        {
            // --- Session-aware clear + forced mute PlaceTile.

            if (!WorldGen.InWorld(x, y))
                return;

            NoteSessionTouch(x, y);
            ClearAndSetTile(x, y, tileType, style);
        }

        /// <summary>Places a stone tile at the given coordinate.</summary>
        public void PlaceStone(Point tile)
        {
            // --- Delegate to PlaceTile with TileID.Stone.
            PlaceTile(tile.X, tile.Y, TileID.Stone);
        }

        /// <summary>Ensures a cell has no tile (air), snapshotting into the session when active.</summary>
        public void EnsureAir(Point tile)
        {
            // --- Clear only the tile; leave walls alone for air assertions.

            if (!WorldGen.InWorld(tile.X, tile.Y))
                return;

            NoteSessionTouch(tile.X, tile.Y);
            SuppressTileSound.Run(() => SuppressTileDrop.KillTile(tile.X, tile.Y));
        }

        /// <summary>
        /// Snaps the player's tile center into the paste inventory reach window when out of range.
        /// </summary>
        public bool EnsurePlayerInReach(Player player, Rectangle targetArea)
        {
            // --- No-op when already in range; otherwise teleport onto the target center tile.

            if (player is not { active: true })
                return false;

            if (InventoryManager.IsPlayerInInventoryRange(player, targetArea))
                return true;

            Point center = new(
                targetArea.X + Math.Max(0, targetArea.Width / 2),
                targetArea.Y + Math.Max(0, targetArea.Height / 2));
            player.position = new Vector2(center.X * 16f, center.Y * 16f) - player.Size / 2f;
            return InventoryManager.IsPlayerInInventoryRange(player, targetArea);
        }

        /// <summary>True when a chest footprint sits in the paste inventory reach ring around the target.</summary>
        public bool ValidateReachRingChest(Point chestOrigin, Rectangle targetArea)
        {
            // --- Delegate to InventoryManager so MP and SP share one reach rule.

            var footprint = new Rectangle(chestOrigin.X, chestOrigin.Y, 2, 2);
            return InventoryManager.IsChestInInventoryReachRing(footprint, targetArea);
        }

        /// <summary>Applies a synced origin from the network without re-broadcasting.</summary>
        public void ApplySyncedOrigin(bool hasOrigin, int originX, int originY)
        {
            // --- Remote clients mirror host/peer origin without echoing another packet.

            _hasOrigin = hasOrigin;
            _originX = originX;
            _originY = originY;
        }

        /// <summary>Broadcasts the current chamber origin to peers (no-op in single-player).</summary>
        public void BroadcastOriginSync(Mod mod)
        {
            // --- Host & Play / dedicated: send HasOrigin + X/Y so joiners share the anchor without reload.

            ArgumentNullException.ThrowIfNull(mod);
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            ModPacket packet = mod.GetPacket();
            packet.Write(SyncPacketDiscriminator);
            packet.Write(_hasOrigin);
            packet.Write(_originX);
            packet.Write(_originY);

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                // Clients send to the server; the server rebroadcasts.
                packet.Send();
                return;
            }

            // Server / listen host: fan out to every client.
            packet.Send(toClient: -1, ignoreClient: -1);
        }

        /// <summary>Handles an inbound chamber-origin sync packet and rebroadcasts from the server.</summary>
        public static void HandleSyncPacket(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Apply payload locally; when received on the server, forward to all other clients.

            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(reader);

            bool hasOrigin = reader.ReadBoolean();
            int originX = reader.ReadInt32();
            int originY = reader.ReadInt32();

            Instance.ApplySyncedOrigin(hasOrigin, originX, originY);

            if (Main.netMode != NetmodeID.Server)
                return;

            ModPacket packet = mod.GetPacket();
            packet.Write(SyncPacketDiscriminator);
            packet.Write(hasOrigin);
            packet.Write(originX);
            packet.Write(originY);
            packet.Send(toClient: -1, ignoreClient: whoAmI);
        }

        /// <summary>Records a session snapshot and expands the dirty AABB for one cell.</summary>
        private void NoteSessionTouch(int x, int y)
        {
            // --- First write wins for restore; always expand dirty bounds while a session is open.

            if (!_sessionActive || _sessionSnapshots is null)
                return;

            bool already = false;
            for (int i = 0; i < _sessionSnapshots.Count; i++)
            {
                if (_sessionSnapshots[i].X == x && _sessionSnapshots[i].Y == y)
                {
                    already = true;
                    break;
                }
            }

            if (!already)
                _sessionSnapshots.Add((x, y, SaveCell(x, y)));

            if (x < _dirtyMinX)
                _dirtyMinX = x;
            if (y < _dirtyMinY)
                _dirtyMinY = y;
            if (x > _dirtyMaxX)
                _dirtyMaxX = x;
            if (y > _dirtyMaxY)
                _dirtyMaxY = y;
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
