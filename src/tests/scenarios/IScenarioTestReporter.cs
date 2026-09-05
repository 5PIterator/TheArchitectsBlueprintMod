#if DEBUG
#nullable enable

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Reporter abstraction so scenario output can be redirected.</summary>
    internal interface IScenarioTestReporter
    {
        void Info(string message);
        void Pass(string scenarioId, string message);
        void Fail(string scenarioId, string message);
        void Error(string scenarioId, string message);
    }
}
#endif
