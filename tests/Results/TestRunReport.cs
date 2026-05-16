#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TheArchitectsBlueprintMod.Tests.Results
{
    internal enum TestStepKind { Info, Pass, Fail }

    internal sealed class TestStepRecord(string name, TestStepKind kind, string? detail = null)
    {
        public string Name { get; } = name;
        public TestStepKind Kind { get; } = kind;
        public string? Detail { get; } = detail;
        public DateTime Utc { get; } = DateTime.UtcNow;
    }

    internal sealed class TestScenarioRecord(string id, string description)
    {
        public string Id { get; } = id;
        public string Description { get; } = description;
        public List<TestStepRecord> Steps { get; } = [];
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public DateTime? EndedUtc { get; private set; }

        public bool Passed => Steps.All(s => s.Kind != TestStepKind.Fail);

        internal void End() => EndedUtc = DateTime.UtcNow;
    }

    internal sealed class TestRunReport(string runId, string mode)
    {
        public string RunId { get; } = runId;
        public string Mode { get; } = mode;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public DateTime? EndedUtc { get; private set; }
        public List<TestScenarioRecord> Scenarios { get; } = [];

        public int PassedCount => Scenarios.Count(s => s.Passed);
        public int FailedCount => Scenarios.Count(s => !s.Passed);

        internal void End() => EndedUtc = DateTime.UtcNow;

        internal string WriteToDisk()
        {
            End();
            string dir = TestResultsPaths.GetResultsDirectory();
            string stamp = RunId;

            string jsonPath = Path.Combine(dir, $"run_{stamp}.json");
            string txtPath = Path.Combine(dir, $"run_{stamp}.txt");
            string latestJson = Path.Combine(dir, "latest.json");
            string latestTxt = Path.Combine(dir, "latest.txt");

            string json = ToJson();
            string text = ToPlainText();

            File.WriteAllText(jsonPath, json, Encoding.UTF8);
            File.WriteAllText(txtPath, text, Encoding.UTF8);
            File.WriteAllText(latestJson, json, Encoding.UTF8);
            File.WriteAllText(latestTxt, text, Encoding.UTF8);

            return dir;
        }

        internal string ToPlainText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"The Architect's Blueprint — test run {RunId}");
            sb.AppendLine($"Mode: {Mode}");
            sb.AppendLine($"Started: {StartedUtc:u}");
            if (EndedUtc is DateTime ended)
                sb.AppendLine($"Ended:   {ended:u}");
            sb.AppendLine($"Scenarios: {PassedCount} passed, {FailedCount} failed, {Scenarios.Count} total");
            sb.AppendLine();

            foreach (TestScenarioRecord scenario in Scenarios)
            {
                sb.AppendLine(scenario.Passed ? $"[PASS] {scenario.Id}" : $"[FAIL] {scenario.Id}");
                sb.AppendLine($"       {scenario.Description}");
                foreach (TestStepRecord step in scenario.Steps)
                {
                    string label = step.Kind switch
                    {
                        TestStepKind.Pass => "PASS",
                        TestStepKind.Fail => "FAIL",
                        _ => "INFO",
                    };
                    string line = $"  {label,-4} {step.Name}";
                    if (!string.IsNullOrEmpty(step.Detail))
                        line += $" — {step.Detail}";
                    sb.AppendLine(line);
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        internal string ToJson()
        {
            var root = new JObject
            {
                ["runId"] = RunId,
                ["mode"] = Mode,
                ["startedUtc"] = StartedUtc.ToString("o", CultureInfo.InvariantCulture),
                ["endedUtc"] = EndedUtc?.ToString("o", CultureInfo.InvariantCulture),
                ["passed"] = PassedCount,
                ["failed"] = FailedCount,
                ["total"] = Scenarios.Count,
            };

            var scenarios = new JArray();
            foreach (TestScenarioRecord scenario in Scenarios)
            {
                var steps = new JArray();
                foreach (TestStepRecord step in scenario.Steps)
                {
                    steps.Add(new JObject
                    {
                        ["name"] = step.Name,
                        ["kind"] = step.Kind.ToString(),
                        ["detail"] = step.Detail is null ? JValue.CreateNull() : step.Detail,
                        ["utc"] = step.Utc.ToString("o", CultureInfo.InvariantCulture),
                    });
                }

                scenarios.Add(new JObject
                {
                    ["id"] = scenario.Id,
                    ["description"] = scenario.Description,
                    ["passed"] = scenario.Passed,
                    ["startedUtc"] = scenario.StartedUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["endedUtc"] = scenario.EndedUtc?.ToString("o", CultureInfo.InvariantCulture),
                    ["steps"] = steps,
                });
            }

            root["scenarios"] = scenarios;
            return root.ToString(Formatting.Indented);
        }
    }
}
#endif
