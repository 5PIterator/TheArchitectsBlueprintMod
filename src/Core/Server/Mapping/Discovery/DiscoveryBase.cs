using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheArchitectsBlueprintMod.Debug;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core.TileMapping
{
    /// <summary>
    /// Base for discovery processes. Provides shared path resolution, JSON options, save helpers,
    /// and TABLog routing under the TileMapping category.
    /// World-tile sandboxing lives in <see cref="TestChamberSystem"/>; destructive edits use <see cref="SuppressTileDrop"/>.
    /// </summary>
    internal abstract class DiscoveryBase
    {
        /// <summary>ClientLogConfig / TABLog category for all discovery and compile progress.</summary>
        protected const string LogCategory = "TileMapping";

        protected static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        protected static string GetOutputPath(string stem) =>
            TileMappingSystem.GetSourceMapPath(TileMappingSystem.GetSourceMapFileName(stem));

        /// <summary>Logs a high-level discovery/compile progress line.</summary>
        protected static void Log(string message) =>
            TABLog.Info(message, LogCategory);

        /// <summary>Logs a verbose per-item discovery detail line.</summary>
        protected static void LogDebug(string message) =>
            TABLog.Debug(message, LogCategory);

        /// <summary>Logs a discovery warning immediately.</summary>
        protected static void LogWarn(string message) =>
            TABLog.Warn(message, LogCategory);

        /// <summary>Enumerates placement styles for a frame tile from TileObjectData random/wrap ranges.</summary>
        protected static IEnumerable<int> EnumeratePlacementStyles(int tileId)
        {
            // --- Cover RandomStyleRange variants so captured style N still resolves to the placeable item.

            TileObjectData? data = TileObjectData.GetTileData(tileId, 0);
            if (data is null)
            {
                yield return 0;
                yield break;
            }

            int randomRange = data.RandomStyleRange > 0 ? data.RandomStyleRange : 1;
            int multiplier = data.StyleMultiplier > 0 ? data.StyleMultiplier : 1;
            int count = Math.Max(randomRange * multiplier, 1);
            for (int style = 0; style < count; style++)
                yield return style;
        }

        /// <summary>Serializes <paramref name="output"/> to <paramref name="path"/>; warns on failure.</summary>
        protected static void SaveJson<T>(string path, T output, string errorContext)
        {
            // --- Write indented camelCase JSON; surface IO failures through TABLog.

            try
            {
                var json = JsonSerializer.Serialize(output, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LogWarn($"Failed to save {errorContext}: {ex.Message}");
            }
        }
    }
}
