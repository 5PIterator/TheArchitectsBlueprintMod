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
    /// <summary>MoveTo changes the target origin and leaves relative payload cells unchanged.</summary>
    internal sealed class projection_P03 : ITestCase
    {
        public string Id => "projection_P03";
        public string Description => "MoveTo changes target origin; relative payload cells stay";

        public TestCaseType Type => TestCaseType.Positive;

        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Set a stone payload, MoveTo a new origin, assert target moved and relative cell remains.

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
            Payload before = Payload.FromClone(projection.Payload);
            Point newOrigin = new(origin.X + 5, origin.Y + 2);
            projection.MoveTo(newOrigin);

            if (projection.TargetArea.X != newOrigin.X || projection.TargetArea.Y != newOrigin.Y)
            {
                reporter.Fail(
                    Id,
                    $"Expected target ({newOrigin.X},{newOrigin.Y}), got ({projection.TargetArea.X},{projection.TargetArea.Y}).");
                projectionSystem.RemoveProjection(projection);
                selectionSystem.ClearSelection(player);
                yield break;
            }

            if (!PayloadAssert.TryMatch(Id, reporter, before, projection.Payload, projection.TargetArea))
            {
                projectionSystem.RemoveProjection(projection);
                selectionSystem.ClearSelection(player);
                yield break;
            }

            projectionSystem.RemoveProjection(projection);
            selectionSystem.ClearSelection(player);
            reporter.Pass(Id, "MoveTo relocated the target and preserved relative payload cells.");
            yield break;
        }
    }
}
#endif
