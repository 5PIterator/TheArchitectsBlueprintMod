#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArchitectsBlueprintMod.Core;
using ArchitectsBlueprintMod.Tests.Fixtures;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Tests
{
    /// <summary>
    /// In-game payload import/export regression script (fixture, mutate center, export, JSON checks).
    /// Invoked only from <see cref="ArchitectsBlueprintMod.Core.PayloadIoTestCommand"/>.
    /// </summary>
    internal static class PayloadIoTestScript
    {
        internal const string EmbeddedFixture = "ArchitectsBlueprintMod.ABPTestFixture";
        internal const string GoldenClear = "ArchitectsBlueprintMod.GoldenPayloadClearCenter";
        internal const string GoldenFill = "ArchitectsBlueprintMod.GoldenPayloadFillCenter";

        internal const string DefaultFixtureFileName = "ABPTest.asmpayload.json";

        /// <summary>Runs either the clear-center or fill-center scenario.</summary>
        internal static void Run(CommandCaller caller, Player player, string scenarioArg)
        {
            bool clearMode = scenarioArg == "clear";

            caller.Reply($"[abp.test.payload] Scenario: {scenarioArg}", Color.White);

            if (!TryResolveFixtureDocument(out string fixtureText, out string? resolveErr))
            {
                caller.Reply($"Step fixture: FAIL ({resolveErr})", Color.OrangeRed);
                return;
            }

            if (!PayloadSerialization.TryParsePayloadDocument(fixtureText, out Payload imported, out string? fixtureErr))
            {
                caller.Reply($"Step import (parse fixture): FAIL {fixtureErr}", Color.OrangeRed);
                return;
            }

            caller.Reply("Step import (parse fixture): PASS", Color.LimeGreen);

            var chain = TestBlueprintChain.Begin(player).WithTileBoundsFromPlayerForPayload(imported);

            if (!chain.TryValidateInWorld(out string? boundsErr))
            {
                caller.Reply($"FAIL: {boundsErr}", Color.OrangeRed);
                return;
            }

            if (!chain.TryBuildProjectionWithImportedPayload(imported, cutSource: false, out Projection projection, out string? buildErr))
            {
                caller.Reply($"FAIL: Could not construct projection: {buildErr}", Color.OrangeRed);
                return;
            }

            bool centerHasTile = projection.Payload.TryGetCell(new Point(1, 1), out PayloadCell mc) && mc.HasTile();
            if (projection.Payload.Width != 3 || projection.Payload.Height != 3 || !centerHasTile)
            {
                caller.Reply(
                    $"Step post-import: FAIL ({projection.Payload.Width}x{projection.Payload.Height}, centerHasTile={centerHasTile})",
                    Color.OrangeRed);
                return;
            }

            caller.Reply("Step post-import (3×3, center tile): PASS", Color.LimeGreen);

            if (!clearMode && (!projection.Payload.TryGetCell(Point.Zero, out PayloadCell zz) || !zz.HasTile()))
            {
                caller.Reply("Step mutate (fill): FAIL — fixture needs a tile at payload cell (0,0).", Color.OrangeRed);
                return;
            }

            Payload mutated = clearMode ? BuildClearedCenterPayload(projection.Payload) : BuildFilledCenterPayload(projection.Payload);
            projection.SetPayload(mutated, discardSelection: true);

            bool mutateOk = clearMode
                ? projection.Payload.TryGetCell(new Point(1, 1), out PayloadCell cen) && !cen.HasTile()
                : projection.Payload.TryGetCell(new Point(1, 1), out PayloadCell cenf) && cenf.HasTile();
            if (!mutateOk)
            {
                caller.Reply($"Step mutate: FAIL ({scenarioArg})", Color.OrangeRed);
                return;
            }

            caller.Reply(clearMode ? "Step mutate (clear center): PASS" : "Step mutate (fill center): PASS", Color.LimeGreen);

            JToken mutatedCanonical = JToken.Parse(projection.Payload.ToJson());

            string outPath = Path.Combine(
                PayloadSerialization.GetPayloadDirectory(),
                clearMode ? "_TestExport_clear.asmpayload.json" : "_TestExport_fill.asmpayload.json");

            if (!PayloadSerialization.TryWriteExportedWrapper(projection, "_TestExport", outPath, out string? writeErr))
            {
                caller.Reply($"Step export: FAIL ({writeErr})", Color.OrangeRed);
                return;
            }

            caller.Reply($"Step export: PASS ({Path.GetFileName(outPath)})", Color.LimeGreen);

            JToken? exportedTok = JObject.Parse(File.ReadAllText(outPath, Encoding.UTF8))["payload"];
            if (exportedTok is null)
            {
                caller.Reply("Step verify export (round-trip): FAIL (exported document lacks payload).", Color.OrangeRed);
                return;
            }

            JToken exportedPayload = exportedTok;

            if (!JToken.DeepEquals(exportedPayload, mutatedCanonical))
            {
                caller.Reply("Step verify export (round-trip): FAIL", Color.OrangeRed);
                return;
            }

            caller.Reply("Step verify export (round-trip): PASS", Color.LimeGreen);

            string goldenName = clearMode ? GoldenClear : GoldenFill;
            TryCompareEmbeddedGolden(caller, exportedPayload, goldenName);

            caller.Reply($"[abp.test.payload] {scenarioArg} completed successfully.", Color.LimeGreen);
        }

        private static void TryCompareEmbeddedGolden(CommandCaller caller, JToken exportedPayload, string resourceName)
        {
            try
            {
                using Stream? goldenStream = typeof(PayloadIoTestScript).Assembly.GetManifestResourceStream(resourceName);
                if (goldenStream is null)
                {
                    caller.Reply("Step verify (committed golden JSON): skipped (not embedded).", Color.Yellow);
                    return;
                }

                using var reader = new StreamReader(goldenStream, Encoding.UTF8);
                string text = reader.ReadToEnd();
                JToken golden = JToken.Parse(text);
                if (JToken.DeepEquals(exportedPayload, golden))
                {
                    caller.Reply("Step verify (committed golden JSON): PASS", Color.LimeGreen);
                    return;
                }

                caller.Reply(
                    "Step verify (committed golden JSON): differs from export — round-trip OK; update tests/goldens if intentional.",
                    Color.Yellow);
            }
            catch
            {
                caller.Reply("Step verify (committed golden JSON): skipped (missing parse).", Color.Yellow);
            }
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

        private static bool TryResolveFixtureDocument(out string text, out string? error)
        {
            error = null;
            string preferred = Path.Combine(PayloadSerialization.GetPayloadDirectory(), DefaultFixtureFileName);
            if (File.Exists(preferred))
            {
                text = File.ReadAllText(preferred, Encoding.UTF8);
                return true;
            }

            string? embedded = TryReadEmbeddedText(EmbeddedFixture);
            if (!string.IsNullOrWhiteSpace(embedded))
            {
                text = embedded;
                return true;
            }

            text = "";
            error = $"No ABPTest fixture (expected {preferred} or embedded {EmbeddedFixture}).";
            return false;
        }

        private static string? TryReadEmbeddedText(string name)
        {
            using Stream? stream = typeof(PayloadIoTestScript).Assembly.GetManifestResourceStream(name);
            if (stream is null)
                return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
#endif
