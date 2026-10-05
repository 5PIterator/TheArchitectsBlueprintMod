#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.SelectionTests
{
    /// <summary>SetPoints defines expected bounds; ClearSelection leaves the selection undefined.</summary>
    internal sealed class BoundsScenario : IScenarioTest
    {
        public string Id => "selection.bounds";
        public string Description => "SetPoints defines width/height; ClearSelection clears bounds.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- NewSelection, SetPoints to a 4x3 rect, assert size, then ClearSelection.

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            Point origin = ScenarioWorldHelpers.TileNearPlayer(player, offsetX: 8, offsetY: -4);
            selectionSystem.NewSelection(player);
            selectionSystem.SetPoints(player, origin, new Point(origin.X + 3, origin.Y + 2));

            Core.Selection selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                reporter.Fail(Id, "Selection was not defined after SetPoints.");
                yield break;
            }

            if (selection.Area.Width != 4 || selection.Area.Height != 3)
            {
                reporter.Fail(Id, $"Expected 4x3, got {selection.Area.Width}x{selection.Area.Height}.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            selectionSystem.ClearSelection(player);
            Core.Selection afterClear = selectionSystem.GetSelection();
            if (afterClear is not null && afterClear.IsDefined)
            {
                reporter.Fail(Id, "Selection remained defined after ClearSelection.");
                yield break;
            }

            reporter.Pass(Id, "SetPoints defined 4x3 and ClearSelection cleared it.");
            yield break;
        }
    }
}
#endif
