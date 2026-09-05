#if DEBUG
#nullable enable

using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Imports mirror and legacy JSON payloads in-game.</summary>
    internal sealed class PayloadJsonImportScenarioTest : IScenarioTest
    {
        public string Id => "sc6";
        public string Description => "Imports mirror and legacy sparse JSON into defined payloads.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            if (!TryImport(ScenariosTestFixtures.MirrorDocumentJson, expectLegacy: false, out string? mirrorError))
            {
                reporter.Fail(Id, $"Mirror import failed: {mirrorError}");
                return;
            }

            if (!TryImport(ScenariosTestFixtures.LegacySparseJson, expectLegacy: true, out string? legacyError))
            {
                reporter.Fail(Id, $"Legacy import failed: {legacyError}");
                return;
            }

            reporter.Pass(Id, "Mirror and legacy JSON imports succeeded.");
        }

        private static bool TryImport(string json, bool expectLegacy, out string? error)
        {
            error = null;
            bool ok = PayloadJsonImport.TryImport(json, out Payload payload, out PayloadDocumentInfo info, out bool isLegacy, out error);
            if (!ok)
                return false;
            if (isLegacy != expectLegacy)
            {
                error = $"Expected isLegacy={expectLegacy}, got {isLegacy}.";
                return false;
            }
            if (!payload.IsDefined)
            {
                error = "Payload was not defined after import.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(info.ProjectName))
            {
                error = "Project name missing from document info.";
                return false;
            }

            return true;
        }
    }
}
#endif
