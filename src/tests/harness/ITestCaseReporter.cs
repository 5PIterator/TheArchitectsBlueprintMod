#if DEBUG
#nullable enable

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Test Report sink — Case output including errors and warnings.</summary>
    internal interface ITestCaseReporter
    {
        void Info(string message);
        void Pass(string caseTag, string message);
        void Fail(string caseTag, string message);
        void Error(string caseTag, string message);
    }
}
#endif
