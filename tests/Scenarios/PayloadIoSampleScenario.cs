#if DEBUG
#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Text;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using TheArchitectsBlueprintMod.Tests.Samples;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal sealed class PayloadIoSampleScenario(PayloadSample sample, bool clearCenter) : ITestScenario
    {
        public string Id => clearCenter ? $"payload_io_clear_{sample.Slug}" : $"payload_io_fill_{sample.Slug}";

        public string Description => clearCenter
            ? $"Import {sample.FileName}, clear center, export round-trip."
            : $"Import {sample.FileName}, fill center, export round-trip.";

        public void Run(ITestReporter reporter, Player player)
        {
            if (!PayloadSampleCatalog.TryLoad(sample, out Payload imported, out string? loadErr))
            {
                reporter.Fail("load sample", loadErr);
                return;
            }

            if (!PayloadScenarioChecks.SupportsCenterIoMutate(imported, forFill: !clearCenter, out string? skipReason))
            {
                reporter.Info($"[{Id}] skipped — {skipReason}");
                return;
            }

            TestBlueprintChain config = TestBlueprintChain.Begin(player).WithTileBoundsAtCursorForPayload(imported);
            ScenarioHarness.RunImportedProjection(reporter, player, Id, config, imported, (chain, r) =>
            {
                Projection projection = chain.Projection!;

                Payload mutated = clearCenter
                    ? BuildClearedCenterPayload(projection.Payload)
                    : BuildFilledCenterPayload(projection.Payload);
                projection.SetPayload(mutated);

                bool mutateOk = clearCenter
                    ? projection.Payload.TryGetCell(new Point(1, 1), out PayloadCell cen) && !cen.HasTile()
                    : projection.Payload.TryGetCell(new Point(1, 1), out PayloadCell cenf) && cenf.HasTile();
                if (!mutateOk)
                {
                    r.Fail("mutate", clearCenter ? "clear center" : "fill center");
                    return false;
                }

                r.Pass(clearCenter ? "center cleared" : "center filled");

                JToken mutatedCanonical = JToken.Parse(projection.Payload.ToJson());
                string outPath = Path.Combine(
                    PayloadSerialization.GetPayloadDirectory(),
                    clearCenter ? $"_TestExport_clear_{sample.Slug}.tab.json" : $"_TestExport_fill_{sample.Slug}.tab.json");

                if (!PayloadSerialization.TryWriteExportedWrapper(projection, "_TestExport", outPath, out string? writeErr))
                {
                    r.Fail("export", writeErr);
                    return false;
                }

                r.Pass($"exported {Path.GetFileName(outPath)}");

                JToken? exportedTok = JObject.Parse(File.ReadAllText(outPath, Encoding.UTF8))["payload"];
                if (exportedTok is null)
                {
                    r.Fail("round-trip", "exported document lacks payload");
                    return false;
                }

                if (!JToken.DeepEquals(exportedTok, mutatedCanonical))
                {
                    r.Fail("round-trip", "exported JSON differs from in-memory payload");
                    return false;
                }

                r.Pass("export round-trip");
                return true;
            });
        }

        private static Payload BuildClearedCenterPayload(Payload src)
        {
            var entries = new List<Payload.Entry>();
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    if (x == 1 && y == 1)
                        continue;
                    if (src.TryGetCell(new Point(x, y), out PayloadCell cell) && !cell.IsEmpty)
                        entries.Add(new Payload.Entry(new Point(x, y), cell));
                }
            }

            return Payload.FromSparse(entries, src.Width, src.Height);
        }

        private static Payload BuildFilledCenterPayload(Payload src)
        {
            src.TryGetCell(new Point(0, 0), out PayloadCell corner);

            var entries = new List<Payload.Entry>();
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    PayloadCell cell = x == 1 && y == 1 ? corner : (src.TryGetCell(new Point(x, y), out PayloadCell c) ? c : default);
                    if (cell.IsEmpty || !cell.HasTile())
                        continue;
                    entries.Add(new Payload.Entry(new Point(x, y), cell));
                }
            }

            return Payload.FromSparse(entries, src.Width, src.Height);
        }
    }
}
#endif
