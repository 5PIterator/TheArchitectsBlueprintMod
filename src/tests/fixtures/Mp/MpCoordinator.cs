#if DEBUG
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Client-side multiplayer Test Case orchestration for authoritative world/chest setup and cleanup.</summary>
    internal static class MpCoordinator
    {
        /// <summary>Default frame budget for authority ACKs and world/chest sync visibility.</summary>
        private const int DefaultSyncTimeoutFrames = 600;

        /// <summary>Builds chamber stone source/target points used by multiplayer stone Cases.</summary>
        internal static void GetStoneCells(out Point source, out Point target)
        {
            // --- Chamber slots keep both cells inside paste inventory reach (ReachTiles = 2).
            TestChamberSystem chamber = TestChamberSystem.Instance;
            source = chamber.StoneSource;
            target = chamber.StoneTarget;
        }

        /// <summary>Returns the wood-chest top-left origin from the chamber material slot.</summary>
        internal static Point GetMaterialChestOrigin()
        {
            // --- Fold MP chest placement onto MaterialChestOrigin (beside StoneTarget).
            return TestChamberSystem.Instance.MaterialChestOrigin;
        }

        /// <summary>
        /// Fails when the local player is outside paste inventory reach of <paramref name="workArea"/>.
        /// Does not teleport or otherwise move the player.
        /// </summary>
        internal static bool TryRequirePlayerNearChamber(
            Player player,
            Rectangle workArea,
            out string? error)
        {
            // --- Authority Chebyshev checks and paste reach both require the owner near the chamber.

            if (InventoryManager.IsPlayerInInventoryRange(player, workArea))
            {
                error = null;
                return true;
            }

            error =
                $"Player is outside paste inventory reach (ReachTiles={Inventory.ReachTiles}) of the chamber work area; "
                + "stand near the CHM origin before running the Case.";
            return false;
        }

        /// <summary>True when this client should use the PasteRequest ACK lifecycle instead of a local operation.</summary>
        internal static bool UsesRemotePasteRequestPath()
        {
            // --- Remote joiners request; SP and listen host execute locally.
            return Main.netMode == NetmodeID.MultiplayerClient && !TileMappingAuthority.IsLocalHostClient();
        }

        /// <summary>True when this process may apply chamber world/chest mutations without a server round-trip.</summary>
        internal static bool CanMutateWorldLocally()
        {
            // --- SP owns the world; Host & Play listen host mutates in-process; remote joiners must request.
            return Main.netMode == NetmodeID.SinglePlayer || TileMappingAuthority.IsLocalHostClient();
        }

        /// <summary>Prepares source/target tiles on the paste authority and waits until the client can observe them.</summary>
        internal static IEnumerator RequestWorldSetup(
            Player player,
            Point source,
            Point target,
            bool placeStoneAtSource,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete)
        {
            // --- SP / listen host mutate immediately; remote clients request + await ACK + tile visibility.

            var workArea = new Rectangle(
                Math.Min(source.X, target.X),
                Math.Min(source.Y, target.Y),
                Math.Abs(source.X - target.X) + 1,
                Math.Abs(source.Y - target.Y) + 1);
            if (!TryRequirePlayerNearChamber(player, workArea, out string? reachError))
            {
                onComplete(false, reachError);
                yield break;
            }

            if (CanMutateWorldLocally())
            {
                if (!MpNetworking.TryApplyWorldSetupLocal(player, source, target, placeStoneAtSource, out string localError))
                {
                    onComplete(false, localError);
                    yield break;
                }

                IEnumerator waitLocal = WaitForWorldSetupVisible(source, target, placeStoneAtSource, reporter, caseTag, onComplete);
                while (waitLocal.MoveNext())
                    yield return waitLocal.Current;
                yield break;
            }

            Mod mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>();
            int correlationId = MpNetworking.NextCorrelationId();
            if (!MpNetworking.TryBeginClientPending(correlationId))
            {
                onComplete(false, "Failed to register world-setup correlation id.");
                yield break;
            }

            MpNetworking.SendWorldSetupRequest(mod, correlationId, source, target, placeStoneAtSource);

            bool ackOk = false;
            string? ackError = null;
            IEnumerator waitAck = AwaitClientAck(correlationId, reporter, caseTag, "world-setup", (ok, err) =>
            {
                ackOk = ok;
                ackError = err;
            });
            while (waitAck.MoveNext())
                yield return waitAck.Current;

            if (!ackOk)
            {
                onComplete(false, ackError ?? "World setup ACK failed.");
                yield break;
            }

            IEnumerator waitVisible = WaitForWorldSetupVisible(source, target, placeStoneAtSource, reporter, caseTag, onComplete);
            while (waitVisible.MoveNext())
                yield return waitVisible.Current;
        }

        /// <summary>Places an authoritative material chest (stocked or empty) beside the paste target.</summary>
        internal static IEnumerator RequestChestPrepare(
            Player player,
            Point chestOrigin,
            Projection projection,
            MpChestMode mode,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete)
        {
            // --- Build stock lines for Stock mode, apply on authority, wait until chest state is visible.

            var workArea = new Rectangle(chestOrigin.X, chestOrigin.Y, MpNetworking.ChestFootprintSize, MpNetworking.ChestFootprintSize);
            if (!TryRequirePlayerNearChamber(player, workArea, out string? reachError))
            {
                onComplete(false, reachError);
                yield break;
            }

            List<(int ItemId, int Count)> lines = [];
            if (mode == MpChestMode.Stock)
            {
                if (!InventoryHelpers.TryBuildRequiredItemLines(projection, out lines, out string? buildError))
                {
                    onComplete(false, buildError);
                    yield break;
                }
            }

            if (CanMutateWorldLocally())
            {
                if (!MpNetworking.TryApplyChestPrepareLocal(player, chestOrigin, mode, lines, out string localError))
                {
                    onComplete(false, localError);
                    yield break;
                }

                IEnumerator waitLocal = WaitForChestPrepareVisible(chestOrigin, mode, lines, reporter, caseTag, onComplete);
                while (waitLocal.MoveNext())
                    yield return waitLocal.Current;
                yield break;
            }

            Mod mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>();
            int correlationId = MpNetworking.NextCorrelationId();
            if (!MpNetworking.TryBeginClientPending(correlationId))
            {
                onComplete(false, "Failed to register chest-prepare correlation id.");
                yield break;
            }

            MpNetworking.SendChestPrepareRequest(mod, correlationId, chestOrigin, mode, lines);

            bool ackOk = false;
            string? ackError = null;
            IEnumerator waitAck = AwaitClientAck(correlationId, reporter, caseTag, "chest-prepare", (ok, err) =>
            {
                ackOk = ok;
                ackError = err;
            });
            while (waitAck.MoveNext())
                yield return waitAck.Current;

            if (!ackOk)
            {
                onComplete(false, ackError ?? "Chest prepare ACK failed.");
                yield break;
            }

            IEnumerator waitVisible = WaitForChestPrepareVisible(chestOrigin, mode, lines, reporter, caseTag, onComplete);
            while (waitVisible.MoveNext())
                yield return waitVisible.Current;
        }

        /// <summary>Clears Case tiles and tears down the material chest on the authority.</summary>
        internal static IEnumerator RequestCleanup(
            Player player,
            Point source,
            Point target,
            Point chestOrigin,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete)
        {
            // --- Always attempt authoritative cleanup so failed Cases do not leave residue.

            var workArea = new Rectangle(
                Math.Min(Math.Min(source.X, target.X), chestOrigin.X),
                Math.Min(Math.Min(source.Y, target.Y), chestOrigin.Y),
                1,
                1);
            int maxX = Math.Max(Math.Max(source.X, target.X), chestOrigin.X + MpNetworking.ChestFootprintSize - 1);
            int maxY = Math.Max(Math.Max(source.Y, target.Y), chestOrigin.Y + MpNetworking.ChestFootprintSize - 1);
            workArea.Width = maxX - workArea.X + 1;
            workArea.Height = maxY - workArea.Y + 1;

            if (!TryRequirePlayerNearChamber(player, workArea, out string? reachError))
            {
                onComplete(false, reachError);
                yield break;
            }

            if (CanMutateWorldLocally())
            {
                if (!MpNetworking.TryApplyCleanupLocal(player, source, target, chestOrigin, out string localError))
                {
                    onComplete(false, localError);
                    yield break;
                }

                reporter.Info($"{caseTag}: local cleanup completed.");
                onComplete(true, null);
                yield break;
            }

            Mod mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>();
            int correlationId = MpNetworking.NextCorrelationId();
            if (!MpNetworking.TryBeginClientPending(correlationId))
            {
                onComplete(false, "Failed to register cleanup correlation id.");
                yield break;
            }

            MpNetworking.SendCleanupRequest(mod, correlationId, source, target, chestOrigin);

            IEnumerator waitAck = AwaitClientAck(correlationId, reporter, caseTag, "cleanup", onComplete);
            while (waitAck.MoveNext())
                yield return waitAck.Current;
        }

        /// <summary>Creates a projection from the authority-prepared source cell and moves it onto the target.</summary>
        internal static bool TryCreateMovedStoneProjection(
            Player player,
            Point source,
            Point target,
            out Projection? projection,
            out Payload expected,
            out string? error)
        {
            // --- Capture after world-setup visibility, move onto cleared target, verify reach.
            projection = null;
            expected = Payload.Empty;
            error = null;

            if (!Main.tile[source.X, source.Y].HasTile || Main.tile[source.X, source.Y].TileType != TileID.Stone)
            {
                error = $"Source cell ({source.X},{source.Y}) is not stone after world setup.";
                return false;
            }

            if (!StonePasteFixture.TryCreateProjection(
                    player,
                    new Rectangle(source.X, source.Y, 1, 1),
                    captureWorldPayload: true,
                    out projection,
                    out error)
                || projection is null)
            {
                return false;
            }

            if (!projection.HasPayload)
            {
                error = "Projection has no payload after capture.";
                StonePasteFixture.Cleanup(player, projection);
                projection = null;
                return false;
            }

            expected = Payload.FromClone(projection.Payload);
            projection.MoveTo(target);

            if (!TryRequirePlayerNearChamber(player, projection.TargetArea, out error))
            {
                StonePasteFixture.Cleanup(player, projection);
                projection = null;
                return false;
            }

            Point chestOrigin = GetMaterialChestOrigin();
            if (!TestChamberSystem.Instance.ValidateReachRingChest(chestOrigin, projection.TargetArea))
            {
                error =
                    $"Material chest footprint ({chestOrigin.X},{chestOrigin.Y}) is outside the paste reach ring.";
                StonePasteFixture.Cleanup(player, projection);
                projection = null;
                return false;
            }

            return true;
        }

        /// <summary>Waits until the target world cell matches the expected payload or times out.</summary>
        internal static IEnumerator WaitForTargetMatch(
            Rectangle targetArea,
            Payload expected,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxFrames = DefaultSyncTimeoutFrames)
        {
            // --- Poll FromWorld until TryMatch would pass, allowing MP tile sync latency.

            for (int frame = 0; frame < maxFrames; frame++)
            {
                Payload actual = Payload.FromWorld(targetArea);
                PayloadDiffResult diff = PayloadDiff.Compare(expected, actual, targetArea);
                if (diff.MismatchCount == 0)
                {
                    reporter.Info($"{caseTag}: target matched after {frame} frame(s).");
                    onComplete(true, null);
                    yield break;
                }

                yield return null;
            }

            onComplete(false, $"Target did not match expected payload within {maxFrames} frames.");
        }

        /// <summary>Waits until a world tile is empty (used after cut/no-place assertions).</summary>
        internal static IEnumerator WaitForTileEmpty(
            Point tile,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxFrames = DefaultSyncTimeoutFrames)
        {
            // --- Poll HasTile until cleared or timeout.
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (!Main.tile[tile.X, tile.Y].HasTile)
                {
                    reporter.Info($"{caseTag}: tile ({tile.X},{tile.Y}) empty after {frame} frame(s).");
                    onComplete(true, null);
                    yield break;
                }

                yield return null;
            }

            onComplete(false, $"Tile ({tile.X},{tile.Y}) still occupied after {maxFrames} frames.");
        }

        /// <summary>Counts stacks of an item type inside the Case material chest.</summary>
        internal static int CountChestItem(Point chestOrigin, int itemType)
        {
            // --- Sum matching stacks from the Main.chest entry at the Case origin.
            if (itemType <= 0 || !TryGetChestAt(chestOrigin, out Chest? chest) || chest?.item is null)
                return 0;

            int total = 0;
            for (int i = 0; i < chest.item.Length; i++)
            {
                Item slot = chest.item[i];
                if (slot is not null && !slot.IsAir && slot.type == itemType)
                    total += slot.stack;
            }

            return total;
        }

        /// <summary>Counts the primary manifest consumable inside the Case material chest.</summary>
        internal static int CountPrimaryConsumableInChest(Point chestOrigin, Projection projection)
        {
            // --- Use the first consumable demand line as the consumption probe.
            int itemId = GetPrimaryConsumableItemId(projection);
            return itemId > 0 ? CountChestItem(chestOrigin, itemId) : 0;
        }

        /// <summary>Returns the first consumable item id from the projection manifest, or 0.</summary>
        internal static int GetPrimaryConsumableItemId(Projection projection)
        {
            Manifest manifest = ManifestBuilder.Build(projection);
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                ManifestEntry entry = manifest.Entries[i];
                if (entry.ConsumableItemId > 0)
                    return entry.ConsumableItemId;
            }

            return 0;
        }

        private static bool TryGetChestAt(Point chestOrigin, out Chest? chest)
        {
            // --- Find the Main.chest slot whose exact origin matches the Case chamber.
            chest = null;
            for (int i = 0; i < Main.maxChests; i++)
            {
                Chest? candidate = Main.chest[i];
                if (candidate is null)
                    continue;

                if (candidate.x == chestOrigin.X && candidate.y == chestOrigin.Y)
                {
                    chest = candidate;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator AwaitClientAck(
            int correlationId,
            ITestCaseReporter reporter,
            string caseTag,
            string label,
            Action<bool, string?> onComplete,
            int maxFrames = DefaultSyncTimeoutFrames)
        {
            // --- Poll pending table until the authority ACK arrives or the frame budget expires.
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (MpNetworking.TryTakeClientPending(correlationId, out bool ok, out string error))
                {
                    if (ok)
                    {
                        reporter.Info($"{caseTag}: {label} ACK ok in {frame} frame(s).");
                        onComplete(true, null);
                    }
                    else
                    {
                        onComplete(false, $"{label} rejected: {error}");
                    }

                    yield break;
                }

                yield return null;
            }

            MpNetworking.CancelClientPending(correlationId);
            onComplete(false, $"{label} timed out after {maxFrames} frames.");
        }

        private static IEnumerator WaitForWorldSetupVisible(
            Point source,
            Point target,
            bool placeStoneAtSource,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxFrames = DefaultSyncTimeoutFrames)
        {
            // --- Require target air and optional source stone after net sync.
            for (int frame = 0; frame < maxFrames; frame++)
            {
                bool targetOk = !Main.tile[target.X, target.Y].HasTile;
                bool sourceOk = !placeStoneAtSource
                    || (Main.tile[source.X, source.Y].HasTile && Main.tile[source.X, source.Y].TileType == TileID.Stone);

                if (targetOk && sourceOk)
                {
                    reporter.Info($"{caseTag}: world setup visible after {frame} frame(s).");
                    onComplete(true, null);
                    yield break;
                }

                yield return null;
            }

            onComplete(false, $"World setup not visible within {maxFrames} frames.");
        }

        private static IEnumerator WaitForChestPrepareVisible(
            Point chestOrigin,
            MpChestMode mode,
            IReadOnlyList<(int ItemId, int Count)> lines,
            ITestCaseReporter reporter,
            string caseTag,
            Action<bool, string?> onComplete,
            int maxFrames = DefaultSyncTimeoutFrames)
        {
            // --- Require chest record + stocked or empty contents after net sync.
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (TryGetChestAt(chestOrigin, out Chest? chest) && chest?.item is not null)
                {
                    bool contentsOk = mode == MpChestMode.Empty
                        ? IsChestEmpty(chest)
                        : HasChestStock(chest, lines);

                    if (contentsOk)
                    {
                        reporter.Info($"{caseTag}: chest prepare visible after {frame} frame(s).");
                        onComplete(true, null);
                        yield break;
                    }
                }

                yield return null;
            }

            onComplete(false, $"Chest prepare not visible within {maxFrames} frames.");
        }

        private static bool IsChestEmpty(Chest chest)
        {
            for (int i = 0; i < chest.item.Length; i++)
            {
                Item slot = chest.item[i];
                if (slot is not null && !slot.IsAir)
                    return false;
            }

            return true;
        }

        private static bool HasChestStock(Chest chest, IReadOnlyList<(int ItemId, int Count)> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                int have = 0;
                for (int s = 0; s < chest.item.Length; s++)
                {
                    Item slot = chest.item[s];
                    if (slot is not null && !slot.IsAir && slot.type == lines[i].ItemId)
                        have += slot.stack;
                }

                if (have < lines[i].Count)
                    return false;
            }

            return true;
        }
    }
}
#endif
