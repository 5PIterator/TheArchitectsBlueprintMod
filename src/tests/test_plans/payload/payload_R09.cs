#if DEBUG
#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Text;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PayloadTests
{
    /// <summary>
    /// Desired: export modsLoaded tracks content-owning mods only (vanilla stone → none).
    /// Fails while CreateExportMetadata still dumps the full session mod list.
    /// </summary>
    internal sealed class payload_R09 : ITestCase
    {
        public string Id => "payload_R09";
        public string Description => "Vanilla-only stone export must not list unused session mods";

        public TestCaseType Type => TestCaseType.Regression;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Build stone payload, create export metadata, require no unused session mods.

            if (!PayloadCellFixture.TryCreateStonePayload(out Payload stonePayload, out _, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            PayloadExportMetadata meta = PayloadTagIO.CreateExportMetadata(
                projectName: "CaseExportMods",
                playerName: player.name);

            IReadOnlyList<string>? modsLoaded = meta.ModsLoaded;
            if (modsLoaded is null || modsLoaded.Count == 0)
            {
                reporter.Pass(Id, "Export metadata ModsLoaded is empty for vanilla stone (content-aware).");
                yield break;
            }

            // Vanilla Tile_1 has no ModTile owner; any listed mod is session noise, not content tracking.
            var unexpected = new List<string>();
            for (int i = 0; i < modsLoaded.Count; i++)
            {
                string name = modsLoaded[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                unexpected.Add(name);
            }

            if (unexpected.Count == 0)
            {
                reporter.Pass(Id, "Export metadata ModsLoaded has no content-owning mods for vanilla stone.");
                yield break;
            }

            var detail = new StringBuilder();
            detail.Append("Export ModsLoaded still lists session mods for vanilla-only stone: ");
            for (int i = 0; i < unexpected.Count; i++)
            {
                if (i > 0)
                    detail.Append(", ");
                detail.Append(unexpected[i]);
            }

            detail.Append(". Expected null/empty until per-tile export tracking lands.");
            reporter.Fail(Id, detail.ToString());
            yield break;
        }
    }
}
#endif
