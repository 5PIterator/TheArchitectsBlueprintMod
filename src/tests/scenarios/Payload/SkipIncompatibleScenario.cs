#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PayloadTests
{
    /// <summary>SkipIncompatible remaps an unresolvable tile and keeps the stone cell.</summary>
    internal sealed class SkipIncompatibleScenario : IScenarioTest
    {
        public string Id => "payload.skip";
        public string Description => "SkipIncompatible keeps stone and remaps an unresolvable tile.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Mix stone + bad cell, prepare with SkipIncompatible, assert report and sanitization.

            if (!ScenarioTestCells.TryCreateStone(out PayloadCell stone, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            PayloadCell bad = ScenarioTestCells.CreateUnresolvableTileCell();
            Payload source = Payload.FromSparse(
                [
                    new Payload.Entry(new Point(0, 0), stone),
                    new Payload.Entry(new Point(1, 0), bad),
                ],
                width: 2,
                height: 1);

            Payload result = PayloadCompatibility.Prepare(
                source,
                PayloadPrepareMode.SkipIncompatible,
                metadataModsLoaded: null,
                out PayloadCompatibilityReport report);

            if (report.TotalNonEmptyCells != 2 || report.IncompatibleCells != 1)
            {
                reporter.Fail(
                    Id,
                    $"Unexpected report: total={report.TotalNonEmptyCells}, incompatible={report.IncompatibleCells}.");
                yield break;
            }

            if (!result.TryGetCell(new Point(0, 0), out PayloadCell kept) || kept.Block.Type != stone.Block.Type)
            {
                reporter.Fail(Id, "Stone cell was not preserved.");
                yield break;
            }

            if (!result.TryGetCell(new Point(1, 0), out PayloadCell remapped) || !PayloadCompatibility.IsCellCompatible(remapped))
            {
                reporter.Fail(Id, "Unresolvable cell was not remapped to a compatible placeholder.");
                yield break;
            }

            if (remapped.Block.Type == bad.Block.Type)
            {
                reporter.Fail(Id, "Unresolvable tile type was not changed.");
                yield break;
            }

            reporter.Pass(Id, "SkipIncompatible kept stone and remapped the unresolvable tile.");
            yield break;
        }
    }
}
#endif
