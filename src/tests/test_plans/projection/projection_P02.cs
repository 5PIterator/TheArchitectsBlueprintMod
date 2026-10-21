#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases.ProjectionTests
{
    /// <summary>SetCut toggles CutSource; clearing the payload forces it off.</summary>
    internal sealed class projection_P02 : ITestCase
    {
        public string Id => "projection_P02";
        public string Description => "SetCut toggles CutSource; empty payload forces it off";

        public TestCaseType Type => TestCaseType.Positive;

        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Create a projection with payload, toggle cut on/off, clear payload, assert cut off.

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            ProjectionSystem projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            Point origin = TestChamberSystem.Instance.ProjectionOrigin;

            selectionSystem.NewSelection(player);
            selectionSystem.SetPoints(player, origin, origin);
            Core.Selection selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                reporter.Fail(Id, "Selection was not defined.");
                yield break;
            }

            if (!PayloadCellFixture.TryCreateStonePayload(out Payload stone, out _, out string? cellError))
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
