#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.ProjectionTests
{
    /// <summary>NewProjection from a selection produces a matching payload size.</summary>
    internal sealed class CopyScenario : IScenarioTest
    {
        public string Id => "projection.copy";
        public string Description => "NewProjection captures a selection into a matching payload size.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Build a 2x2 selection, NewProjection with capture, assert payload size, then remove.

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            ProjectionSystem projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            Point origin = ScenarioWorldHelpers.TileNearPlayer(player, offsetX: 20, offsetY: -6);

            selectionSystem.NewSelection(player);
            selectionSystem.SetPoints(player, origin, new Point(origin.X + 1, origin.Y + 1));
            Core.Selection selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                reporter.Fail(Id, "Selection was not defined.");
                yield break;
            }

            Core.Projection? projection = projectionSystem.NewProjection(player, selection, captureWorldPayload: true);
            if (projection is null || !projection.IsDefined)
            {
                reporter.Fail(Id, "NewProjection failed.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            if (!projection.HasPayload || projection.Payload.Width != 2 || projection.Payload.Height != 2)
            {
                reporter.Fail(
                    Id,
                    $"Expected payload 2x2, got {(projection.HasPayload ? $"{projection.Payload.Width}x{projection.Payload.Height}" : "none")}.");
                projectionSystem.RemoveProjection(projection);
                selectionSystem.ClearSelection(player);
                yield break;
            }

            projectionSystem.RemoveProjection(projection);
            selectionSystem.ClearSelection(player);
            reporter.Pass(Id, "NewProjection captured a 2x2 payload.");
            yield break;
        }
    }
}
#endif
