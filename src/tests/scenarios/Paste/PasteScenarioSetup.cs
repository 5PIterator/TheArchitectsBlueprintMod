#if DEBUG
#nullable enable

using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PasteTests
{
    /// <summary>Shared setup for paste scenarios that create their own selection and projection.</summary>
    internal static class PasteScenarioSetup
    {
        /// <summary>Gives the local player a large stack of the given item in the first empty inventory slot.</summary>
        public static void GiveItem(Player player, int itemType, int stack = 999)
        {
            // --- Prefer an empty slot; otherwise overwrite the first inventory slot.
            for (int i = 0; i < player.inventory.Length; i++)
            {
                Item slot = player.inventory[i];
                if (slot is null || slot.IsAir)
                {
                    player.inventory[i] ??= new Item();
                    player.inventory[i].SetDefaults(itemType);
                    player.inventory[i].stack = stack;
                    return;
                }
            }

            player.inventory[0].SetDefaults(itemType);
            player.inventory[0].stack = stack;
        }

        /// <summary>Places a single stone tile in the world at the given coordinate.</summary>
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

        /// <summary>Creates a selection and projection over the given area, optionally capturing world tiles.</summary>
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

        /// <summary>Runs a paste, diffs expected vs world, then tears down the paste area.</summary>
        public static IEnumerator PasteDiffAndTeardown(
            Player player,
            Core.Projection projection,
            Payload expected,
            string scenarioId,
            IScenarioTestReporter reporter,
            string passMessage,
            bool alsoAssertSourceCleared = false,
            Point? sourceTile = null)
        {
            // --- Yielded paste, target diff, optional source-clear check, teardown, cleanup.

            bool pasteOk = false;
            string? pasteError = null;
            IEnumerator paste = ScenarioPasteHelpers.RunPasteAndAwait(
                projection,
                reporter,
                scenarioId,
                (ok, err) =>
                {
                    pasteOk = ok;
                    pasteError = err;
                });

            while (paste.MoveNext())
                yield return paste.Current;

            if (!pasteOk)
            {
                reporter.Fail(scenarioId, pasteError ?? "Paste execution failed.");
                Cleanup(player, projection);
                yield break;
            }

            Payload actual = Payload.FromWorld(projection.TargetArea);
            if (!ScenarioPayloadAssert.TryMatch(scenarioId, reporter, expected, actual, projection.TargetArea))
            {
                IEnumerator teardownFail = ScenarioPasteHelpers.TeardownPasteArea(
                    projection, expected.Width, expected.Height, reporter, scenarioId);
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
                    reporter.Fail(scenarioId, $"Cut paste left source tile at ({src.X},{src.Y}).");
                    IEnumerator teardownCut = ScenarioPasteHelpers.TeardownPasteArea(
                        projection, expected.Width, expected.Height, reporter, scenarioId);
                    while (teardownCut.MoveNext())
                        yield return teardownCut.Current;
                    Cleanup(player, projection);
                    yield break;
                }
            }

            IEnumerator teardown = ScenarioPasteHelpers.TeardownPasteArea(
                projection, expected.Width, expected.Height, reporter, scenarioId);
            while (teardown.MoveNext())
                yield return teardown.Current;

            Cleanup(player, projection);
            reporter.Pass(scenarioId, passMessage);
        }
    }
}
#endif
