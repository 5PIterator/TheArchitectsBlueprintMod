#if DEBUG
#nullable enable

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Primary Test Type for a Test Case (see draft-test-plan skill).</summary>
    internal enum TestCaseType
    {
        Positive,
        Negative,
        Boundary,
        Performance,
        Regression,
        Other,
    }
}
#endif
