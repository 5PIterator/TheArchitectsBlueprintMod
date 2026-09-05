#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Command-driven scenario execution entrypoint.</summary>
    internal static class ScenarioTestRunner
    {
        public const string Usage = "/tab.test scenarios list | /tab.test scenarios all | /tab.test scenarios run <scenarioId>";

        public static void HandleCommand(CommandCaller caller, Player player, string[] args)
        {
            var reporter = new CommandScenarioTestReporter(caller);
            if (args.Length < 2)
            {
                caller.Reply(Usage, Color.Orange);
                return;
            }

            switch (args[1])
            {
                case "list":
                    ListScenarios(reporter);
                    break;
                case "all":
                    RunAll(reporter, player);
                    break;
                case "run" when args.Length >= 3:
                    RunScenario(reporter, player, args[2]);
                    break;
                default:
                    caller.Reply(Usage, Color.Orange);
                    break;
            }
        }

        public static void ListScenarios(IScenarioTestReporter reporter)
        {
            IReadOnlyList<IScenarioTest> scenarios = ScenarioTestRegistry.Scenarios;
            reporter.Info($"Scenarios ({scenarios.Count}):");
            for (int i = 0; i < scenarios.Count; i++)
            {
                IScenarioTest scenario = scenarios[i];
                reporter.Info($"- {scenario.Id}: {scenario.Description}");
            }
        }

        public static void RunAll(IScenarioTestReporter reporter, Player player)
        {
            IReadOnlyList<IScenarioTest> scenarios = ScenarioTestRegistry.Scenarios;
            reporter.Info($"Running {scenarios.Count} scenario(s)...");
            for (int i = 0; i < scenarios.Count; i++)
                RunScenario(reporter, player, scenarios[i].Id);
        }

        public static void RunScenario(IScenarioTestReporter reporter, Player player, string scenarioId)
        {
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

            try
            {
                scenario.Run(player, reporter);
            }
            catch (Exception ex)
            {
                reporter.Error(scenario.Id, ex.Message);
            }
        }
    }

    internal sealed class CommandScenarioTestReporter(CommandCaller caller) : IScenarioTestReporter
    {
        public void Info(string message) => caller.Reply(message, Color.LightGray);
        public void Pass(string scenarioId, string message) => caller.Reply($"[PASS] {scenarioId} | {message}", Color.LightGreen);
        public void Fail(string scenarioId, string message) => caller.Reply($"[FAIL] {scenarioId} | {message}", Color.OrangeRed);
        public void Error(string scenarioId, string message) => caller.Reply($"[ERROR] {scenarioId} | {message}", Color.Red);
    }

    internal sealed class DelegateScenarioTestReporter(Action<string, Color> output) : IScenarioTestReporter
    {
        public void Info(string message) => output(message, Color.LightGray);
        public void Pass(string scenarioId, string message) => output($"[PASS] {scenarioId} | {message}", Color.LightGreen);
        public void Fail(string scenarioId, string message) => output($"[FAIL] {scenarioId} | {message}", Color.OrangeRed);
        public void Error(string scenarioId, string message) => output($"[ERROR] {scenarioId} | {message}", Color.Red);
    }
}
#endif
