#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Server;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteMpTests
{
    /// <summary>
    /// Topology-neutral MP lifecycle: second paste hits PlayerOperationLimit, first paste cancels, both unlock, no writes.
    /// </summary>
    internal sealed class paste_mp_B04 : ITestCase
    {
        public string Id => "paste-mp_B04";
        public string Description => "Multiplayer paste lifecycle: operation-limit reject, cancel, unlock, no tile writes";

        public TestCaseType Type => TestCaseType.Boundary;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- RetryOnMissing config → two projections, empty chest, reject B, cancel A.

            if (!TestCaseGuards.RequireMultiplayerClient(Id, reporter))
                yield break;

            MpCoordinator.GetStoneCells(out Point source, out Point target);
            Point targetB = TestChamberSystem.Instance.StoneTargetB;
            Point chestOrigin = MpCoordinator.GetMaterialChestOrigin();

            IEnumerator body = RunBody(player, reporter, source, target, targetB, chestOrigin);
            IEnumerator run = PasteConfigFixture.RunWith(PasteConfigProfile.RetryOnMissing, body);
            while (run.MoveNext())
                yield return run.Current;
        }

        private IEnumerator RunBody(
            Player player,
            ITestCaseReporter reporter,
            Point source,
            Point target,
            Point targetB,
            Point chestOrigin)
        {
            // --- Nested enumerator keeps yields out of the config restore try/finally.

            bool setupOk = false;
            string? setupError = null;
            IEnumerator worldSetup = MpCoordinator.RequestWorldSetup(
                player,
                source,
                target,
                placeStoneAtSource: true,
                reporter,
                Id,
                (ok, err) =>
                {
                    setupOk = ok;
                    setupError = err;
                });
            while (worldSetup.MoveNext())
                yield return worldSetup.Current;

            if (!setupOk)
            {
                reporter.Fail(Id, setupError ?? "World setup failed.");
                yield break;
            }

            bool setupBOk = false;
            string? setupBError = null;
            IEnumerator worldSetupB = MpCoordinator.RequestWorldSetup(
                player,
                source,
                targetB,
                placeStoneAtSource: true,
                reporter,
                Id,
                (ok, err) =>
                {
                    setupBOk = ok;
                    setupBError = err;
                });
            while (worldSetupB.MoveNext())
                yield return worldSetupB.Current;

            if (!setupBOk)
            {
                reporter.Fail(Id, setupBError ?? "Secondary world setup failed.");
                IEnumerator setupBFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (setupBFailCleanup.MoveNext())
                    yield return setupBFailCleanup.Current;
                yield break;
            }

            if (!MpCoordinator.TryCreateMovedStoneProjection(
                    player,
                    source,
                    target,
                    out Core.Projection? projectionA,
                    out _,
                    out string? projectionAError)
                || projectionA is null)
            {
                reporter.Fail(Id, projectionAError ?? "Projection A setup failed.");
                IEnumerator failCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (failCleanup.MoveNext())
                    yield return failCleanup.Current;
                yield break;
            }

            if (!StonePasteFixture.TryCreateProjection(
                    player,
                    new Rectangle(source.X, source.Y, 1, 1),
                    captureWorldPayload: true,
                    out Core.Projection? projectionB,
                    out string? projectionBError)
                || projectionB is null
                || !projectionB.HasPayload)
            {
                reporter.Fail(Id, projectionBError ?? "Projection B setup failed.");
                StonePasteFixture.Cleanup(player, projectionA);
                IEnumerator failBCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (failBCleanup.MoveNext())
                    yield return failBCleanup.Current;
                yield break;
            }

            projectionB.MoveTo(targetB);

            bool chestOk = false;
            string? chestError = null;
            IEnumerator chestPrepare = MpCoordinator.RequestChestPrepare(
                player,
                chestOrigin,
                projectionA,
                MpChestMode.Empty,
                reporter,
                Id,
                (ok, err) =>
                {
                    chestOk = ok;
                    chestError = err;
                });
            while (chestPrepare.MoveNext())
                yield return chestPrepare.Current;

            if (!chestOk)
            {
                reporter.Fail(Id, chestError ?? "Empty chest prepare failed.");
                StonePasteFixture.Cleanup(player, projectionA);
                StonePasteFixture.Cleanup(player, projectionB);
                IEnumerator chestFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (chestFailCleanup.MoveNext())
                    yield return chestFailCleanup.Current;
                yield break;
            }

            PastingSystem pastingSystem = ModContent.GetInstance<PastingSystem>();
            bool remote = MpCoordinator.UsesRemotePasteRequestPath();
            if (remote)
                PasteNetworking.ClearClientAckObservations();

            if (!pastingSystem.StartPaste(projectionA, out PasteRejectReason startAReason))
            {
                reporter.Fail(Id, $"Paste A rejected unexpectedly: {startAReason}.");
                StonePasteFixture.Cleanup(player, projectionA);
                StonePasteFixture.Cleanup(player, projectionB);
                IEnumerator startAFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (startAFailCleanup.MoveNext())
                    yield return startAFailCleanup.Current;
                yield break;
            }

            int requestA = projectionA.Id;

            if (remote)
            {
                bool accepted = false;
                for (int i = 0; i < 180; i++)
                {
                    if (requestA > 0
                        && PasteNetworking.TryGetLatestAck(requestA, PasteAckKind.Accepted, out _))
                    {
                        accepted = true;
                        break;
                    }

                    if (!projectionA.IsLocked)
                        break;

                    yield return null;
                }

                if (!accepted)
                {
                    reporter.Fail(Id, "Paste A never received Accepted ACK before starting B.");
                    pastingSystem.StopPaste(projectionA);
                    StonePasteFixture.Cleanup(player, projectionA);
                    StonePasteFixture.Cleanup(player, projectionB);
                    IEnumerator acceptFailCleanup = MpCoordinator.RequestCleanup(
                        player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                    while (acceptFailCleanup.MoveNext())
                        yield return acceptFailCleanup.Current;
                    yield break;
                }
            }
            else if (!pastingSystem.HasActiveOperationFor(projectionA))
            {
                reporter.Fail(Id, "Paste A was not active before starting B.");
                StonePasteFixture.Cleanup(player, projectionA);
                StonePasteFixture.Cleanup(player, projectionB);
                IEnumerator activeFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (activeFailCleanup.MoveNext())
                    yield return activeFailCleanup.Current;
                yield break;
            }

            bool rejectOk = false;
            string? rejectError = null;
            IEnumerator expectReject = PasteHelpers.RunPasteExpectReject(
                projectionB,
                PasteRejectReason.PlayerOperationLimit,
                reporter,
                Id,
                (ok, err) =>
                {
                    rejectOk = ok;
                    rejectError = err;
                });
            while (expectReject.MoveNext())
                yield return expectReject.Current;

            if (!rejectOk)
            {
                reporter.Fail(Id, rejectError ?? "Second paste did not hit PlayerOperationLimit.");
                pastingSystem.StopPaste(projectionA);
                StonePasteFixture.Cleanup(player, projectionA);
                StonePasteFixture.Cleanup(player, projectionB);
                IEnumerator rejectFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (rejectFailCleanup.MoveNext())
                    yield return rejectFailCleanup.Current;
                yield break;
            }

            pastingSystem.StopPaste(projectionA);

            for (int i = 0; i < 180; i++)
            {
                if (!projectionA.IsLocked && !projectionB.IsLocked)
                    break;

                yield return null;
            }

            if (projectionA.IsLocked || projectionB.IsLocked)
            {
                reporter.Fail(
                    Id,
                    $"Projection unlock failed (A locked={projectionA.IsLocked}, B locked={projectionB.IsLocked}).");
                StonePasteFixture.Cleanup(player, projectionA);
                StonePasteFixture.Cleanup(player, projectionB);
                IEnumerator unlockFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (unlockFailCleanup.MoveNext())
                    yield return unlockFailCleanup.Current;
                yield break;
            }

            if (remote && requestA > 0)
            {
                bool sawCancelOrFinish =
                    PasteNetworking.TryGetLatestAck(requestA, PasteAckKind.Cancelled, out _)
                    || PasteNetworking.TryGetLatestAck(requestA, PasteAckKind.Finished, out _);
                reporter.Info(
                    sawCancelOrFinish
                        ? $"{Id}: paste A observed Cancelled or Finished ACK."
                        : $"{Id}: paste A unlocked without Cancelled/Finished ACK (local teardown race).");
            }

            if (Main.tile[target.X, target.Y].HasTile || Main.tile[targetB.X, targetB.Y].HasTile)
            {
                reporter.Fail(Id, "Lifecycle paste wrote tiles unexpectedly.");
                StonePasteFixture.Cleanup(player, projectionA);
                StonePasteFixture.Cleanup(player, projectionB);
                IEnumerator writeFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (writeFailCleanup.MoveNext())
                    yield return writeFailCleanup.Current;
                yield break;
            }

            StonePasteFixture.Cleanup(player, projectionA);
            StonePasteFixture.Cleanup(player, projectionB);

            bool cleanupOk = false;
            string? cleanupError = null;
            IEnumerator cleanup = MpCoordinator.RequestCleanup(
                player,
                source,
                target,
                chestOrigin,
                reporter,
                Id,
                (ok, err) =>
                {
                    cleanupOk = ok;
                    cleanupError = err;
                });
            while (cleanup.MoveNext())
                yield return cleanup.Current;

            bool cleanupBOk = false;
            IEnumerator cleanupB = MpCoordinator.RequestCleanup(
                player,
                source,
                targetB,
                chestOrigin,
                reporter,
                Id,
                (ok, _) => cleanupBOk = ok);
            while (cleanupB.MoveNext())
                yield return cleanupB.Current;

            if (!cleanupOk || !cleanupBOk)
            {
                reporter.Error(Id, cleanupError ?? "Cleanup failed.");
                yield break;
            }

            reporter.Pass(Id, "Multiplayer lifecycle rejected the second paste, cancelled the first, and left no writes.");
        }
    }
}
#endif
