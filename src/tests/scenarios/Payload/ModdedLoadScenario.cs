#if DEBUG
#nullable enable

using System.Collections;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PayloadTests
{
    /// <summary>Loads the massive modded fixture and asserts the document is defined.</summary>
    internal sealed class ModdedLoadScenario : IScenarioTest
    {
        private const string FixtureFileName = "SS_MassiveModded.tab";

        public string Id => "payload.modded";
        public string Description => "Loads SS_MassiveModded.tab and asserts a defined payload.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Resolve the fixture path, read the document, and require defined non-empty dimensions.

            if (!ScenarioPayloadFixturePaths.TryResolve(FixtureFileName, out string fixturePath, out string? pathError))
            {
                reporter.Fail(Id, pathError ?? "Fixture path resolve failed.");
                yield break;
            }

            if (!PayloadSerialization.TryReadPayloadFromFile(fixturePath, out Payload payload, out PayloadDocumentInfo info, out string? readError))
            {
                reporter.Fail(Id, readError ?? "Fixture payload read failed.");
                yield break;
            }

            if (!payload.IsDefined || payload.Width <= 0 || payload.Height <= 0)
            {
                reporter.Fail(Id, "Modded fixture did not produce a defined payload.");
                yield break;
            }

            reporter.Pass(
                Id,
                $"Loaded {FixtureFileName} ({payload.Width}x{payload.Height}, sparse={payload.Entries.Count}, project={info.ProjectName}).");
            yield break;
        }
    }
}
#endif
