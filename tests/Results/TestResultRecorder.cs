#if DEBUG
#nullable enable

using System;

namespace TheArchitectsBlueprintMod.Tests.Results
{
    /// <summary>Records test output and writes a run report when disposed.</summary>
    internal sealed class TestResultRecorder : ITestReporter, IDisposable
    {
        private readonly ITestReporter _sink;
        private readonly TestRunReport _report;
        private TestScenarioRecord? _currentScenario;

        private TestResultRecorder(ITestReporter sink, TestRunReport report)
        {
            _sink = sink;
            _report = report;
        }

        internal static TestResultRecorder Begin(ITestReporter sink, string mode)
        {
            string runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return new TestResultRecorder(sink, new TestRunReport(runId, mode));
        }

        internal void BeginScenario(string id, string description)
        {
            _currentScenario?.End();
            _currentScenario = new TestScenarioRecord(id, description);
            _report.Scenarios.Add(_currentScenario);
        }

        internal string WriteResultsAndGetDirectory()
        {
            _currentScenario?.End();
            string dir = _report.WriteToDisk();
            string summary =
                $"[tab.test] Results: {_report.PassedCount}/{_report.Scenarios.Count} scenarios passed. " +
                $"See tests/Results/latest.txt";
            _sink.Info(summary);
            _sink.Info($"[tab.test] Written to: {dir}");
            return dir;
        }

        public void Info(string message)
        {
            _sink.Info(message);
            _currentScenario?.Steps.Add(new TestStepRecord(message, TestStepKind.Info));
        }

        public void Pass(string step)
        {
            _sink.Pass(step);
            _currentScenario?.Steps.Add(new TestStepRecord(step, TestStepKind.Pass));
        }

        public void Fail(string step, string? detail = null)
        {
            _sink.Fail(step, detail);
            _currentScenario?.Steps.Add(new TestStepRecord(step, TestStepKind.Fail, detail));
        }

        public void Dispose() => WriteResultsAndGetDirectory();
    }
}
#endif
