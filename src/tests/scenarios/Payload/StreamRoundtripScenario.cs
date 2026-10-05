#if DEBUG
#nullable enable

using System.Collections;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests.PayloadTests
{
    /// <summary>Writes and reads a compressed TagIO stream including metadata and the cell.</summary>
    internal sealed class StreamRoundtripScenario : IScenarioTest
    {
        public string Id => "payload.stream";
        public string Description => "WriteDocument / TryReadDocument memory-stream roundtrip.";

        public IEnumerator Run(Player player, IScenarioTestReporter reporter)
        {
            // --- WriteDocument to a MemoryStream, rewind, read, then diff the payload.

            if (!ScenarioTestCells.TryCreateStonePayload(out Payload source, out _, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            var meta = new PayloadExportMetadata("StreamProject", "Tester", "2026-01-01T00:00:00.0000000Z", "0.0.0-test", "0.0.0.0", null);
            using var stream = new MemoryStream();
            PayloadTagIO.WriteDocument(stream, source, meta);
            stream.Position = 0;

            if (!PayloadTagIO.TryReadDocument(stream, out Payload actual, out PayloadDocumentInfo info, out string? error))
            {
                reporter.Fail(Id, error ?? "TryReadDocument failed.");
                yield break;
            }

            if (info.ProjectName != "StreamProject" || info.PlayerName != "Tester")
            {
                reporter.Fail(Id, "Stream roundtrip metadata mismatch.");
                yield break;
            }

            ScenarioPayloadAssert.PayloadsMatch(
                Id,
                reporter,
                source,
                actual,
                new Rectangle(0, 0, 1, 1),
                "TagIO stream roundtrip matched metadata and cell.");
            yield break;
        }
    }
}
#endif
