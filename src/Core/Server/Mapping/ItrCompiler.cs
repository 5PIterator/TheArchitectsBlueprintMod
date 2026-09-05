using System;
using System.IO;
using TheArchitectsBlueprintMod.Debug;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Compiles source .itr maps into a single compiled InteractionMap document.
    /// </summary>
    internal static class ItrCompiler
    {
        private const string LogCategory = "TileMapping";

        public static void Compile(Mod mod)
        {
            var map = new InteractionMap();
            var directory = TileMappingSystem.GetSourceMapsDirectory();

            TABLog.Info("Loading SourceMaps into InteractionMap...", LogCategory);
            ItrLoader.LoadAllInto(directory, map);

            TABLog.Info("Writing unified interaction maps...", LogCategory);
            SaveUnifiedMaps(mod, map);
        }

        private static void SaveUnifiedMaps(Mod mod, InteractionMap map)
        {
            try
            {
                InteractionMapMetadata metadata = InteractionMapMetadata.Create(mod, format: "tile-first", mapType: "compiled");
                string binaryPath = ItrStorage.GetCompiledInteractionMapPath();
                string jsonPath = ItrStorage.GetCompiledInteractionMapJsonPath();

                using (var binary = File.Create(binaryPath))
                    InteractionMapTagIO.WriteBinaryDocument(binary, map, metadata);

                using (var json = File.Create(jsonPath))
                    InteractionMapTagIO.WriteJsonDocument(json, map, metadata);

                TABLog.Info($"Compiled interaction map written: {binaryPath}", LogCategory);
                TABLog.Info($"Readable interaction map written: {jsonPath}", LogCategory);
            }
            catch (Exception ex)
            {
                TABLog.Warn($"Failed to save unified interaction maps: {ex.Message}", LogCategory);
            }
        }
    }
}
