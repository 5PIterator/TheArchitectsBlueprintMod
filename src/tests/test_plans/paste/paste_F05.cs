#if DEBUG
#nullable enable

using System.Collections;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.TestCases.PasteTests
{
    /// <summary>
    /// Times PastePlanningLookup.Build and a full Identify sweep on the modular layer fixture.
    /// </summary>
    internal sealed class paste_F05 : ITestCase
    {
        private const string FixtureFileName = "TABModularTest.tab";

        /// <summary>Generous tripwire so only catastrophic Build regressions fail the Case.</summary>
        private const long BuildTripwireMs = 2000;

        public string Id => "paste_F05";
        public string Description => "Times PastePlanningLookup.Build and Identify on TABModularTest.tab";

        public TestCaseType Type => TestCaseType.Performance;


        public IEnumerator Run(Player player, ITestCaseReporter reporter)
        {
            // --- DefaultPaste config → optional stone Build sample → modular Build + Identify.

            IEnumerator body = RunBody(player, reporter);
            IEnumerator run = PasteConfigFixture.RunWith(PasteConfigProfile.DefaultPaste, body);
            while (run.MoveNext())
                yield return run.Current;
        }

        private IEnumerator RunBody(Player player, ITestCaseReporter reporter)
        {
            long stoneBuildMs = -1;
            Core.Projection? stoneProjection = null;
            Point stoneSource = default;
            Point stoneTarget = default;
            bool stoneJoined = false;
            string? stoneError = null;

            IEnumerator stoneJoin = StonePasteFixture.JoinProjectionMoved(
                player,
                reporter,
                Id,
                (ok, proj, _, src, tgt, err) =>
                {
                    stoneJoined = ok;
                    stoneProjection = proj;
                    stoneSource = src;
                    stoneTarget = tgt;
                    stoneError = err;
                });
            while (stoneJoin.MoveNext())
                yield return stoneJoin.Current;

            if (stoneJoined && stoneProjection is not null)
            {
                PasteExecutionContext stoneExecution = PasteExecutionContext.FromProjection(stoneProjection);
                var stoneWatch = Stopwatch.StartNew();
                _ = PastePlanningLookup.Build(stoneExecution);
                stoneWatch.Stop();
                stoneBuildMs = stoneWatch.ElapsedMilliseconds;
                reporter.Info($"{Id}: stone 1x1 PastePlanningLookup.Build={stoneBuildMs}ms.");

                StonePasteFixture.Cleanup(player, stoneProjection);
                IEnumerator stoneClear = StonePasteFixture.ClearWorldCells(
                    player, stoneSource, stoneTarget, reporter, Id);
                while (stoneClear.MoveNext())
                    yield return stoneClear.Current;
            }
            else
            {
                reporter.Info($"{Id}: stone Build baseline skipped ({stoneError ?? "setup failed"}).");
            }

            if (!PayloadFixturePaths.TryResolve(FixtureFileName, out string fixturePath, out string? pathError))
            {
                reporter.Fail(Id, pathError ?? "Fixture path resolve failed.");
                yield break;
            }

            if (!PayloadSerialization.TryReadPayloadFromFile(
                    fixturePath,
                    out Payload source,
                    out PayloadDocumentInfo info,
                    out string? readError))
            {
                reporter.Fail(Id, readError ?? "Fixture payload read failed.");
                yield break;
            }

            if (!source.IsDefined)
            {
                reporter.Fail(Id, "Fixture payload is not defined.");
                yield break;
            }

            Point origin = TestChamberSystem.Instance.FixtureOrigin(source.Width, source.Height);
            var area = new Rectangle(origin.X, origin.Y, source.Width, source.Height);
            if (!StonePasteFixture.TryCreateProjection(
                    player,
                    area,
                    captureWorldPayload: false,
                    out Core.Projection? projection,
                    out string? setupError)
                || projection is null)
            {
                reporter.Fail(Id, setupError ?? "Projection setup failed.");
                yield break;
            }

            try
            {
                projection.SetPayload(source, info.ModsLoaded);
                if (!projection.HasPayload)
                {
                    reporter.Fail(Id, "Projection payload is not defined after fixture load.");
                    yield break;
                }

                PasteExecutionContext execution = PasteExecutionContext.FromProjection(projection);

                var buildWatch = Stopwatch.StartNew();
                PasteTileLookup lookup = PastePlanningLookup.Build(execution);
                buildWatch.Stop();
                long buildMs = buildWatch.ElapsedMilliseconds;

                LayerPipeline pipeline = LayerSystem.ComposePipeline();
                PastingConfig config = ModContent.GetInstance<PastingConfig>();
                EffectivePastePolicy policy = EffectivePastePolicy.Resolve(config, PasteClientHints.FromClientConfig());
                var identifyContext = new PasteContext(
                    playerIndex: execution.Owner,
                    inventoryLookup: null,
                    policy: policy,
                    lookup: lookup,
                    effectSession: null,
                    interactionBudget: null,
                    payload: execution.Payload);

                int identifiedCells = 0;
                var identifyWatch = Stopwatch.StartNew();
                Point size = execution.PayloadSize;
                for (int x = 0; x < size.X; x++)
                {
                    for (int y = 0; y < size.Y; y++)
                    {
                        Point rel = new(x, y);
                        if (!execution.IsIncluded(rel) || !lookup.TryGetPasteTile(rel, out PasteTile pasteTile))
                            continue;

                        _ = LayerSystem.Identify(
                            in pasteTile,
                            in lookup,
                            pipeline,
                            in identifyContext,
                            config,
                            PasteSubject.Payload);
                        identifiedCells++;
                    }
                }

                identifyWatch.Stop();
                long identifyMs = identifyWatch.ElapsedMilliseconds;

                reporter.Info(
                    $"{Id}: modular {source.Width}x{source.Height} sparse={source.Entries.Count} "
                    + $"Build={buildMs}ms Identify={identifyMs}ms cells={identifiedCells}"
                    + (stoneBuildMs >= 0 ? $" stoneBuild={stoneBuildMs}ms" : string.Empty)
                    + ".");

                if (buildMs > BuildTripwireMs)
                {
                    reporter.Fail(
                        Id,
                        $"PastePlanningLookup.Build took {buildMs}ms (tripwire {BuildTripwireMs}ms) "
                        + $"on {source.Width}x{source.Height}; Identify={identifyMs}ms.");
                    yield break;
                }

                reporter.Pass(
                    Id,
                    $"Build={buildMs}ms Identify={identifyMs}ms on {source.Width}x{source.Height} "
                    + $"(tripwire {BuildTripwireMs}ms).");
            }
            finally
            {
                StonePasteFixture.Cleanup(player, projection);
            }

            yield break;
        }
    }
}
#endif
