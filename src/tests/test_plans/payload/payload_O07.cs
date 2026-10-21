#if DEBUG
#nullable enable

using System;
using System.Collections;
using System.IO;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.TestCases.PayloadTests
{
    /// <summary>Opens the massive modded fixture and asserts document metadata (and cells when schema-valid).</summary>
    internal sealed class payload_O07 : ITestCase
    {
        private const string FixtureFileName = "SS_MassiveModded.tab";

        public string Id => "payload_O07";
        public string Description => "Loads SS_MassiveModded.tab metadata and cells when style-valid";

        public TestCaseType Type => TestCaseType.Other;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- Resolve path, require DocumentInfo, then prefer a full cell payload when the fixture has style.

            if (!PayloadFixturePaths.TryResolve(FixtureFileName, out string fixturePath, out string? pathError))
            {
                reporter.Fail(Id, pathError ?? "Fixture path resolve failed.");
                yield break;
            }

            PayloadDocumentInfo info;
            using (FileStream stream = File.OpenRead(fixturePath))
            {
                if (!PayloadTagIO.TryReadDocumentInfo(stream, out info, out string? infoError))
                {
                    reporter.Fail(Id, infoError ?? "Fixture document info read failed.");
                    yield break;
                }
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                reporter.Fail(Id, $"Modded fixture DocumentInfo has invalid size {info.Width}x{info.Height}.");
                yield break;
            }

            if (PayloadSerialization.TryReadPayloadFromFile(
                    fixturePath,
                    out Payload payload,
                    out PayloadDocumentInfo fullInfo,
                    out string? readError)
                && payload.IsDefined)
            {
                reporter.Pass(
                    Id,
                    $"Loaded {FixtureFileName} ({payload.Width}x{payload.Height}, sparse={payload.Entries.Count}, project={fullInfo.ProjectName}).");
                yield break;
            }

            // Pre-style fixtures still validate as documents; cell bodies need a re-export with style.
            bool styleGap = readError is not null
                && readError.Contains("tile cells require style", StringComparison.Ordinal);
            if (styleGap)
            {
                reporter.Info(
                    $"{Id}: full cell load blocked ({readError}); DocumentInfo OK ({info.Width}x{info.Height}, project={info.ProjectName}).");
                reporter.Pass(
                    Id,
                    $"Document metadata loaded for {FixtureFileName} ({info.Width}x{info.Height}); re-export with style for full cell parse.");
                yield break;
            }

            reporter.Fail(Id, readError ?? "Fixture payload read failed.");
            yield break;
        }
    }
}
#endif
