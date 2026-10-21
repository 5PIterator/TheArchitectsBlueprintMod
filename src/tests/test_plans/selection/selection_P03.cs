#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases.SelectionTests
{
    /// <summary>TranslateSelection shifts the origin and keeps size.</summary>
    internal sealed class selection_P03 : ITestCase
    {
        public string Id => "selection_P03";
        public string Description => "TranslateSelection shifts origin and keeps size";

        public TestCaseType Type => TestCaseType.Positive;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Define 2x2, translate by (+3,-1), assert new origin and unchanged size.

            SelectionSystem selectionSystem = ModContent.GetInstance<SelectionSystem>();
            Point origin = TestChamberSystem.Instance.SelectionOrigin;
            selectionSystem.NewSelection(player);
            selectionSystem.SetPoints(player, origin, new Point(origin.X + 1, origin.Y + 1));

            Core.Selection before = selectionSystem.GetSelection();
            if (before is null || !before.IsDefined)
            {
                reporter.Fail(Id, "Selection was not defined.");
                yield break;
            }

            int width = before.Area.Width;
            int height = before.Area.Height;
            var delta = new Point(3, -1);
            selectionSystem.TranslateSelection(player, delta);

            Core.Selection after = selectionSystem.GetSelection();
            if (after is null || !after.IsDefined)
            {
                reporter.Fail(Id, "Selection undefined after TranslateSelection.");
                yield break;
            }

            if (after.Area.X != origin.X + delta.X || after.Area.Y != origin.Y + delta.Y)
            {
                reporter.Fail(
                    Id,
                    $"Expected origin ({origin.X + delta.X},{origin.Y + delta.Y}), got ({after.Area.X},{after.Area.Y}).");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            if (after.Area.Width != width || after.Area.Height != height)
            {
                reporter.Fail(Id, $"Size changed after translate: {after.Area.Width}x{after.Area.Height}.");
                selectionSystem.ClearSelection(player);
                yield break;
            }

            selectionSystem.ClearSelection(player);
            reporter.Pass(Id, "TranslateSelection moved origin and preserved size.");
            yield break;
        }
    }
}
#endif
