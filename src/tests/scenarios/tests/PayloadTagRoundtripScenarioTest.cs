#if DEBUG
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>In-world sanity check for TagIO payload document roundtrip.</summary>
    internal sealed class PayloadTagRoundtripScenarioTest : IScenarioTest
    {
        public string Id => "sc4";
        public string Description => "Builds and reads a minimal sparse payload document in-process.";

        public void Run(Player player, IScenarioTestReporter reporter)
        {
            if (!ScenarioTestGuards.RequireInWorld(Id, reporter))
                return;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, Id, reporter))
                return;

            TagCompound root = BuildMinimalDocument();
            if (!PayloadTagIO.TryReadDocument(root, out Payload payload, out PayloadDocumentInfo info, out string? error))
            {
                reporter.Fail(Id, error ?? "TryReadDocument failed.");
                return;
            }

            if (!payload.IsDefined || payload.Entries.Count != 1)
            {
                reporter.Fail(Id, $"Expected one sparse cell, got {payload.Entries.Count}.");
                return;
            }

            if (info.Width != 1 || info.Height != 1)
            {
                reporter.Fail(Id, $"Unexpected dimensions {info.Width}x{info.Height}.");
                return;
            }

            reporter.Pass(Id, "Payload TagIO roundtrip succeeded.");
        }

        private static TagCompound BuildMinimalDocument()
        {
            var cellBody = new TagCompound { ["tile"] = "Tile_1" };
            var entry = new TagCompound
            {
                ["x"] = 0,
                ["y"] = 0,
                ["c"] = cellBody,
            };
            var metadata = new TagCompound
            {
                ["projectName"] = "ScenarioRoundtrip",
                ["playerName"] = "Tester",
                ["width"] = 1,
                ["height"] = 1,
            };
            return new TagCompound
            {
                ["format"] = PayloadTagIO.FormatId,
                ["schemaVersion"] = PayloadTagIO.SchemaVersion,
                ["metadata"] = metadata,
                ["payload"] = new List<TagCompound> { entry },
            };
        }
    }
}
#endif
