#if DEBUG
#nullable enable

using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Writes and reads a compressed TagIO payload document in-game.</summary>
    internal sealed class PayloadTagIOStreamScenarioTest : IScenarioTest
    {
        public string Id => "sc5";
        public string Description => "WriteDocument/TryReadDocument roundtrip through a memory stream.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            if (!PayloadTagIO.TryCellFromTag(new TagCompound { ["tile"] = "Tile_1" }, out PayloadCell stone))
            {
                reporter.Fail(Id, "Failed to build stone test cell.");
                return;
            }

            Payload source = Payload.FromSparse([new Payload.Entry(new Point(0, 0), stone)], width: 1, height: 1);
            var meta = new PayloadExportMetadata("StreamProject", "Tester", "2026-01-01T00:00:00.0000000Z", "0.0.0-test", "0.0.0.0", null);

            using var stream = new MemoryStream();
            PayloadTagIO.WriteDocument(stream, source, meta);
            stream.Position = 0;

            if (!PayloadTagIO.TryReadDocument(stream, out Payload payload, out PayloadDocumentInfo info, out string? error))
            {
                reporter.Fail(Id, error ?? "TryReadDocument failed.");
                return;
            }

            if (!payload.IsDefined || payload.Entries.Count != 1 || info.ProjectName != "StreamProject")
            {
                reporter.Fail(Id, "Stream roundtrip payload or metadata mismatch.");
                return;
            }

            reporter.Pass(Id, "TagIO stream roundtrip succeeded.");
        }
    }
}
#endif
