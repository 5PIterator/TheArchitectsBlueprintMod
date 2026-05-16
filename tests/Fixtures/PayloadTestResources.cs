#if DEBUG
#nullable enable

using System;
using System.Linq;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Tests.Samples;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Backward-compatible entry points; samples live under <c>tests/Samples</c>.</summary>
    internal static class PayloadTestResources
    {
        internal static bool TryLoadPayload(string fileName, out Payload payload, out string? error)
        {
            payload = default!;
            PayloadSample? sample = PayloadSampleCatalog.All.FirstOrDefault(
                s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (sample is null)
            {
                error = $"Unknown sample file '{fileName}'.";
                return false;
            }

            return PayloadSampleCatalog.TryLoad(sample, out payload, out error);
        }

        internal static bool TryLoadFixturePayload(out Payload payload, out string? error)
        {
            PayloadSample? sample = PayloadSampleCatalog.All.FirstOrDefault();
            if (sample is null)
            {
                payload = default!;
                error = "No samples in catalog.";
                return false;
            }

            return PayloadSampleCatalog.TryLoad(sample, out payload, out error);
        }

        internal static bool TryLoadFixtureDocument(out string text, out string? error)
        {
            PayloadSample? sample = PayloadSampleCatalog.All.FirstOrDefault();
            if (sample is null)
            {
                text = "";
                error = "No samples in catalog.";
                return false;
            }

            return PayloadSampleCatalog.TryLoadDocument(sample, out text, out error);
        }
    }
}
#endif
