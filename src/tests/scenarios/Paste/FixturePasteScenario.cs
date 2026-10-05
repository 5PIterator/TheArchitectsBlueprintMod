#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PasteTests
{
    /// <summary>Shared fixture-load paste flow for layer and medium scenarios.</summary>
    internal static class FixturePasteScenario
    {
        /// <summary>Loads a .tab fixture onto a new projection, pastes, diffs, and tears down.</summary>
        public static IEnumerator Run(
            Player player,
            IScenarioTestReporter reporter,
            string scenarioId,
            string fixtureFileName,
            int offsetX,
            int offsetY,
            string passMessage)
        {
            // --- Resolve fixture, create projection shell, SetPayload, paste+diff+teardown.

            if (!ScenarioPayloadFixturePaths.TryResolve(fixtureFileName, out string fixturePath, out string? pathError))
            {
                reporter.Fail(scenarioId, pathError ?? "Fixture path resolve failed.");
                yield break;
            }

            if (!PayloadSerialization.TryReadPayloadFromFile(
                    fixturePath,
                    out Payload source,
                    out PayloadDocumentInfo info,
                    out string? readError))
            {
                reporter.Fail(scenarioId, readError ?? "Fixture payload read failed.");
                yield break;
            }

            if (!source.IsDefined)
            {
                reporter.Fail(scenarioId, "Fixture payload is not defined.");
                yield break;
            }

            Point origin = ScenarioWorldHelpers.TileNearPlayer(player, offsetX, offsetY);
            var area = new Rectangle(origin.X, origin.Y, source.Width, source.Height);
            if (!PasteScenarioSetup.TryCreateProjection(
                    player,
                    area,
                    captureWorldPayload: false,
                    out Core.Projection? projection,
                    out string? setupError)
                || projection is null)
            {
                reporter.Fail(scenarioId, setupError ?? "Projection setup failed.");
                yield break;
            }

            projection.SetPayload(source, info.ModsLoaded);
            if (!projection.HasPayload)
            {
                reporter.Fail(scenarioId, "Projection payload is not defined after fixture load.");
                PasteScenarioSetup.Cleanup(player, projection);
                yield break;
            }

            Payload expected = Payload.FromClone(projection.Payload);
            reporter.Info(
                $"{scenarioId}: fixture loaded from {fixturePath} ({expected.Width}x{expected.Height}, sparse={expected.Entries.Count}).");

            IEnumerator run = PasteScenarioSetup.PasteDiffAndTeardown(
                player,
                projection,
                expected,
                scenarioId,
                reporter,
                passMessage);
            while (run.MoveNext())
                yield return run.Current;
        }
    }
}
#endif
