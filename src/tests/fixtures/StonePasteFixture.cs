#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>
    /// Test Fixture chain for stone paste (SP, listen host, or remote joiner):
    /// StoneWorldPrepared → ProjectionCaptured → ProjectionMoved → (Materials*) → PasteExecuted → Teardown.
    /// </summary>
    internal static class StonePasteFixture
    {
        /// <summary>Fixture node StoneWorldPrepared: place a single stone tile.</summary>
        public static bool PlaceStone(Point tile, out string? error)
        {
            // --- Clear the cell, then PlaceTile stone forced and mute.
            error = null;
            if (!WorldGen.InWorld(tile.X, tile.Y))
            {
                error = $"Tile out of world: ({tile.X},{tile.Y}).";
                return false;
            }

            WorldGen.KillTile(tile.X, tile.Y, fail: false, effectOnly: false, noItem: true);
            bool placed = WorldGen.PlaceTile(tile.X, tile.Y, TileID.Stone, mute: true, forced: true, plr: -1, style: 0);
            if (!placed || !Main.tile[tile.X, tile.Y].HasTile || Main.tile[tile.X, tile.Y].TileType != TileID.Stone)
            {
                error = $"Failed to place stone at ({tile.X},{tile.Y}).";
                return false;
            }

            return true;
        }

        /// <summary>Clears a single world tile without dropping items.</summary>
        public static void ClearTile(Point tile)
        {
            if (WorldGen.InWorld(tile.X, tile.Y))
                WorldGen.KillTile(tile.X, tile.Y, fail: false, effectOnly: false, noItem: true);
        }

        /// <summary>Fixture node ProjectionCaptured: selection + projection over an area.</summary>
        public static bool TryCreateProjection(
            Player player,
            Rectangle area,
            bool captureWorldPayload,
            out Core.Projection? projection,
            out string? error)
        {
            // --- NewSelection + SetBounds, then NewProjection from that selection.
            projection = null;
            error = null;
            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            ProjectionSystem projectionSystem = ModContent.GetInstance<ProjectionSystem>();

            selectionSystem.NewSelection(player);
            selectionSystem.SetBounds(player, area);
            Core.Selection selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                error = "Failed to define selection for paste setup.";
                return false;
            }

            projection = projectionSystem.NewProjection(player, selection, captureWorldPayload);
            if (projection is null || !projection.IsDefined)
            {
                error = "Failed to create projection for paste setup.";
                selectionSystem.ClearSelection(player);
                return false;
            }

            return true;
        }

        /// <summary>Removes the projection and clears the player's selection.</summary>
        public static void Cleanup(Player player, Core.Projection? projection)
        {
            // --- Unlock/remove projection if present, then clear selection state.
            if (projection is not null)
            {
                ProjectionSystem projectionSystem = ModContent.GetInstance<ProjectionSystem>();
                if (projection.IsLocked)
                    projection.Unlock();
                projectionSystem.RemoveProjection(projection);
            }

            ModContent.GetInstance<SelectionSystem>().ClearSelection(player);
        }

        /// <summary>
        /// Join at ProjectionMoved via authority world setup (SP / listen host / remote).
        /// Caller must Cleanup projection; use <see cref="ClearWorldCells"/> for chamber tiles.
        /// </summary>
        public static IEnumerator JoinProjectionMoved(
            Player player,
            ITestCaseReporter reporter,
            string caseTag,
            System.Action<bool, Core.Projection?, Payload, Point, Point, string?> onComplete)
        {
            // --- Authority place stone + clear target, then capture/move like paste-mp stone Cases.

            MpCoordinator.GetStoneCells(out Point source, out Point target);

            bool setupOk = false;
            string? setupError = null;
            IEnumerator worldSetup = MpCoordinator.RequestWorldSetup(
                player,
                source,
                target,
                placeStoneAtSource: true,
                reporter,
                caseTag,
                (ok, err) =>
                {
                    setupOk = ok;
                    setupError = err;
                });
            while (worldSetup.MoveNext())
                yield return worldSetup.Current;

            if (!setupOk)
            {
                onComplete(false, null, Payload.Empty, source, target, setupError ?? "World setup failed.");
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
                onComplete(false, null, Payload.Empty, source, target, projectionError ?? "Projection setup failed.");
                yield break;
            }

            onComplete(true, projection, expected, source, target, null);
        }

        /// <summary>Clears chamber stone cells (and material-chest slot) on the paste authority.</summary>
        public static IEnumerator ClearWorldCells(
            Player player,
            Point source,
            Point target,
            ITestCaseReporter reporter,
            string caseTag)
        {
            // --- Reuse MP cleanup so remote joiners do not leave authoritative residue.

            Point chestOrigin = MpCoordinator.GetMaterialChestOrigin();
            IEnumerator cleanup = MpCoordinator.RequestCleanup(
                player,
                source,
                target,
                chestOrigin,
                reporter,
                caseTag,
                (_, _) => { });
            while (cleanup.MoveNext())
                yield return cleanup.Current;
        }

        /// <summary>Runs a paste, diffs expected vs world, then tears down the paste area.</summary>
        public static IEnumerator PasteDiffAndTeardown(
            Player player,
            Core.Projection projection,
            Payload expected,
            string caseTag,
            ITestCaseReporter reporter,
            string passMessage,
            bool alsoAssertSourceCleared = false,
            Point? sourceTile = null)
        {
            // --- Yielded paste, target diff, optional source-clear check, teardown, cleanup.

            bool pasteOk = false;
            string? pasteError = null;
            IEnumerator paste = PasteHelpers.RunPasteAndAwait(
                projection,
                reporter,
                caseTag,
                (ok, err) =>
                {
                    pasteOk = ok;
                    pasteError = err;
                });

            while (paste.MoveNext())
                yield return paste.Current;

            if (!pasteOk)
            {
                reporter.Fail(caseTag, pasteError ?? "Paste execution failed.");
                Cleanup(player, projection);
                yield break;
            }

            Payload actual = Payload.FromWorld(projection.TargetArea);
            if (!PayloadAssert.TryMatch(caseTag, reporter, expected, actual, projection.TargetArea))
            {
                IEnumerator teardownFail = PasteHelpers.TeardownPasteArea(
                    projection, expected.Width, expected.Height, reporter, caseTag);
                while (teardownFail.MoveNext())
                    yield return teardownFail.Current;
                Cleanup(player, projection);
                yield break;
            }

            if (alsoAssertSourceCleared && sourceTile is Point src)
            {
                Tile source = Main.tile[src.X, src.Y];
                if (source.HasTile)
                {
                    reporter.Fail(caseTag, $"Cut paste left source tile at ({src.X},{src.Y}).");
                    IEnumerator teardownCut = PasteHelpers.TeardownPasteArea(
                        projection, expected.Width, expected.Height, reporter, caseTag);
                    while (teardownCut.MoveNext())
                        yield return teardownCut.Current;
                    Cleanup(player, projection);
                    yield break;
                }
            }

            IEnumerator teardown = PasteHelpers.TeardownPasteArea(
                projection, expected.Width, expected.Height, reporter, caseTag);
            while (teardown.MoveNext())
                yield return teardown.Current;

            Cleanup(player, projection);
            reporter.Pass(caseTag, passMessage);
        }

        /// <summary>
        /// Runs a paste that should not place tiles (missing materials); asserts the target stays air.
        /// Paste ops finish quickly when RetryOnMissingItems is false — they do not hang.
        /// </summary>
        public static IEnumerator PasteExpectNoPlaceAndCleanup(
            Player player,
            Core.Projection projection,
            string caseTag,
            ITestCaseReporter reporter,
            string passMessage,
            Point clearSource,
            Point clearTarget,
            int maxSteps = 180)
        {
            // --- Yield the paste, then require the target cell still empty.

            bool pasteFinished = false;
            string? pasteError = null;
            IEnumerator paste = PasteHelpers.RunPasteAndAwait(
                projection,
                reporter,
                caseTag,
                (ok, err) =>
                {
                    pasteFinished = ok;
                    pasteError = err;
                },
                maxSteps);

            while (paste.MoveNext())
                yield return paste.Current;

            Tile targetTile = Main.tile[clearTarget.X, clearTarget.Y];
            if (targetTile.HasTile)
            {
                reporter.Fail(
                    caseTag,
                    $"Paste placed tile type {targetTile.TileType} at ({clearTarget.X},{clearTarget.Y}) without required materials.");
                IEnumerator teardown = PasteHelpers.TeardownPasteArea(
                    projection, projection.Payload.Width, projection.Payload.Height, reporter, caseTag);
                while (teardown.MoveNext())
                    yield return teardown.Current;
                Cleanup(player, projection);
                IEnumerator clearFail = ClearWorldCells(player, clearSource, clearTarget, reporter, caseTag);
                while (clearFail.MoveNext())
                    yield return clearFail.Current;
                yield break;
            }

            reporter.Info(
                pasteFinished
                    ? $"{caseTag}: paste finished without placing (missing materials dropped place tasks)."
                    : $"{caseTag}: paste did not finish ({pasteError}); target stayed air.");
            Cleanup(player, projection);
            IEnumerator clear = ClearWorldCells(player, clearSource, clearTarget, reporter, caseTag);
            while (clear.MoveNext())
                yield return clear.Current;
            reporter.Pass(caseTag, passMessage);
        }
    }
}
#endif
