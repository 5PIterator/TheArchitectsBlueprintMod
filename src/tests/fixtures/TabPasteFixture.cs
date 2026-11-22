#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>
    /// Test Fixture chain for .tab paste:
    /// FixtureLoaded → ProjectionCopied → RequirePlayerNearChamber → MaterialsGiven → PasteExecuted → Teardown.
    /// </summary>
    internal static class TabPasteFixture
    {
        /// <summary>Join at FixtureLoaded: resolve .tab, SetPayload at FixtureOrigin, paste, diff, teardown.</summary>
        public static IEnumerator Run(
            Player player,
            ITestCaseReporter reporter,
            string caseTag,
            string fixtureFileName,
            string passMessage)
        {
            // --- Resolve fixture, create projection at FixtureOrigin, SetPayload, paste+diff+teardown.

            if (!PayloadFixturePaths.TryResolve(fixtureFileName, out string fixturePath, out string? pathError))
            {
                reporter.Fail(caseTag, pathError ?? "Fixture path resolve failed.");
                yield break;
            }

            if (!PayloadSerialization.TryReadPayloadFromFile(
                    fixturePath,
                    out Payload source,
                    out PayloadDocumentInfo info,
                    out string? readError))
            {
                reporter.Fail(caseTag, readError ?? "Fixture payload read failed.");
                yield break;
            }

            if (!source.IsDefined)
            {
                reporter.Fail(caseTag, "Fixture payload is not defined.");
                yield break;
            }

            TestChamberSystem chamber = TestChamberSystem.Instance;
            Point origin = chamber.FixtureOrigin(source.Width, source.Height);
            var area = new Rectangle(origin.X, origin.Y, source.Width, source.Height);
            if (!StonePasteFixture.TryCreateProjection(
                    player,
                    area,
                    copyWorldPayload: false,
                    out Core.Projection? projection,
                    out string? setupError)
                || projection is null)
            {
                reporter.Fail(caseTag, setupError ?? "Projection setup failed.");
                yield break;
            }

            projection.SetPayload(source, info.ModsLoaded);
            if (!projection.HasPayload)
            {
                reporter.Fail(caseTag, "Projection payload is not defined after fixture load.");
                StonePasteFixture.Cleanup(player, projection);
                yield break;
            }

            if (!MpCoordinator.TryRequirePlayerNearChamber(player, projection.TargetArea, out string? reachError))
            {
                reporter.Fail(caseTag, reachError ?? "Player is outside paste inventory reach of the fixture target.");
                StonePasteFixture.Cleanup(player, projection);
                yield break;
            }

            Payload expected = Payload.FromClone(projection.Payload);
            reporter.Info(
                $"{caseTag}: fixture loaded from {fixturePath} ({expected.Width}x{expected.Height}, sparse={expected.Entries.Count}).");

            if (!InventoryHelpers.GiveRequiredItems(player, projection, reporter, caseTag))
            {
                StonePasteFixture.Cleanup(player, projection);
                yield break;
            }

            IEnumerator run = StonePasteFixture.PasteDiffAndTeardown(
                player,
                projection,
                expected,
                caseTag,
                reporter,
                passMessage);
            while (run.MoveNext())
                yield return run.Current;
        }
    }
}
#endif
