#if DEBUG
#nullable enable

using System.Collections;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PayloadTests
{
    /// <summary>Writes and reads a one-cell TagIO payload document.</summary>
    internal sealed class TagRoundtripScenario : IScenarioTest
    {
        public string Id => "payload.tag";
        public string Description => "TagIO BuildDocument / TryReadDocument roundtrip of a stone cell.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- Build a stone payload, serialize to TagCompound, read back, and diff the cell.

            if (!ScenarioTestCells.TryCreateStonePayload(out Payload source, out _, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            var meta = new PayloadExportMetadata("TagProject", "Tester", "2026-01-01T00:00:00.0000000Z", "0.0.0-test", "0.0.0.0", null);
            var root = PayloadTagIO.BuildDocument(source, meta);
            if (!PayloadTagIO.TryReadDocument(root, out Payload actual, out PayloadDocumentInfo info, out string? error))
            {
                reporter.Fail(Id, error ?? "TryReadDocument failed.");
                yield break;
            }

            if (info.Width != 1 || info.Height != 1 || info.ProjectName != "TagProject")
            {
                reporter.Fail(Id, $"Unexpected metadata {info.ProjectName} {info.Width}x{info.Height}.");
                yield break;
            }

            ScenarioPayloadAssert.PayloadsMatch(
                Id,
                reporter,
                source,
                actual,
                new Rectangle(0, 0, 1, 1),
                "TagIO document roundtrip matched the stone cell.");
            yield break;
        }
    }
}
#endif
