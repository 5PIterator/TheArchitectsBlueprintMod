#if DEBUG
#nullable enable

using System;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Fixtures;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    /// <summary>Runs scenario bodies inside a fixture chain enter/dispose guard.</summary>
    internal static class ScenarioHarness
    {
        internal static void RunSelection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            Func<BlueprintFixtureChain, ITestReporter, bool> body)
        {
            reporter.Info($"[{scenarioId}] aim cursor at open ground away from the player.");
            if (!BlueprintFixtureChain.TryEnterSelection(config, out BlueprintFixtureChain? chain, out string? error))
            {
                reporter.Fail("enter", error);
                return;
            }

            ArgumentNullException.ThrowIfNull(chain);
            RunBody(reporter, scenarioId, chain, () => body(chain, reporter));
        }

        internal static void RunProjection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            Func<BlueprintFixtureChain, ITestReporter, bool> body) =>
            RunProjection(reporter, player, scenarioId, config, cutSource: false, beforeCapture: null, body);

        internal static void RunProjection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            Action<BlueprintFixtureChain> beforeCapture,
            Func<BlueprintFixtureChain, ITestReporter, bool> body) =>
            RunProjection(reporter, player, scenarioId, config, cutSource: false, beforeCapture, body);

        internal static void RunProjection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            bool cutSource,
            Func<BlueprintFixtureChain, ITestReporter, bool> body) =>
            RunProjection(reporter, player, scenarioId, config, cutSource, beforeCapture: null, body);

        internal static void RunProjection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            bool cutSource,
            Action<BlueprintFixtureChain>? beforeCapture,
            Func<BlueprintFixtureChain, ITestReporter, bool> body)
        {
            reporter.Info($"[{scenarioId}] aim cursor at tiles to capture into the projection.");
            if (!BlueprintFixtureChain.TryEnterProjection(config, cutSource, beforeCapture, out BlueprintFixtureChain? chain, out string? error))
            {
                reporter.Fail("enter", error);
                return;
            }

            ArgumentNullException.ThrowIfNull(chain);
            RunBody(reporter, scenarioId, chain, () => body(chain, reporter));
        }

        internal static void RunImportedProjection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            Payload payload,
            Func<BlueprintFixtureChain, ITestReporter, bool> body) =>
            RunImportedProjection(reporter, player, scenarioId, config, payload, cutSource: false, body);

        internal static void RunImportedProjection(
            ITestReporter reporter,
            Player player,
            string scenarioId,
            TestBlueprintChain config,
            Payload payload,
            bool cutSource,
            Func<BlueprintFixtureChain, ITestReporter, bool> body)
        {
            reporter.Info($"[{scenarioId}] aim cursor at the paste target (away from the player).");
            if (!BlueprintFixtureChain.TryEnterImportedProjection(config, payload, cutSource, out BlueprintFixtureChain? chain, out string? error))
            {
                reporter.Fail("enter", error);
                return;
            }

            ArgumentNullException.ThrowIfNull(chain);
            RunBody(reporter, scenarioId, chain, () => body(chain, reporter));
        }

        private static void RunBody(
            ITestReporter reporter,
            string scenarioId,
            BlueprintFixtureChain chain,
            Func<bool> body)
        {
            try
            {
                if (!body())
                    return;

                reporter.Info($"[{scenarioId}] completed.");
            }
            finally
            {
                chain.Dispose();
                if (!chain.VerifyTeardown(out string? error))
                    reporter.Fail("teardown", error);
                else
                {
                    reporter.Pass("teardown");
                    reporter.Pass("world tiles restored");
                }
            }
        }
    }
}
#endif
