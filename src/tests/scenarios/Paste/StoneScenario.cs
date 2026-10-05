#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PasteTests
{
    /// <summary>Places one stone, captures it, pastes onto air, and diffs the target cell.</summary>
    internal sealed class StoneScenario : IScenarioTest
    {
        public string Id => "paste.stone";
        public string Description => "1x1 stone capture, paste onto air, and cell diff.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Place stone, capture via projection, move to air, give stone, paste, diff, teardown.

            if (!ScenarioTestGuards.RequireSinglePlayer(Id, reporter))
                yield break;

            Point source = ScenarioWorldHelpers.TileNearPlayer(player, offsetX: 6, offsetY: -8);
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

            if (!projection.HasPayload)
            {
                reporter.Fail(Id, "Projection has no payload after capture.");
                PasteScenarioSetup.Cleanup(player, projection);
                PasteScenarioSetup.ClearTile(source);
                yield break;
            }

            Payload expected = Payload.FromClone(projection.Payload);
            projection.MoveTo(target);
            PasteScenarioSetup.GiveItem(player, ItemID.StoneBlock);

            IEnumerator run = PasteScenarioSetup.PasteDiffAndTeardown(
                player,
                projection,
                expected,
                Id,
                reporter,
                "1x1 stone paste matched the captured cell.");
            while (run.MoveNext())
                yield return run.Current;

            PasteScenarioSetup.ClearTile(source);
            PasteScenarioSetup.ClearTile(target);
        }
    }
}
#endif
