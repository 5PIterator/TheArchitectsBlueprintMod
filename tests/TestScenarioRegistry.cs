#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TheArchitectsBlueprintMod.Tests.Results;
using TheArchitectsBlueprintMod.Tests.Samples;
using TheArchitectsBlueprintMod.Tests.Scenarios;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests
{
    internal static class TestScenarioRegistry
    {
        private static ITestScenario[]? _scenarios;

        private static ITestScenario[] BuildScenarios() =>
            [.. ScenarioDiscovery.DiscoverAll()];

        internal static IReadOnlyList<ITestScenario> All => _scenarios ??= BuildScenarios();

        internal static IReadOnlyList<PayloadSample> Samples => PayloadSampleCatalog.All;

        internal static bool TryGet(string id, out ITestScenario? scenario)
        {
            scenario = All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            return scenario is not null;
        }

        internal static void Run(ITestReporter reporter, Player player, string id)
        {
            using var recorder = TestResultRecorder.Begin(reporter, id);
            if (!TryGet(id, out ITestScenario? scenario) || scenario is null)
            {
                recorder.Fail("unknown scenario", id);
                ListScenarioIds(recorder);
                return;
            }

            recorder.BeginScenario(scenario.Id, scenario.Description);
            scenario.Run(recorder, player);
        }

        internal static void RunAll(ITestReporter reporter, Player player)
        {
            using var recorder = TestResultRecorder.Begin(reporter, "all");
            recorder.Info(
                $"[tab.test] Running {All.Count} scenario(s) for {Samples.Count} sample(s): " +
                string.Join(", ", Samples.Select(s => s.FileName)));
            foreach (ITestScenario scenario in All)
            {
                recorder.BeginScenario(scenario.Id, scenario.Description);
                scenario.Run(recorder, player);
            }

            recorder.Info("[tab.test] All scenarios finished.");
        }

        internal static void ListScenarioIds(ITestReporter reporter)
        {
            reporter.Info($"Samples ({Samples.Count}):");
            foreach (PayloadSample sample in Samples)
                reporter.Info($"  {sample.FileName} → {sample.Slug}");

            reporter.Info($"Scenarios ({All.Count}):");
            foreach (ITestScenario scenario in All)
                reporter.Info($"  {scenario.Id} — {scenario.Description}");
        }

        internal static bool TryResolveLegacyAlias(string token, out string? scenarioId)
        {
            PayloadSample? first = Samples.FirstOrDefault();
            PayloadSample? second = Samples.Count > 1 ? Samples[1] : null;

            scenarioId = token switch
            {
                "selection" => first is null ? null : $"selection_bounds_{first.Slug}",
                "projection" => first is null ? null : $"projection_capture_{first.Slug}",
                "import" => first is null ? null : $"import_{first.Slug}",
                "import_complex" => second is null ? null : $"import_{second.Slug}",
                "exec" => first is null ? null : $"paste_{first.Slug}",
                "exec_complex" => second is null ? null : $"paste_{second.Slug}",
                "cut" => first is null ? null : $"paste_cut_source_{first.Slug}",
                "cut_on" => first is null ? null : $"cut_on_{first.Slug}",
                "cut_off" => first is null ? null : $"cut_off_{first.Slug}",
                "cut_toggle" => first is null ? null : $"cut_source_toggle_{first.Slug}",
                _ => null,
            };
            return scenarioId is not null;
        }
    }
}
#endif
