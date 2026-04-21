using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core.TileMapping
{
    /// <summary>
    /// Compiles source .itr into layer .itr files. Loads SourceMaps into InteractionMap, then exports by layer.
    /// </summary>
    internal static class ItrCompiler
    {
        private static readonly string[] Layers = ["Wall", "Tile", "Liquid", "Wire", "Paint"];

        public static void Compile(Mod mod, Action<string> msg)
        {
            var map = new InteractionMap();
            var directory = TileMappingSystem.GetSourceMapsDirectory();

            msg("Loading SourceMaps into InteractionMap...");
            ItrLoader.LoadAllInto(directory, map, msg);

            msg("Exporting to CompiledMaps by layer...");
            var byLayer = map.ExportByLayer();

            foreach (var layerName in Layers)
            {
                if (!byLayer.TryGetValue(layerName, out var data))
                    data = new Dictionary<string, TileFirstExportGroup>(StringComparer.OrdinalIgnoreCase);

                var path = ItrStorage.GetCompiledMapPath($"{layerName}.itr");
                SaveLayer(mod, path, data, layerName, msg);
                msg($"{layerName} layer: wrote {data.Count} entries to {path}");
            }
        }

        private static void SaveLayer(Mod mod, string path, Dictionary<string, TileFirstExportGroup> data, string errorContext, Action<string> msg)
        {
            try
            {
                var doc = new InteractionMapDocument<Dictionary<string, TileFirstExportGroup>>
                {
                    Metadata = InteractionMapMetadata.Create(mod, "tile-first", "compiled"),
                    Data = data,
                };
                var json = JsonSerializer.Serialize(doc, InteractionMapJsonOptions.TileFirst);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                msg($"Failed to save {errorContext}: {ex.Message}");
            }
        }
    }
}
