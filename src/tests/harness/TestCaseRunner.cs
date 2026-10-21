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

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Command-driven Test Case execution entrypoint.</summary>
    internal static class TestCaseRunner
    {
        public const string Usage = "/tab.test list | /tab.test all | /tab.test run <CaseTag>";

        /// <summary>Dispatches list / all / run subcommands from the chat args after the command name.</summary>
        public static void HandleCommand(CommandCaller caller, Player player, string[] args)
        {
            // --- Parse the first token, then list or start an auto stepper session.
            var reporter = new CommandTestCaseReporter(caller);
            if (args.Length < 1)
            {
                caller.Reply(Usage, Color.Orange);
                return;
            }

            switch (args[0])
            {
                case "list":
                    ListCases(reporter);
                    break;
                case "all":
                    RunAll(reporter, player);
                    break;
                case "run" when args.Length >= 2:
                    RunCase(reporter, player, args[1]);
                    break;
                default:
                    caller.Reply(Usage, Color.Orange);
                    break;
            }
        }

        /// <summary>Prints every registered Test Case id, type, and Test Scenario title.</summary>
        public static void ListCases(ITestCaseReporter reporter)
        {
            // --- Walk the registry in registration order.
            IReadOnlyList<ITestCase> cases = TestCaseRegistry.Cases;
            reporter.Info($"Test Cases ({cases.Count}):");
            for (int i = 0; i < cases.Count; i++)
            {
                ITestCase testCase = cases[i];
                reporter.Info($"- {testCase.Id} [{testCase.Type}]: {testCase.Description}");
            }
        }

        /// <summary>Starts an auto stepper session that runs every registered Test Case in order.</summary>
        public static void RunAll(ITestCaseReporter reporter, Player player)
        {
            // --- Run the full registry at one enumerator step per frame.
            ArgumentNullException.ThrowIfNull(reporter);
            ArgumentNullException.ThrowIfNull(player);

            ITestCase[] suite = [.. TestCaseRegistry.Cases];
            var dump = new FileDumpTestCaseReporter(reporter, "all");
            var tally = new TallyingTestCaseReporter(dump);
            dump.Info($"Running {suite.Length} Test Case(s)...");
            StartCaseSession(dump, player, suite, tally);
        }

        /// <summary>Starts an auto stepper session for a single Case Tag.</summary>
        public static void RunCase(ITestCaseReporter reporter, Player player, string caseTag)
        {
            // --- Resolve the Case Tag, then pump that one Case across frames.
            ArgumentNullException.ThrowIfNull(reporter);
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(caseTag);

            ITestCase? testCase = TestCaseRegistry.Cases.FirstOrDefault(c =>
                string.Equals(c.Id, caseTag, StringComparison.OrdinalIgnoreCase));
            if (testCase is null)
            {
                reporter.Fail(caseTag, "Unknown Case Tag.");
                return;
            }

            var dump = new FileDumpTestCaseReporter(reporter, testCase.Id);
            var tally = new TallyingTestCaseReporter(dump);
            dump.Info($"Running {testCase.Id}...");
            StartCaseSession(dump, player, [testCase], tally);
        }

        /// <summary>Starts a one-step-per-frame auto session for the given Test Cases.</summary>
        private static void StartCaseSession(
            FileDumpTestCaseReporter dump,
            Player player,
            IReadOnlyList<ITestCase> cases,
            TallyingTestCaseReporter tally)
        {
            // --- Discovery uses 500 steps/frame; Cases must not — paste ticks + INFO logging would freeze the client.
            DebugStepper.StartAutoSession(
                EnumerateCases(cases, player, tally),
                onComplete: () =>
                {
                    PrintSummary(tally);
                    dump.Dispose();
                },
                onCancel: () =>
                {
                    dump.Info("Test Case run cancelled.");
                    PrintSummary(tally);
                    dump.Dispose();
                },
                stepsPerFrame: 1);
        }

        /// <summary>Applies shared guards once, then yields each Case enumerator in order.</summary>
        private static IEnumerator EnumerateCases(
            IReadOnlyList<ITestCase> cases,
            Player player,
            TallyingTestCaseReporter reporter)
        {
            // --- Guard once for the whole batch, then run each Case to completion.

            if (!TestCaseGuards.RequireInWorld(reporter))
                yield break;
            if (!TestCaseGuards.RequireLocalPlayer(player, reporter))
                yield break;
            if (!TestCaseGuards.RequireChamberConfigured(reporter))
                yield break;

            for (int i = 0; i < cases.Count; i++)
            {
                ITestCase testCase = cases[i];
                IEnumerator steps;
                try
                {
                    steps = testCase.Run(player, reporter);
                }
                catch (Exception ex)
                {
                    reporter.Error(testCase.Id, ex.Message);
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
                        reporter.Error(testCase.Id, ex.Message);
                        break;
                    }

                    if (!moved)
                        break;

                    yield return steps.Current;
                }
            }
        }

        private static void PrintSummary(TallyingTestCaseReporter tally)
        {
            tally.Inner.Info(
                $"Summary: {tally.PassCount} passed, {tally.FailCount} failed, {tally.ErrorCount} errors.");
        }
    }

    /// <summary>Forwards reports to a command caller with colored chat lines.</summary>
    internal sealed class CommandTestCaseReporter(CommandCaller caller) : ITestCaseReporter
    {
        public void Info(string message) => caller.Reply(message, Color.LightGray);
        public void Pass(string caseTag, string message) => caller.Reply($"[PASS] {caseTag} | {message}", Color.LightGreen);
        public void Fail(string caseTag, string message) => caller.Reply($"[FAIL] {caseTag} | {message}", Color.OrangeRed);
        public void Error(string caseTag, string message) => caller.Reply($"[ERROR] {caseTag} | {message}", Color.Red);
    }

    /// <summary>Forwards reports to an arbitrary colored text sink.</summary>
    internal sealed class DelegateTestCaseReporter(Action<string, Color> output) : ITestCaseReporter
    {
        public void Info(string message) => output(message, Color.LightGray);
        public void Pass(string caseTag, string message) => output($"[PASS] {caseTag} | {message}", Color.LightGreen);
        public void Fail(string caseTag, string message) => output($"[FAIL] {caseTag} | {message}", Color.OrangeRed);
        public void Error(string caseTag, string message) => output($"[ERROR] {caseTag} | {message}", Color.Red);
    }

    /// <summary>Counts pass / fail / error while forwarding to an inner reporter.</summary>
    internal sealed class TallyingTestCaseReporter(ITestCaseReporter inner) : ITestCaseReporter
    {
        public ITestCaseReporter Inner => inner;
        public int PassCount { get; private set; }
        public int FailCount { get; private set; }
        public int ErrorCount { get; private set; }

        public void Info(string message) => inner.Info(message);

        public void Pass(string caseTag, string message)
        {
            PassCount++;
            inner.Pass(caseTag, message);
        }

        public void Fail(string caseTag, string message)
        {
            FailCount++;
            inner.Fail(caseTag, message);
        }

        public void Error(string caseTag, string message)
        {
            ErrorCount++;
            inner.Error(caseTag, message);
        }
    }
}
#endif
