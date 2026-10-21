#if DEBUG
#nullable enable

using System;
using System.IO;
using System.Reflection;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TestCases
{
    /// <summary>Resolves payload fixture files from the ModSources tree.</summary>
    internal static class PayloadFixturePaths
    {
        private const string PayloadsDirectory = "payloads";

        /// <summary>Locates a fixture under src/tests/payloads by walking up from the assembly, then falling back to ModSources.</summary>
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

            error = $"Fixture not found: {fileName} (searched assembly parents and ModSources).";
            return false;
        }

        private static bool TryResolveFromAssembly(string fileName, out string fullPath)
        {
            // --- Walk parent directories looking for src/tests/payloads/<file>.
            fullPath = string.Empty;
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "src", "tests", PayloadsDirectory, fileName);
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
            // --- Fall back to Documents/My Games/.../ModSources/<mod>/src/tests/payloads.
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
                PayloadsDirectory,
                fileName);

            return File.Exists(fullPath);
        }
    }
}
#endif
