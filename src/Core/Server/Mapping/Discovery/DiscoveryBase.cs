using System;
using System.IO;
using System.Text.Json;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Base for discovery processes. Provides shared path resolution, JSON options, and save helpers.
    /// World-tile sandboxing lives in <see cref="TestChamberSystem"/>; destructive edits use <see cref="SuppressTileDrop"/>.
    /// </summary>
    internal abstract class DiscoveryBase
    {
        protected static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        protected static string GetDebugPath(string fileName) => TileMappingSystem.GetPath(fileName);
        protected static string GetOutputPath(string fileName) => TileMappingSystem.GetSourceMapPath(fileName);

        protected static void SaveJson<T>(string path, T output, Action<string> log, string errorContext)
        {
            try
            {
                var json = JsonSerializer.Serialize(output, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                log($"Failed to save {errorContext}: {ex.Message}");
            }
        }
    }
}
