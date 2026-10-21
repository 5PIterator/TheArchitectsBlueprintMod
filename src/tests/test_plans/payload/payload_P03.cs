#if DEBUG
#nullable enable

using System.Collections;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PayloadTests
{
    /// <summary>Imports mirror and legacy JSON payloads and diffs the resulting cells.</summary>
    internal sealed class payload_P03 : ITestCase
    {
        public string Id => "payload_P03";
        public string Description => "Mirror and legacy sparse JSON import with cell equality";

        public TestCaseType Type => TestCaseType.Positive;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Import mirror then legacy fixtures; require isLegacy flags and matching stone cells.

            if (!PayloadCellFixture.TryCreateStone(out PayloadCell stone, out string? cellError))
            {
                reporter.Fail(Id, cellError ?? "Stone cell build failed.");
                yield break;
            }

            if (!TryImport(
                    PayloadJsonFixture.MirrorDocumentJson,
                    expectLegacy: false,
                    expectWidth: 1,
                    expectHeight: 1,
                    out Payload mirror,
                    out string? mirrorError))
            {
                reporter.Fail(Id, $"Mirror import failed: {mirrorError}");
                yield break;
            }

            Payload mirrorExpected = Payload.FromSparse([new Payload.Entry(new Point(0, 0), stone)], 1, 1);
            if (!PayloadAssert.TryMatch(Id, reporter, mirrorExpected, mirror, new Rectangle(0, 0, 1, 1)))
                yield break;

            if (!TryImport(
                    PayloadJsonFixture.LegacySparseJson,
                    expectLegacy: true,
                    expectWidth: 2,
                    expectHeight: 1,
                    out Payload legacy,
                    out string? legacyError))
            {
                reporter.Fail(Id, $"Legacy import failed: {legacyError}");
                yield break;
            }

            Payload legacyExpected = Payload.FromSparse(
                [
                    new Payload.Entry(new Point(0, 0), stone),
                    new Payload.Entry(new Point(1, 0), stone),
                ],
                2,
                1);

            if (!PayloadAssert.TryMatch(Id, reporter, legacyExpected, legacy, new Rectangle(0, 0, 2, 1)))
                yield break;

            reporter.Pass(Id, "Mirror and legacy JSON imports matched their stone cells.");
            yield break;
        }

        private static bool TryImport(
            string json,
            bool expectLegacy,
            int expectWidth,
            int expectHeight,
            out Payload payload,
            out string? error)
        {
            // --- Parse, then check legacy flag, defined state, and declared dimensions.
            payload = Payload.Empty;
            error = null;
            bool ok = PayloadJsonImport.TryImport(json, out payload, out PayloadDocumentInfo info, out bool isLegacy, out error);
            if (!ok)
                return false;
            if (isLegacy != expectLegacy)
            {
                error = $"Expected isLegacy={expectLegacy}, got {isLegacy}.";
                return false;
            }
            if (!payload.IsDefined)
            {
                error = "Payload was not defined after import.";
                return false;
            }
            if (info.Width != expectWidth || info.Height != expectHeight)
            {
                error = $"Expected {expectWidth}x{expectHeight}, got {info.Width}x{info.Height}.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(info.ProjectName))
            {
                error = "Project name missing from document info.";
                return false;
            }

            return true;
        }
    }
}
#endif
