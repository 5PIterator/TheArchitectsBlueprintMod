#if DEBUG
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Command-driven scenario execution entrypoint.</summary>
    internal static class ScenarioTestRunner
    {
        public const string Usage = "/tab.test list | /tab.test all | /tab.test run <scenarioId>";

        /// <summary>Dispatches list / all / run subcommands from the chat args after the command name.</summary>
        public static void HandleCommand(CommandCaller caller, Player player, string[] args)
        {
            // --- Parse the first token, then list or start an auto stepper session.
            var reporter = new CommandScenarioTestReporter(caller);
            if (args.Length < 1)
            {
                caller.Reply(Usage, Color.Orange);
                return;
            }

            switch (args[0])
            {
                case "list":
                case "scenarios" when args.Length >= 2 && args[1] == "list":
                    ListScenarios(reporter);
                    break;
                case "all":
                case "scenarios" when args.Length >= 2 && args[1] == "all":
                    RunAll(reporter, player);
                    break;
                case "run" when args.Length >= 2:
                    RunScenario(reporter, player, args[1]);
                    break;
                case "scenarios" when args.Length >= 3 && args[1] == "run":
                    RunScenario(reporter, player, args[2]);
                    break;
                default:
                    caller.Reply(Usage, Color.Orange);
                    break;
            }
        }

        /// <summary>Prints every registered scenario id and description.</summary>
        public static void ListScenarios(IScenarioTestReporter reporter)
        {
            // --- Walk the registry in registration order.
            IReadOnlyList<IScenarioTest> scenarios = ScenarioTestRegistry.Scenarios;
            reporter.Info($"Scenarios ({scenarios.Count}):");
            for (int i = 0; i < scenarios.Count; i++)
            {
                IScenarioTest scenario = scenarios[i];
                reporter.Info($"- {scenario.Id}: {scenario.Description}");
            }
        }

        /// <summary>Starts an auto stepper session that runs every registered scenario in order.</summary>
        public static void RunAll(IScenarioTestReporter reporter, Player player)
        {
            // --- Wrap the reporter for tallies, then pump the suite across frames.
            ArgumentNullException.ThrowIfNull(reporter);
            ArgumentNullException.ThrowIfNull(player);

            var tally = new TallyingScenarioTestReporter(reporter);
            reporter.Info($"Running {ScenarioTestRegistry.Scenarios.Count} scenario(s)...");
            DebugStepper.StartAutoSession(
                EnumerateScenarios(ScenarioTestRegistry.Scenarios, player, tally),
                onComplete: () => PrintSummary(tally),
                onCancel: () =>
                {
                    reporter.Info("Scenario run cancelled.");
                    PrintSummary(tally);
                });
        }

        /// <summary>Starts an auto stepper session for a single scenario id.</summary>
        public static void RunScenario(IScenarioTestReporter reporter, Player player, string scenarioId)
        {
            // --- Resolve the id, then pump that one scenario across frames.
            ArgumentNullException.ThrowIfNull(reporter);
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(scenarioId);

            IScenarioTest? scenario = ScenarioTestRegistry.Scenarios.FirstOrDefault(s =>
                string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (scenario is null)
            {
                reporter.Fail(scenarioId, "Unknown scenario id.");
                return;
            }

            var tally = new TallyingScenarioTestReporter(reporter);
            reporter.Info($"Running {scenario.Id}...");
            DebugStepper.StartAutoSession(
                EnumerateScenarios([scenario], player, tally),
                onComplete: () => PrintSummary(tally),
                onCancel: () =>
                {
                    reporter.Info("Scenario run cancelled.");
                    PrintSummary(tally);
                });
        }

        /// <summary>Applies shared guards once, then yields each scenario enumerator in order.</summary>
        private static IEnumerator EnumerateScenarios(
            IReadOnlyList<IScenarioTest> scenarios,
            Player player,
            TallyingScenarioTestReporter reporter)
        {
            // --- Guard once for the whole batch, then run each scenario to completion.

            if (!ScenarioTestGuards.RequireInWorld(reporter))
                yield break;
            if (!ScenarioTestGuards.RequireLocalPlayer(player, reporter))
                yield break;

            for (int i = 0; i < scenarios.Count; i++)
            {
                IScenarioTest scenario = scenarios[i];
                IEnumerator steps;
                try
                {
                    steps = scenario.Run(player, reporter);
                }
                catch (Exception ex)
                {
                    reporter.Error(scenario.Id, ex.Message);
                    continue;
                }

                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = steps.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        reporter.Error(scenario.Id, ex.Message);
                        break;
                    }

                    if (!moved)
                        break;

                    yield return steps.Current;
                }
            }
        }

        private static void PrintSummary(TallyingScenarioTestReporter tally)
        {
            tally.Inner.Info(
                $"Summary: {tally.PassCount} passed, {tally.FailCount} failed, {tally.ErrorCount} errors.");
        }
    }

    /// <summary>Forwards reports to a command caller with colored chat lines.</summary>
    internal sealed class CommandScenarioTestReporter(CommandCaller caller) : IScenarioTestReporter
    {
        public void Info(string message) => caller.Reply(message, Color.LightGray);
        public void Pass(string scenarioId, string message) => caller.Reply($"[PASS] {scenarioId} | {message}", Color.LightGreen);
        public void Fail(string scenarioId, string message) => caller.Reply($"[FAIL] {scenarioId} | {message}", Color.OrangeRed);
        public void Error(string scenarioId, string message) => caller.Reply($"[ERROR] {scenarioId} | {message}", Color.Red);
    }

    /// <summary>Forwards reports to an arbitrary colored text sink.</summary>
    internal sealed class DelegateScenarioTestReporter(Action<string, Color> output) : IScenarioTestReporter
    {
        public void Info(string message) => output(message, Color.LightGray);
        public void Pass(string scenarioId, string message) => output($"[PASS] {scenarioId} | {message}", Color.LightGreen);
        public void Fail(string scenarioId, string message) => output($"[FAIL] {scenarioId} | {message}", Color.OrangeRed);
        public void Error(string scenarioId, string message) => output($"[ERROR] {scenarioId} | {message}", Color.Red);
    }

    /// <summary>Counts pass / fail / error while forwarding to an inner reporter.</summary>
    internal sealed class TallyingScenarioTestReporter(IScenarioTestReporter inner) : IScenarioTestReporter
    {
        public IScenarioTestReporter Inner => inner;
        public int PassCount { get; private set; }
        public int FailCount { get; private set; }
        public int ErrorCount { get; private set; }

        public void Info(string message) => inner.Info(message);

        public void Pass(string scenarioId, string message)
        {
            PassCount++;
            inner.Pass(scenarioId, message);
        }

        public void Fail(string scenarioId, string message)
        {
            FailCount++;
            inner.Fail(scenarioId, message);
        }

        public void Error(string scenarioId, string message)
        {
            ErrorCount++;
            inner.Error(scenarioId, message);
        }
    }
}
#endif
