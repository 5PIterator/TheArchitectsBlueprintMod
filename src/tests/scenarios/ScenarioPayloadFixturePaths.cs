#if DEBUG
#nullable enable

using System;
using System.IO;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Resolves payload fixture files from the local ModSources folder.</summary>
    internal static class ScenarioPayloadFixturePaths
    {
        private const string PayloadsDirectory = "payloads";

        public static bool TryResolve(string fileName, out string fullPath, out string? error)
        {
            fullPath = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "Fixture file name is required.";
                return false;
            }

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string modSourcesRoot = Path.Combine(
                documents,
                "My Games",
                "Terraria",
                "tModLoader",
                "ModSources",
                global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod.ModInternalName);
            fullPath = Path.Combine(modSourcesRoot, "src", "tests", PayloadsDirectory, fileName);

            if (File.Exists(fullPath))
                return true;

            error = $"Fixture not found: {fullPath}";
            return false;
        }
    }
}
#endif
