#if DEBUG
#nullable enable

using System.Collections;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>
    /// In-game Test Case contract. Id is the Case Tag ({feature}_{T}{nn});
    /// Description is the Test Scenario title; Type matches the tag's type code;
    /// Run produces the Test Report via the reporter.
    /// </summary>
    internal interface ITestCase
    {
        /// <summary>Case Tag — e.g. paste_P01; paste-mp_P01 (file/class: paste_mp_P01).</summary>
        string Id { get; }

        /// <summary>Test Scenario — descriptive title only (not the tag).</summary>
        string Description { get; }

        /// <summary>Primary Test Type for this Case (must match the tag's type code).</summary>
        TestCaseType Type { get; }

        /// <summary>Runs the Test Case; may yield across frames for long operations such as paste.</summary>
        IEnumerator Run(Player player, ITestCaseReporter reporter);
    }
}
#endif
