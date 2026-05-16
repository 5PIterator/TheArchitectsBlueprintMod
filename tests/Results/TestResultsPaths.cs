#if DEBUG
#nullable enable

using System;
using System.IO;
using System.Reflection;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Tests.Results
{
    internal static class TestResultsPaths
    {
        /// <summary>Prefer <c>ModSources/.../tests/Results</c> when running a dev build; otherwise mod save folder.</summary>
        internal static string GetResultsDirectory()
        {
            string? modSources = TryFindModSourcesResultsDirectory();
            if (modSources is not null)
                return modSources;

            string fallback = Path.Combine(TileMappingSystem.GetModDirectory(), "test-results");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static string? TryFindModSourcesResultsDirectory()
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "TheArchitectsBlueprintMod.csproj")))
                {
                    string results = Path.Combine(dir, "tests", "Results");
                    Directory.CreateDirectory(results);
                    return results;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}
#endif
