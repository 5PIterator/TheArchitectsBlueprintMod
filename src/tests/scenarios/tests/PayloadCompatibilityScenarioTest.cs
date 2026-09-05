#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Validates payload compatibility prepare/report paths in-game.</summary>
    internal sealed class PayloadCompatibilityScenarioTest : IScenarioTest
    {
        public string Id => "sc8";
        public string Description => "Prepare validate-only reports compatible stone cells.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            if (!PayloadTagIO.TryCellFromTag(new TagCompound { ["tile"] = "Tile_1", ["style"] = 0 }, out PayloadCell stone))
            {
                reporter.Fail(Id, "Failed to build stone test cell.");
                return;
            }

            Payload source = Payload.FromSparse([new Payload.Entry(new Point(0, 0), stone)], width: 1, height: 1);
            Payload result = PayloadCompatibility.Prepare(
                source,
                PayloadPrepareMode.ValidateOnly,
                metadataModsLoaded: null,
                out PayloadCompatibilityReport report);

            if (!ReferenceEquals(source, result))
            {
                reporter.Fail(Id, "Validate-only prepare should return the same payload instance.");
                return;
            }

            if (report.TotalNonEmptyCells != 1 || report.IncompatibleCells != 0)
            {
                reporter.Fail(Id, $"Unexpected report: total={report.TotalNonEmptyCells}, incompatible={report.IncompatibleCells}.");
                return;
            }

            reporter.Pass(Id, "Compatibility validate-only report is clean.");
        }
    }
}
#endif
