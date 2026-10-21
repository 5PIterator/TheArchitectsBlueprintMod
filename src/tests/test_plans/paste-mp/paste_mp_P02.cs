#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteMpTests
{
    /// <summary>Topology-neutral MP cut paste: target matches and source clears using chest materials.</summary>
    internal sealed class paste_mp_P02 : ITestCase
    {
        public string Id => "paste-mp_P02";
        public string Description => "Multiplayer 1x1 cut paste: stocked chest, target matches, source clears";

        public TestCaseType Type => TestCaseType.Positive;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            if (!TestCaseGuards.RequireMultiplayerClient(Id, reporter))
                yield break;

            IEnumerator body = RunBody(player, reporter);
            IEnumerator run = PasteConfigFixture.RunWith(PasteConfigProfile.DefaultPaste, body);
            while (run.MoveNext())
                yield return run.Current;
        }

        private IEnumerator RunBody(Player player, ITestCaseReporter reporter)
        {
            MpCoordinator.GetStoneCells(out Point source, out Point target);
            Point chestOrigin = MpCoordinator.GetMaterialChestOrigin();

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

            if (!MpCoordinator.TryCreateMovedStoneProjection(
                    player,
                    source,
                    target,
                    out Core.Projection? projection,
                    out Payload expected,
                    out string? projectionError)
                || projection is null)
            {
                reporter.Fail(Id, projectionError ?? "Projection setup failed.");
                IEnumerator failCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (failCleanup.MoveNext())
                    yield return failCleanup.Current;
                yield break;
            }

            projection.SetCut(true);
            if (!projection.CutSource)
            {
                reporter.Fail(Id, "Failed to enable CutSource.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator cutFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (cutFailCleanup.MoveNext())
                    yield return cutFailCleanup.Current;
                yield break;
            }

            bool chestOk = false;
            string? chestError = null;
            IEnumerator chestPrepare = MpCoordinator.RequestChestPrepare(
                player,
                chestOrigin,
                projection,
                MpChestMode.Stock,
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
                reporter.Fail(Id, chestError ?? "Chest prepare failed.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator chestFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (chestFailCleanup.MoveNext())
                    yield return chestFailCleanup.Current;
                yield break;
            }

            bool pasteOk = false;
            string? pasteError = null;
            IEnumerator paste = PasteHelpers.RunPasteAndAwait(
                projection,
                reporter,
                Id,
                (ok, err) =>
                {
                    pasteOk = ok;
                    pasteError = err;
                });
            while (paste.MoveNext())
                yield return paste.Current;

            if (!pasteOk)
            {
                reporter.Fail(Id, pasteError ?? "Cut paste execution failed.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator pasteFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (pasteFailCleanup.MoveNext())
                    yield return pasteFailCleanup.Current;
                yield break;
            }

            bool matchOk = false;
            string? matchError = null;
            IEnumerator waitMatch = MpCoordinator.WaitForTargetMatch(
                projection.TargetArea,
                expected,
                reporter,
                Id,
                (ok, err) =>
                {
                    matchOk = ok;
                    matchError = err;
                });
            while (waitMatch.MoveNext())
                yield return waitMatch.Current;

            if (!matchOk)
            {
                Payload actual = Payload.FromWorld(projection.TargetArea);
                PayloadAssert.TryMatch(Id, reporter, expected, actual, projection.TargetArea);
                reporter.Fail(Id, matchError ?? "Cut paste target mismatch.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator matchFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (matchFailCleanup.MoveNext())
                    yield return matchFailCleanup.Current;
                yield break;
            }

            bool sourceEmptyOk = false;
            string? sourceEmptyError = null;
            IEnumerator waitSource = MpCoordinator.WaitForTileEmpty(
                source,
                reporter,
                Id,
                (ok, err) =>
                {
                    sourceEmptyOk = ok;
                    sourceEmptyError = err;
                });
            while (waitSource.MoveNext())
                yield return waitSource.Current;

            if (!sourceEmptyOk)
            {
                reporter.Fail(Id, sourceEmptyError ?? $"Cut paste left source tile at ({source.X},{source.Y}).");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator sourceFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (sourceFailCleanup.MoveNext())
                    yield return sourceFailCleanup.Current;
                yield break;
            }

            StonePasteFixture.Cleanup(player, projection);

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

            if (!cleanupOk)
            {
                reporter.Error(Id, cleanupError ?? "Cleanup failed.");
                yield break;
            }

            reporter.Pass(Id, "Multiplayer cut paste matched the target and cleared the source.");
        }
    }
}
#endif
