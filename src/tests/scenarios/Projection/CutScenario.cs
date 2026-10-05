#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.ProjectionTests
{
    /// <summary>SetCut toggles CutSource; clearing the payload forces it off.</summary>
    internal sealed class CutScenario : IScenarioTest
    {
        public string Id => "projection.cut";
        public string Description => "SetCut toggles CutSource; empty payload forces it off.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Create a projection with payload, toggle cut on/off, clear payload, assert cut off.

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            ProjectionSystem projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            Point origin = ScenarioWorldHelpers.TileNearPlayer(player, offsetX: 24, offsetY: -6);

            selectionSystem.NewSelection(player);
            selectionSystem.SetPoints(player, origin, origin);
            Core.Selection selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                reporter.Fail(Id, "Selection was not defined.");
                yield break;
            }

            if (!ScenarioTestCells.TryCreateStonePayload(out Payload stone, out _, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            Core.Projection? projection = projectionSystem.NewProjection(player, selection, captureWorldPayload: false);
            if (projection is null)
            {
                reporter.Fail(Id, "NewProjection failed.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            projection.SetPayload(stone);
            projection.SetCut(true);
            if (!projection.CutSource)
            {
                reporter.Fail(Id, "SetCut(true) did not enable CutSource.");
                projectionSystem.RemoveProjection(projection);
                selectionSystem.ClearSelection(player);
                yield break;
            }

            projection.SetCut(false);
            if (projection.CutSource)
            {
                reporter.Fail(Id, "SetCut(false) did not disable CutSource.");
                projectionSystem.RemoveProjection(projection);
                selectionSystem.ClearSelection(player);
                yield break;
            }

            projection.SetCut(true);
            projection.SetPayload(Payload.Empty);
            if (projection.CutSource)
            {
                reporter.Fail(Id, "Empty payload left CutSource enabled.");
                projectionSystem.RemoveProjection(projection);
                selectionSystem.ClearSelection(player);
                yield break;
            }

            projectionSystem.RemoveProjection(projection);
            selectionSystem.ClearSelection(player);
            reporter.Pass(Id, "SetCut toggled and empty payload forced CutSource off.");
            yield break;
        }
    }
}
#endif
