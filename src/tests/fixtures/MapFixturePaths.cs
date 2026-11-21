#if DEBUG
#nullable enable

using System;
using System.IO;
using System.Reflection;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Resolves InteractionMap fixture files from the ModSources tree.</summary>
    internal static class MapFixturePaths
    {
        private const string MapsDirectory = "maps";

        /// <summary>Locates a fixture under src/tests/maps by walking up from the assembly, then falling back to ModSources.</summary>
        public static bool TryResolve(string fileName, out string fullPath, out string? error)
        {
            // --- Validate the name, then search assembly parents and the default ModSources path.

            fullPath = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Fixture file name is required.";
                return false;
            }

            if (TryResolveFromAssembly(fileName, out fullPath))
                return true;

            if (TryResolveFromModSources(fileName, out fullPath))
                return true;

            error = $"Map fixture not found: {fileName} (searched assembly parents and ModSources).";
            return false;
        }

        private static bool TryResolveFromAssembly(string fileName, out string fullPath)
        {
            // --- Walk parent directories looking for src/tests/maps/<file>.
            fullPath = string.Empty;
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "src", "tests", MapsDirectory, fileName);
                if (File.Exists(candidate))
                {
                    fullPath = candidate;
                    return true;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return false;
        }

        private static bool TryResolveFromModSources(string fileName, out string fullPath)
        {
            // --- Fall back to Documents/My Games/.../ModSources/<mod>/src/tests/maps.
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            fullPath = Path.Combine(
                documents,
                "My Games",
                "Terraria",
                "tModLoader",
                "ModSources",
                global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod.ModInternalName,
                "src",
                "tests",
                MapsDirectory,
                fileName);

            return File.Exists(fullPath);
        }
    }
}
#endif
