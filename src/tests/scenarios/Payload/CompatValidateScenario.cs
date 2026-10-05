#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PayloadTests
{
    /// <summary>Validate-only prepare keeps a stone payload and reports unresolvable tiles as incompatible.</summary>
    internal sealed class CompatValidateScenario : IScenarioTest
    {
        public string Id => "payload.compat";
        public string Description => "Validate-only reports clean stone and flags an unresolvable tile.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Check identity + clean report for stone, then a positive incompatible count for a bad cell.

            if (!ScenarioTestCells.TryCreateStonePayload(out Payload stonePayload, out _, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            Payload stoneResult = PayloadCompatibility.Prepare(
                stonePayload,
                PayloadPrepareMode.ValidateOnly,
                metadataModsLoaded: null,
                out PayloadCompatibilityReport stoneReport);

            if (!ReferenceEquals(stonePayload, stoneResult))
            {
                reporter.Fail(Id, "Validate-only prepare should return the same payload instance.");
                yield break;
            }

            if (stoneReport.TotalNonEmptyCells != 1 || stoneReport.IncompatibleCells != 0)
            {
                reporter.Fail(
                    Id,
                    $"Unexpected stone report: total={stoneReport.TotalNonEmptyCells}, incompatible={stoneReport.IncompatibleCells}.");
                yield break;
            }

            PayloadCell bad = ScenarioTestCells.CreateUnresolvableTileCell();
            Payload badPayload = Payload.FromSparse([new Payload.Entry(new Point(0, 0), bad)], 1, 1);
            PayloadCompatibility.Prepare(
                badPayload,
                PayloadPrepareMode.ValidateOnly,
                metadataModsLoaded: null,
                out PayloadCompatibilityReport badReport);

            if (badReport.TotalNonEmptyCells != 1 || badReport.IncompatibleCells != 1)
            {
                reporter.Fail(
                    Id,
                    $"Unexpected bad-cell report: total={badReport.TotalNonEmptyCells}, incompatible={badReport.IncompatibleCells}.");
                yield break;
            }

            reporter.Pass(Id, "Validate-only reports clean stone and one incompatible unresolvable tile.");
            yield break;
        }
    }
}
#endif
