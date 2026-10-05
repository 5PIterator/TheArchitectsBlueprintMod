#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PasteTests
{
    /// <summary>Paste with cut enabled fills the target and clears the source cell.</summary>
    internal sealed class CutScenario : IScenarioTest
    {
        public string Id => "paste.cut";
        public string Description => "1x1 cut paste: target matches stone and source clears.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Capture stone, enable cut, paste onto air, assert target match and source clear.

            if (!ScenarioTestGuards.RequireSinglePlayer(Id, reporter))
                yield break;

            Point source = ScenarioWorldHelpers.TileNearPlayer(player, offsetX: 6, offsetY: -10);
            Point target = new(source.X + 4, source.Y);
            PasteScenarioSetup.ClearTile(target);

            if (!PasteScenarioSetup.PlaceStone(source, out string? placeError))
            {
                reporter.Fail(Id, placeError ?? "Stone placement failed.");
                yield break;
            }

            if (!PasteScenarioSetup.TryCreateProjection(
                    player,
                    new Rectangle(source.X, source.Y, 1, 1),
                    captureWorldPayload: true,
                    out Core.Projection? projection,
                    out string? setupError)
                || projection is null)
            {
                reporter.Fail(Id, setupError ?? "Projection setup failed.");
                PasteScenarioSetup.ClearTile(source);
                yield break;
            }

            Payload expected = Payload.FromClone(projection.Payload);
            projection.SetCut(true);
            if (!projection.CutSource)
            {
                reporter.Fail(Id, "Failed to enable CutSource.");
                PasteScenarioSetup.Cleanup(player, projection);
                PasteScenarioSetup.ClearTile(source);
                yield break;
            }

            projection.MoveTo(target);
            PasteScenarioSetup.GiveItem(player, ItemID.StoneBlock);

            IEnumerator run = PasteScenarioSetup.PasteDiffAndTeardown(
                player,
                projection,
                expected,
                Id,
                reporter,
                "Cut paste matched the target and cleared the source.",
                alsoAssertSourceCleared: true,
                sourceTile: source);
            while (run.MoveNext())
                yield return run.Current;

            PasteScenarioSetup.ClearTile(source);
            PasteScenarioSetup.ClearTile(target);
        }
    }
}
#endif
