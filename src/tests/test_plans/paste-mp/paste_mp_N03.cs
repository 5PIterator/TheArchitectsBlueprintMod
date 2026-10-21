#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteMpTests
{
    /// <summary>Topology-neutral MP paste with an empty material chest; target stays air and projection unlocks.</summary>
    internal sealed class paste_mp_N03 : ITestCase
    {
        public string Id => "paste-mp_N03";
        public string Description => "Multiplayer paste with empty material chest: no place, unlock";

        public TestCaseType Type => TestCaseType.Negative;


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
                    out _,
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

            bool chestOk = false;
            string? chestError = null;
            IEnumerator chestPrepare = MpCoordinator.RequestChestPrepare(
                player,
                chestOrigin,
                projection,
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
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator chestFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (chestFailCleanup.MoveNext())
                    yield return chestFailCleanup.Current;
                yield break;
            }

            bool pasteFinished = false;
            string? pasteError = null;
            IEnumerator paste = PasteHelpers.RunPasteAndAwait(
                projection,
                reporter,
                Id,
                (ok, err) =>
                {
                    pasteFinished = ok;
                    pasteError = err;
                },
                maxSteps: 180);
            while (paste.MoveNext())
                yield return paste.Current;

            for (int i = 0; i < 30; i++)
                yield return null;

            Tile targetTile = Main.tile[target.X, target.Y];
            if (targetTile.HasTile)
            {
                reporter.Fail(
                    Id,
                    $"Paste placed tile type {targetTile.TileType} at ({target.X},{target.Y}) without chest materials.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator placedFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (placedFailCleanup.MoveNext())
                    yield return placedFailCleanup.Current;
                yield break;
            }

            if (projection.IsLocked)
            {
                reporter.Fail(Id, "Projection remained locked after no-materials paste.");
                StonePasteFixture.Cleanup(player, projection);
                IEnumerator lockFailCleanup = MpCoordinator.RequestCleanup(
                    player, source, target, chestOrigin, reporter, Id, (_, _) => { });
                while (lockFailCleanup.MoveNext())
                    yield return lockFailCleanup.Current;
                yield break;
            }

            reporter.Info(
                pasteFinished
                    ? $"{Id}: paste finished without placing (missing materials dropped place tasks)."
                    : $"{Id}: paste did not finish ({pasteError}); target stayed air and unlocked.");

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

            reporter.Pass(Id, "Multiplayer paste left the target empty without chest materials.");
        }
    }
}
#endif
