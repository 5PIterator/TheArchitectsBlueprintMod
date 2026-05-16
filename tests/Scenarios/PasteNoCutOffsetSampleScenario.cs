#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class PasteNoCutOffsetSampleScenario(PayloadSample sample) : ITestScenario
    {
        public string Id => $"paste_no_cut_{sample.Slug}";
        public string Description => $"Capture {sample.FileName}, offset paste without cut; source retains payload.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                reporter.Fail("environment", "Paste scenario requires single-player.");
                return;
            }

            if (!PayloadSampleCatalog.TryLoad(sample, out Payload payload, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            if (PayloadScenarioChecks.CountCellsWithTile(payload) == 0)
            {
                reporter.Info($"[{Id}] skipped — payload has no tiles to place for capture.");
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(payload);
            int offsetX = PasteScenarioOffsets.TargetOffsetTilesX(payload);

            ScenarioHarness.RunProjection(
                reporter,
                player,
                Id,
                config,
                chain => WorldScenarioSetup.PlacePayload(chain, chain.WorldTileBounds, payload),
                (chain, r) =>
            {
                Projection projection = chain.Projection!;
                Rectangle sourceArea = projection.SourceArea;

                projection.MoveTo(new Point(sourceArea.X + offsetX, sourceArea.Y));
                projection.SetCut(false);

                if (!PayloadScenarioChecks.CutSourceEquals(projection, expected: false, out string? error))
                {
                    r.Fail("cut source", error);
                    return false;
                }

                r.Pass("cut source off");

                if (!PasteScenarioHelpers.RunPasteToCompletion(chain, projection, r, out error))
                {
                    r.Fail("paste", error);
                    return false;
                }

                if (!PayloadScenarioChecks.WorldAreaMatchesPayload(sourceArea, projection.Payload, out error))
                {
                    r.Fail("source retained", error);
                    return false;
                }

                r.Pass("source area still matches payload");

                if (!PayloadScenarioChecks.TargetWorldMatchesPayload(projection, out error))
                {
                    r.Fail("target tiles", error);
                    return false;
                }

                r.Pass("target matches payload");
                return true;
            });
        }
    }
}
#endif
