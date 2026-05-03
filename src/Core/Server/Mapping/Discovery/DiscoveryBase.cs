using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.ModUtils;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.TileMapping
{
    /// <summary>
    /// Base for discovery processes. Provides shared path resolution, JSON options, save helpers, and test chamber setup.
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

        protected static (int X, int Y)[] GetTestChamberRingOffsets() =>
        [
            (-1, -1), (0, -1), (1, -1),
            (-1, 0),         (1, 0),
            (-1, 1),  (0, 1), (1, 1),
        ];

        protected static List<(int x, int y, bool hasTile, int tileType, int wallType)> SetupTestChamber(int centerX, int centerY)
        {
            var saved = new List<(int, int, bool, int, int)>();
            foreach (var (dx, dy) in GetTestChamberRingOffsets())
            {
                int x = centerX + dx;
                int y = centerY + dy;
                if (!WorldGen.InWorld(x, y)) continue;
                var t = Main.tile[x, y];
                saved.Add((x, y, t.HasTile, t.TileType, t.WallType));
                SuppressTileDrop.KillTile(x, y);
                WorldGen.KillWall(x, y, fail: false);
                WorldGen.PlaceTile(x, y, TileID.DemoniteBrick, mute: true, forced: true, plr: -1, style: 0);
            }
            return saved;
        }

        protected static void TeardownTestChamber(int centerX, int centerY, List<(int x, int y, bool hasTile, int tileType, int wallType)>? saved)
        {
            if (saved is null) return;
            foreach (var (x, y, hasTile, tileType, wallType) in saved)
            {
                if (!WorldGen.InWorld(x, y)) continue;
                SuppressTileDrop.KillTile(x, y);
                WorldGen.KillWall(x, y, fail: false);
                if (wallType != WallID.None)
                    WorldGen.PlaceWall(x, y, (ushort)wallType, mute: true);
                if (hasTile)
                    WorldGen.PlaceTile(x, y, tileType, mute: true, forced: true, plr: -1, style: 0);
            }
        }
    }
}
