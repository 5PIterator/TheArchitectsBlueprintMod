#if DEBUG
#nullable enable

using System.Collections.Generic;
using TheArchitectsBlueprintMod.Tests.Samples;

namespace TheArchitectsBlueprintMod.Tests.Scenarios
{
    internal static class ScenarioDiscovery
    {
        internal static IReadOnlyList<ITestScenario> DiscoverAll()
        {
            var list = new List<ITestScenario>();
            foreach (PayloadSample sample in PayloadSampleCatalog.All)
            {
                list.Add(new SelectionBoundsSampleScenario(sample));
                list.Add(new ProjectionCaptureSampleScenario(sample));
                list.Add(new CutSourceToggleSampleScenario(sample));
                list.Add(new ImportSampleScenario(sample));
                list.Add(new PasteSampleScenario(sample));
                list.Add(new CutSourceImportOnSampleScenario(sample));
                list.Add(new CutSourceImportOffSampleScenario(sample));
                list.Add(new PasteCutSourceOffsetSampleScenario(sample));
                list.Add(new PasteNoCutOffsetSampleScenario(sample));
                list.Add(new PayloadIoSampleScenario(sample, clearCenter: true));
                list.Add(new PayloadIoSampleScenario(sample, clearCenter: false));
            }

            return list;
        }
    }
}
#endif
