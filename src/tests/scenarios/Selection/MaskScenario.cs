#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.SelectionTests
{
    /// <summary>IncludeTiles and ExcludeTiles update the inclusion mask.</summary>
    internal sealed class MaskScenario : IScenarioTest
    {
        public string Id => "selection.mask";
        public string Description => "IncludeTiles / ExcludeTiles update the selection mask.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Define 3x3, exclude one cell, re-include it, assert Included flags each step.

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            Point origin = ScenarioWorldHelpers.TileNearPlayer(player, offsetX: 12, offsetY: -4);
            selectionSystem.NewSelection(player);
            selectionSystem.SetPoints(player, origin, new Point(origin.X + 2, origin.Y + 2));

            Core.Selection selection = selectionSystem.GetSelection();
            if (selection is null || !selection.IsDefined)
            {
                reporter.Fail(Id, "Selection was not defined.");
                yield break;
            }

            var excludeCell = new Point(origin.X + 1, origin.Y + 1);
            selection.ExcludeTiles(new Rectangle(excludeCell.X, excludeCell.Y, 1, 1));
            if (!selection.TryGetTile(excludeCell, out Blueprint.BlueprintCell excluded) || excluded.Included)
            {
                reporter.Fail(Id, "ExcludeTiles did not clear the center cell.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            selection.IncludeTiles(new Rectangle(excludeCell.X, excludeCell.Y, 1, 1));
            if (!selection.TryGetTile(excludeCell, out Blueprint.BlueprintCell included) || !included.Included)
            {
                reporter.Fail(Id, "IncludeTiles did not restore the center cell.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            selectionSystem.ClearSelection(player);
            reporter.Pass(Id, "ExcludeTiles and IncludeTiles toggled the center cell.");
            yield break;
        }
    }
}
#endif
