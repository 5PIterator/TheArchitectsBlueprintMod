#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Validates sparse payload construction and filtering in-game.</summary>
    internal sealed class PayloadFromSparseScenarioTest : IScenarioTest
    {
        public string Id => "sc7";
        public string Description => "FromSparse keeps in-bounds cells and drops out-of-bounds placements.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            var body = new TagCompound { ["tile"] = "Tile_1" };
            if (!PayloadTagIO.TryCellFromTag(body, out PayloadCell stone))
            {
                reporter.Fail(Id, "Failed to parse stone test cell.");
                return;
            }

            Payload payload = Payload.FromSparse(
                [
                    new Payload.Entry(new Point(0, 0), stone),
                    new Payload.Entry(new Point(4, 0), stone),
                    new Payload.Entry(new Point(1, 1), default),
                ],
                width: 2,
                height: 2);

            if (!payload.IsDefined || payload.Entries.Count != 1)
            {
                reporter.Fail(Id, $"Expected one in-bounds cell, got {payload.Entries.Count}.");
                return;
            }

            if (payload.Entries[0].Relative != new Point(0, 0))
            {
                reporter.Fail(Id, "Unexpected surviving cell coordinate.");
                return;
            }

            reporter.Pass(Id, "FromSparse filtering behaved as expected.");
        }
    }
}
#endif
