#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>DEBUG-only multiplayer Test Case packet types for authoritative world/chest setup and cleanup.</summary>
    internal enum MpMessageType : byte
    {
        WorldSetupRequest = 1,
        WorldSetupAck = 2,
        CleanupRequest = 3,
        CleanupAck = 4,
        ChestPrepareRequest = 5,
        ChestPrepareAck = 6,
    }

    /// <summary>Chest prepare mode for multiplayer Test Case material control.</summary>
    internal enum MpChestMode : byte
    {
        Stock = 1,
        Empty = 2,
    }

    /// <summary>
    /// DEBUG-only packet protocol that applies world/chest setup and teardown on the paste authority.
    /// </summary>
    internal static class MpNetworking
    {
        /// <summary>Top-level Mod.HandlePacket discriminator; keeps paste packet bytes unchanged.</summary>
        internal const byte PacketDiscriminator = 250;

        /// <summary>Max Chebyshev distance from the owner player tile for setup/cleanup cells.</summary>
        private const int MaxPlayerTileDistance = 8;

        /// <summary>Wood chest multitile width/height used by Test Case chambers.</summary>
        internal const int ChestFootprintSize = 2;

        /// <summary>Max stock lines accepted in one chest-prepare request.</summary>
        private const int MaxChestStockLines = 64;

        private sealed class PendingClientRequest
        {
            public bool Completed;
            public bool Ok;
            public string Error = string.Empty;
        }

        private static readonly Dictionary<int, PendingClientRequest> ClientPending = new();
        private static int _nextCorrelationId = 1;

        /// <summary>Allocates a new client-side correlation id for a Test Case request.</summary>
        internal static int NextCorrelationId()
        {
            // --- Bump a process-local counter; wrap to keep ids positive.
            int id = _nextCorrelationId++;
            if (_nextCorrelationId <= 0)
                _nextCorrelationId = 1;
            return id;
        }

        /// <summary>True when this process may mutate world tiles for Test Case packets or local host setup.</summary>
        internal static bool IsAuthoritySide()
        {
            // --- Dedicated/listen server packet side, or in-process Host & Play listen host.
            return Main.netMode == NetmodeID.Server || TileMappingAuthority.IsLocalHostClient();
        }

        /// <summary>Registers a pending client request and returns false when the id collides.</summary>
        internal static bool TryBeginClientPending(int correlationId)
        {
            // --- One in-flight result slot per correlation id.
            if (correlationId <= 0 || ClientPending.ContainsKey(correlationId))
                return false;

            ClientPending[correlationId] = new PendingClientRequest();
            return true;
        }

        /// <summary>Reads a completed pending result and removes it from the client table.</summary>
        internal static bool TryTakeClientPending(int correlationId, out bool ok, out string error)
        {
            // --- Pop completed entries only so the Case waiter can poll.
            ok = false;
            error = string.Empty;
            if (!ClientPending.TryGetValue(correlationId, out PendingClientRequest? pending) || !pending.Completed)
                return false;

            ClientPending.Remove(correlationId);
            ok = pending.Ok;
            error = pending.Error;
            return true;
        }

        /// <summary>Drops a pending client request without waiting for an ACK.</summary>
        internal static void CancelClientPending(int correlationId)
        {
            ClientPending.Remove(correlationId);
        }

        /// <summary>Dispatches a Test Case packet after the top-level discriminator has been consumed.</summary>
        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Branch on message type; authority handlers ignore non-authoritative processes.
            var messageType = (MpMessageType)reader.ReadByte();
            switch (messageType)
            {
                case MpMessageType.WorldSetupRequest:
                    HandleWorldSetupRequest(mod, reader, whoAmI);
                    return;
                case MpMessageType.WorldSetupAck:
                    HandleClientAck(reader);
                    return;
                case MpMessageType.CleanupRequest:
                    HandleCleanupRequest(mod, reader, whoAmI);
                    return;
                case MpMessageType.CleanupAck:
                    HandleClientAck(reader);
                    return;
                case MpMessageType.ChestPrepareRequest:
                    HandleChestPrepareRequest(mod, reader, whoAmI);
                    return;
                case MpMessageType.ChestPrepareAck:
                    HandleClientAck(reader);
                    return;
                default:
                    throw new InvalidDataException($"Unknown MP Test Case message type: {messageType}");
            }
        }

        /// <summary>Sends an authoritative world setup request for the source/target cells.</summary>
        internal static void SendWorldSetupRequest(Mod mod, int correlationId, Point source, Point target, bool placeStoneAtSource)
        {
            // --- Client → authority: clear target, optionally place stone at source.
            ModPacket packet = mod.GetPacket();
            packet.Write(PacketDiscriminator);
            packet.Write((byte)MpMessageType.WorldSetupRequest);
            packet.Write(correlationId);
            packet.Write(source.X);
            packet.Write(source.Y);
            packet.Write(target.X);
            packet.Write(target.Y);
            packet.Write(placeStoneAtSource);
            packet.Send();
        }

        /// <summary>Sends an authoritative chest prepare request (stock or empty).</summary>
        internal static void SendChestPrepareRequest(
            Mod mod,
            int correlationId,
            Point chestOrigin,
            MpChestMode mode,
            IReadOnlyList<(int ItemId, int Count)> stockLines)
        {
            // --- Client → authority: place wood chest and optionally fill demand lines.
            ModPacket packet = mod.GetPacket();
            packet.Write(PacketDiscriminator);
            packet.Write((byte)MpMessageType.ChestPrepareRequest);
            packet.Write(correlationId);
            packet.Write(chestOrigin.X);
            packet.Write(chestOrigin.Y);
            packet.Write((byte)mode);
            packet.Write(stockLines.Count);
            for (int i = 0; i < stockLines.Count; i++)
            {
                packet.Write(stockLines[i].ItemId);
                packet.Write(stockLines[i].Count);
            }

            packet.Send();
        }

        /// <summary>Sends a cleanup request that clears Case tiles and tears down the material chest.</summary>
        internal static void SendCleanupRequest(Mod mod, int correlationId, Point source, Point target, Point chestOrigin)
        {
            // --- Client → authority: clear cells and remove the temporary chest footprint.
            ModPacket packet = mod.GetPacket();
            packet.Write(PacketDiscriminator);
            packet.Write((byte)MpMessageType.CleanupRequest);
            packet.Write(correlationId);
            packet.Write(source.X);
            packet.Write(source.Y);
            packet.Write(target.X);
            packet.Write(target.Y);
            packet.Write(chestOrigin.X);
            packet.Write(chestOrigin.Y);
            packet.Send();
        }

        /// <summary>Applies world setup locally on the listen host without sending a packet.</summary>
        internal static bool TryApplyWorldSetupLocal(Player player, Point source, Point target, bool placeStoneAtSource, out string error)
        {
            // --- Same mutation path as the authority packet handler, for Host & Play host Cases.
            return TryApplyWorldSetup(player, source, target, placeStoneAtSource, out error);
        }

        /// <summary>Applies chest prepare locally on the listen host without sending a packet.</summary>
        internal static bool TryApplyChestPrepareLocal(
            Player player,
            Point chestOrigin,
            MpChestMode mode,
            IReadOnlyList<(int ItemId, int Count)> stockLines,
            out string error)
        {
            // --- Same mutation path as the authority chest-prepare handler.
            return TryApplyChestPrepare(player, chestOrigin, mode, stockLines, out error);
        }

        /// <summary>Applies tile/chest cleanup locally on the listen host without sending a packet.</summary>
        internal static bool TryApplyCleanupLocal(Player player, Point source, Point target, Point chestOrigin, out string error)
        {
            // --- Clear paste cells and tear down the temporary material chest.
            return TryApplyCleanup(player, source, target, chestOrigin, out error);
        }

        private static void HandleWorldSetupRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Authority only: validate owner/range, mutate tiles, sync, ACK.
            int correlationId = reader.ReadInt32();
            var source = new Point(reader.ReadInt32(), reader.ReadInt32());
            var target = new Point(reader.ReadInt32(), reader.ReadInt32());
            bool placeStoneAtSource = reader.ReadBoolean();

            if (!IsAuthoritySide())
                return;

            if (!TryGetOwnerPlayer(whoAmI, out Player player))
            {
                SendAck(mod, whoAmI, MpMessageType.WorldSetupAck, correlationId, false, "Invalid owner player.");
                return;
            }

            if (!TryApplyWorldSetup(player, source, target, placeStoneAtSource, out string error))
            {
                SendAck(mod, whoAmI, MpMessageType.WorldSetupAck, correlationId, false, error);
                return;
            }

            SendAck(mod, whoAmI, MpMessageType.WorldSetupAck, correlationId, true, string.Empty);
        }

        private static void HandleChestPrepareRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Authority only: place/stock or empty the Case chest, then ACK.
            int correlationId = reader.ReadInt32();
            var chestOrigin = new Point(reader.ReadInt32(), reader.ReadInt32());
            var mode = (MpChestMode)reader.ReadByte();
            int lineCount = reader.ReadInt32();

            if (!IsAuthoritySide())
                return;

            if (lineCount < 0 || lineCount > MaxChestStockLines)
            {
                SendAck(mod, whoAmI, MpMessageType.ChestPrepareAck, correlationId, false, "Invalid chest stock line count.");
                return;
            }

            var lines = new List<(int ItemId, int Count)>(lineCount);
            for (int i = 0; i < lineCount; i++)
                lines.Add((reader.ReadInt32(), reader.ReadInt32()));

            if (!TryGetOwnerPlayer(whoAmI, out Player player))
            {
                SendAck(mod, whoAmI, MpMessageType.ChestPrepareAck, correlationId, false, "Invalid owner player.");
                return;
            }

            if (!TryApplyChestPrepare(player, chestOrigin, mode, lines, out string error))
            {
                SendAck(mod, whoAmI, MpMessageType.ChestPrepareAck, correlationId, false, error);
                return;
            }

            SendAck(mod, whoAmI, MpMessageType.ChestPrepareAck, correlationId, true, string.Empty);
        }

        private static void HandleCleanupRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            // --- Authority only: clear cells, tear down chest, ACK.
            int correlationId = reader.ReadInt32();
            var source = new Point(reader.ReadInt32(), reader.ReadInt32());
            var target = new Point(reader.ReadInt32(), reader.ReadInt32());
            var chestOrigin = new Point(reader.ReadInt32(), reader.ReadInt32());

            if (!IsAuthoritySide())
                return;

            if (!TryGetOwnerPlayer(whoAmI, out Player player))
            {
                SendAck(mod, whoAmI, MpMessageType.CleanupAck, correlationId, false, "Invalid owner player.");
                return;
            }

            if (!TryApplyCleanup(player, source, target, chestOrigin, out string error))
            {
                SendAck(mod, whoAmI, MpMessageType.CleanupAck, correlationId, false, error);
                return;
            }

            SendAck(mod, whoAmI, MpMessageType.CleanupAck, correlationId, true, string.Empty);
        }

        private static void HandleClientAck(BinaryReader reader)
        {
            // --- Client only: complete the matching pending correlation slot.
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            int correlationId = reader.ReadInt32();
            bool ok = reader.ReadBoolean();
            string error = reader.ReadString();

            if (!ClientPending.TryGetValue(correlationId, out PendingClientRequest? pending))
                return;

            pending.Completed = true;
            pending.Ok = ok;
            pending.Error = error ?? string.Empty;
        }

        private static void SendAck(Mod mod, int toClient, MpMessageType ackType, int correlationId, bool ok, string error)
        {
            // --- Dedicated server replies to one client; listen-host may complete locally.
            if (Main.netMode == NetmodeID.Server)
            {
                ModPacket packet = mod.GetPacket();
                packet.Write(PacketDiscriminator);
                packet.Write((byte)ackType);
                packet.Write(correlationId);
                packet.Write(ok);
                packet.Write(error ?? string.Empty);
                packet.Send(toClient);
                return;
            }

            if (TileMappingAuthority.IsLocalHostClient())
            {
                if (toClient == Main.myPlayer)
                {
                    if (ClientPending.TryGetValue(correlationId, out PendingClientRequest? pending))
                    {
                        pending.Completed = true;
                        pending.Ok = ok;
                        pending.Error = error ?? string.Empty;
                    }

                    return;
                }

                ModPacket packet = mod.GetPacket();
                packet.Write(PacketDiscriminator);
                packet.Write((byte)ackType);
                packet.Write(correlationId);
                packet.Write(ok);
                packet.Write(error ?? string.Empty);
                packet.Send(toClient);
            }
        }

        private static bool TryGetOwnerPlayer(int whoAmI, out Player player)
        {
            // --- Require an active player slot that matches the packet sender.
            player = null!;
            if ((uint)whoAmI >= (uint)Main.maxPlayers)
                return false;

            player = Main.player[whoAmI];
            return player is not null && player.active;
        }

        private static bool TryApplyWorldSetup(Player player, Point source, Point target, bool placeStoneAtSource, out string error)
        {
            // --- Range-check both cells, clear target, optionally place stone, then net-sync.
            error = string.Empty;
            if (!IsCellAllowedForPlayer(player, source, out error) || !IsCellAllowedForPlayer(player, target, out error))
                return false;

            ClearTileAuthoritative(source);
            ClearTileAuthoritative(target);

            if (placeStoneAtSource)
            {
                bool placed = WorldGen.PlaceTile(source.X, source.Y, TileID.Stone, mute: true, forced: true, plr: -1, style: 0);
                if (!placed || !Main.tile[source.X, source.Y].HasTile || Main.tile[source.X, source.Y].TileType != TileID.Stone)
                {
                    error = $"Failed to place stone at ({source.X},{source.Y}).";
                    return false;
                }
            }

            SyncTile(source);
            SyncTile(target);
            return true;
        }

        private static bool TryApplyChestPrepare(
            Player player,
            Point chestOrigin,
            MpChestMode mode,
            IReadOnlyList<(int ItemId, int Count)> stockLines,
            out string error)
        {
            // --- Validate, tear down any prior chest, place wood chest, stock or leave empty, sync.
            error = string.Empty;
            if (mode is not (MpChestMode.Stock or MpChestMode.Empty))
            {
                error = $"Unsupported chest mode: {mode}.";
                return false;
            }

            if (!IsChestFootprintAllowedForPlayer(player, chestOrigin, out error))
                return false;

            for (int i = 0; i < stockLines.Count; i++)
            {
                if (stockLines[i].ItemId <= 0 || stockLines[i].Count <= 0)
                {
                    error = $"Invalid chest stock line item={stockLines[i].ItemId} count={stockLines[i].Count}.";
                    return false;
                }
            }

            TearDownChestAt(chestOrigin);

            // CreateChest writes the table slot without requiring a solid floor under floating test cells.
            int chestIndex = Chest.CreateChest(chestOrigin.X, chestOrigin.Y);
            if (chestIndex < 0)
            {
                error = $"Failed to CreateChest at origin ({chestOrigin.X},{chestOrigin.Y}).";
                return false;
            }

            Chest chest = Main.chest[chestIndex];
            if (chest is null)
            {
                error = $"CreateChest returned index {chestIndex} but Main.chest slot is null.";
                return false;
            }

            if (chest.x != chestOrigin.X || chest.y != chestOrigin.Y)
            {
                error =
                    $"Chest origin mismatch: expected ({chestOrigin.X},{chestOrigin.Y}) got ({chest.x},{chest.y}).";
                TearDownChestAt(new Point(chest.x, chest.y));
                return false;
            }

            ClearChestItems(chestIndex);

            if (mode == MpChestMode.Stock
                && !TryStockChest(chestIndex, stockLines, out error))
            {
                TearDownChestAt(chestOrigin);
                return false;
            }

            // Broadcast table registration before slot contents so remotes can bind SyncChestItem.
            BroadcastChestRegistration(chestIndex, chestOrigin);
            SyncChestFootprint(chestOrigin);
            SyncAllChestSlots(chestIndex);
            return true;
        }

        private static bool TryApplyCleanup(Player player, Point source, Point target, Point chestOrigin, out string error)
        {
            // --- Clear paste cells and tear down the temporary material chest.
            error = string.Empty;
            if (!IsCellAllowedForPlayer(player, source, out error) || !IsCellAllowedForPlayer(player, target, out error))
                return false;

            if (!IsChestFootprintAllowedForPlayer(player, chestOrigin, out error))
                return false;

            ClearTileAuthoritative(source);
            ClearTileAuthoritative(target);
            SyncTile(source);
            SyncTile(target);

            TearDownChestAt(chestOrigin);
            SyncChestFootprint(chestOrigin);
            return true;
        }

        private static bool TryStockChest(int chestIndex, IReadOnlyList<(int ItemId, int Count)> lines, out string error)
        {
            // --- Fill chest slots from merged demand lines without exceeding capacity.
            error = string.Empty;
            Chest chest = Main.chest[chestIndex];
            if (chest?.item is null)
            {
                error = $"Chest {chestIndex} has no item array.";
                return false;
            }

            int nextSlot = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                int itemId = lines[i].ItemId;
                int remaining = lines[i].Count;
                Item probe = new();
                probe.SetDefaults(itemId);
                int maxStack = Math.Max(1, probe.maxStack);

                while (remaining > 0)
                {
                    if (nextSlot >= chest.item.Length)
                    {
                        error = $"Chest ran out of slots stocking item {itemId}.";
                        return false;
                    }

                    int add = Math.Min(maxStack, remaining);
                    chest.item[nextSlot] ??= new Item();
                    chest.item[nextSlot].SetDefaults(itemId);
                    chest.item[nextSlot].stack = add;
                    remaining -= add;
                    nextSlot++;
                }
            }

            return true;
        }

        private static void ClearChestItems(int chestIndex)
        {
            Chest? chest = Main.chest[chestIndex];
            if (chest?.item is null)
                return;

            for (int i = 0; i < chest.item.Length; i++)
            {
                chest.item[i] ??= new Item();
                chest.item[i].TurnToAir();
            }
        }

        private static void TearDownChestAt(Point chestOrigin)
        {
            // --- Remove any chest record whose origin matches, then clear the 2x2 footprint tiles.
            if (TryFindChestIndexAtExact(chestOrigin.X, chestOrigin.Y, out int chestIndex))
            {
                ClearChestItems(chestIndex);
                Main.chest[chestIndex] = null;
                if (Main.netMode == NetmodeID.Server || TileMappingAuthority.IsLocalHostClient())
                    NetMessage.SendData(MessageID.ChestUpdates, -1, -1, null, chestIndex, chestOrigin.X, chestOrigin.Y);
            }

            for (int dy = 0; dy < ChestFootprintSize; dy++)
            {
                for (int dx = 0; dx < ChestFootprintSize; dx++)
                    ClearTileAuthoritative(new Point(chestOrigin.X + dx, chestOrigin.Y + dy));
            }
        }

        private static bool TryFindChestIndexAtExact(int worldX, int worldY, out int chestIndex)
        {
            // --- Linear scan of Main.chest for an exact origin match.
            chestIndex = -1;
            for (int i = 0; i < Main.maxChests; i++)
            {
                Chest? chest = Main.chest[i];
                if (chest is null)
                    continue;

                if (chest.x == worldX && chest.y == worldY)
                {
                    chestIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool IsChestFootprintAllowedForPlayer(Player player, Point chestOrigin, out string error)
        {
            // --- Require every cell of the 2x2 footprint to pass the near-player range check.
            for (int dy = 0; dy < ChestFootprintSize; dy++)
            {
                for (int dx = 0; dx < ChestFootprintSize; dx++)
                {
                    if (!IsCellAllowedForPlayer(player, new Point(chestOrigin.X + dx, chestOrigin.Y + dy), out error))
                        return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool IsCellAllowedForPlayer(Player player, Point tile, out string error)
        {
            // --- Keep Case mutations near the owner and inside the world.
            error = string.Empty;
            if (!WorldGen.InWorld(tile.X, tile.Y))
            {
                error = $"Tile out of world: ({tile.X},{tile.Y}).";
                return false;
            }

            Point playerTile = player.Center.ToTileCoordinates();
            int dist = Math.Max(Math.Abs(tile.X - playerTile.X), Math.Abs(tile.Y - playerTile.Y));
            if (dist > MaxPlayerTileDistance)
            {
                error =
                    $"Tile ({tile.X},{tile.Y}) is farther than {MaxPlayerTileDistance} tiles from player ({playerTile.X},{playerTile.Y}).";
                return false;
            }

            return true;
        }

        private static void ClearTileAuthoritative(Point tile)
        {
            if (WorldGen.InWorld(tile.X, tile.Y))
                WorldGen.KillTile(tile.X, tile.Y, fail: false, effectOnly: false, noItem: true);
        }

        private static void SyncTile(Point tile)
        {
            // --- Broadcast the mutated cell so remote clients observe setup/cleanup.
            if (Main.netMode == NetmodeID.Server || TileMappingAuthority.IsLocalHostClient())
                NetMessage.SendTileSquare(-1, tile.X, tile.Y, 1);
        }

        private static void SyncChestFootprint(Point chestOrigin)
        {
            // --- Sync the full 2x2 square covering the wood chest multitile.
            if (Main.netMode == NetmodeID.Server || TileMappingAuthority.IsLocalHostClient())
                NetMessage.SendTileSquare(-1, chestOrigin.X, chestOrigin.Y, ChestFootprintSize, ChestFootprintSize);
        }

        /// <summary>Notifies peers that a Main.chest slot was allocated or cleared at the given origin.</summary>
        private static void BroadcastChestRegistration(int chestIndex, Point chestOrigin)
        {
            // --- ChestUpdates creates/clears the remote table entry; SyncChestItem alone is not enough.

            if (Main.netMode != NetmodeID.Server && !TileMappingAuthority.IsLocalHostClient())
                return;

            NetMessage.SendData(MessageID.ChestUpdates, -1, -1, null, chestIndex, chestOrigin.X, chestOrigin.Y);
        }

        private static void SyncAllChestSlots(int chestIndex)
        {
            // --- Mirror every chest slot so remote clients observe stock/empty state.
            if (Main.netMode != NetmodeID.Server && !TileMappingAuthority.IsLocalHostClient())
                return;

            Chest? chest = Main.chest[chestIndex];
            if (chest?.item is null)
                return;

            for (int i = 0; i < chest.item.Length; i++)
                NetMessage.SendData(MessageID.SyncChestItem, -1, -1, null, chestIndex, i);
        }
    }
}
#endif
