#if DEBUG
#nullable enable

using System.Collections;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PayloadTests
{
    /// <summary>ComputeMissingMods and ValidateOnly report missing-mod metadata correctly.</summary>
    internal sealed class payload_N08 : ITestCase
    {
        public string Id => "payload_N08";
        public string Description => "ComputeMissingMods flags unknown mods; ValidateOnly surfaces MissingModsFromMetadata";

        public TestCaseType Type => TestCaseType.Negative;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Fake missing mod must appear; this mod's name must not; Prepare mirrors the list.

            const string fakeModName = "TAB_CaseFakeMissingMod_DoNotLoad";

            string? thisModName = null;
            try
            {
                thisModName = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>().Name;
            }
            catch
            {
                // Fall through; still validate the fake-mod path.
            }

            IReadOnlyList<string> probeList = string.IsNullOrEmpty(thisModName)
                ? [fakeModName]
                : [fakeModName, thisModName];

            IReadOnlyList<string> missing = PayloadCompatibility.ComputeMissingMods(probeList);
            if (!ContainsOrdinalIgnoreCase(missing, fakeModName))
            {
                reporter.Fail(Id, $"Expected ComputeMissingMods to flag '{fakeModName}'.");
                yield break;
            }

            if (!string.IsNullOrEmpty(thisModName) && ContainsOrdinalIgnoreCase(missing, thisModName))
            {
                reporter.Fail(Id, $"ComputeMissingMods incorrectly flagged loaded mod '{thisModName}'.");
                yield break;
            }

            if (!PayloadCellFixture.TryCreateStonePayload(out Payload stonePayload, out _, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            PayloadCompatibility.Prepare(
                stonePayload,
                PayloadPrepareMode.ValidateOnly,
                metadataModsLoaded: probeList,
                out PayloadCompatibilityReport report);

            if (!ContainsOrdinalIgnoreCase(report.MissingModsFromMetadata, fakeModName))
            {
                reporter.Fail(
                    Id,
                    $"ValidateOnly report missing mods did not include '{fakeModName}'.");
                yield break;
            }

            if (!string.IsNullOrEmpty(thisModName)
                && ContainsOrdinalIgnoreCase(report.MissingModsFromMetadata, thisModName))
            {
                reporter.Fail(
                    Id,
                    $"ValidateOnly report incorrectly listed loaded mod '{thisModName}' as missing.");
                yield break;
            }

            if (report.IncompatibleCells != 0)
            {
                reporter.Fail(
                    Id,
                    $"Stone ValidateOnly should have 0 incompatible cells, got {report.IncompatibleCells}.");
                yield break;
            }

            reporter.Pass(
                Id,
                "ComputeMissingMods and ValidateOnly report flag unknown mods only.");
            yield break;
        }

        private static bool ContainsOrdinalIgnoreCase(IReadOnlyList<string> values, string needle)
        {
            // --- Linear scan with case-insensitive ordinal compare.
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], needle, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
#endif
