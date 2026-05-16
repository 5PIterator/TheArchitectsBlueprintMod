#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class SelectionBoundsSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"selection_bounds_{sample.Slug}";
        public string Description => $"Selection bounds for {sample.FileName} ({sample.Slug}).";

        public void Run(ITestReporter reporter, Player player)
        {
            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            int expectedCells = payload.Width * payload.Height;
            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);
            ScenarioHarness.RunSelection(reporter, player, Id, config, (chain, r) =>
            {
                Selection selection = chain.Selection!;
                if (!BlueprintTestAssertions.VerifySelection(player, chain.WorldTileBounds, selection, out string? error))
                {
                    r.Fail("bounds", error);
                    return false;
                }

                r.Pass("bounds");

                if (selection.TileCount != expectedCells)
                {
                    r.Fail("tile count", $"Expected {expectedCells} cells, got {selection.TileCount}.");
                    return false;
                }

                for (int y = 0; y < selection.Area.Height; y++)
                {
                    for (int x = 0; x < selection.Area.Width; x++)
                    {
                        if (!selection.Tiles[x, y].Included)
                        {
                            r.Fail("inclusion", $"Cell ({x},{y}) is excluded.");
                            return false;
                        }
                    }
                }

                r.Pass($"all {expectedCells} cells included");
                return true;
            });
        }
    }
}
#endif
