using System;
using System.IO;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Compiles source .itr maps into a single compiled InteractionMap document.
    /// </summary>
    internal static class ItrCompiler
    {
        public static void Compile(Mod mod, Action<string> msg)
        {
            var map = new InteractionMap();
            var directory = TileMappingSystem.GetSourceMapsDirectory();

            msg("Loading SourceMaps into InteractionMap...");
            ItrLoader.LoadAllInto(directory, map, msg);

            msg("Writing unified interaction maps...");
            SaveUnifiedMaps(mod, map, msg);
        }

        private static void SaveUnifiedMaps(Mod mod, InteractionMap map, Action<string> msg)
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

                msg($"Compiled interaction map written: {binaryPath}");
                msg($"Readable interaction map written: {jsonPath}");
            }
            catch (Exception ex)
            {
                msg($"Failed to save unified interaction maps: {ex.Message}");
            }
        }
    }
}
