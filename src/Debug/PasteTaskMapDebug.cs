using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Writes the task map built at the start of a PastingOperation to a debug JSON file.
/// </summary>
internal static class PasteTaskMapDebug
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static void WriteTaskMap(
        Projection projection,
        bool applySourceClear,
        LayerPipeline pipeline,
        IReadOnlyList<Queue<ClearingTask>> clearTasksByLayer,
        IReadOnlyList<Queue<PlacingTask>> placeTasksByLayer)
    {
        if (!TileMappingAuthority.UsesLocalMappingFilesystem())
            return;

        var dto = new TaskMapDto
        {
            ProjectionClearsSource = projection.CutSource,
            ApplySourceClear = applySourceClear,
            TargetArea = Rect(projection.Area),
            SourceArea = Rect(projection.SourceArea),
            SourceTileCount = projection.SourceTiles.Length,
            TargetTileCount = projection.TargetTiles.Length,
            ClearTasks = BuildClearMap(pipeline, clearTasksByLayer),
            PlaceTasks = BuildPlaceMap(pipeline, placeTasksByLayer),
        };

        var path = TileMappingSystem.GetPath("PasteTaskMap_Debug.json");
        try
        {
            var json = JsonSerializer.Serialize(dto, Options);
            File.WriteAllText(path, json);
        }
        catch { /* Debug file write failed; file will not exist */ }
    }

    private static object Rect(Rectangle r) => new { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    private static Dictionary<string, List<TaskPointDto>> BuildClearMap(
        LayerPipeline pipeline,
        IReadOnlyList<Queue<ClearingTask>> byLayer)
    {
        var map = new Dictionary<string, List<TaskPointDto>>();
        for (int i = 0; i < pipeline.ClearOrder.Count && i < byLayer.Count; i++)
        {
            var layer = pipeline.ClearOrder[i];
            var tasks = byLayer[i].ToArray();
            if (tasks.Length == 0) continue;
            map[layer.Id] = tasks.SelectMany(t => t.Tiles, (t, pt) => new TaskPointDto(pt.World.X, pt.World.Y)).ToList();
        }
        return map;
    }

    private static Dictionary<string, List<PlaceTaskDto>> BuildPlaceMap(
        LayerPipeline pipeline,
        IReadOnlyList<Queue<PlacingTask>> byLayer)
    {
        var map = new Dictionary<string, List<PlaceTaskDto>>();
        for (int i = 0; i < pipeline.PlaceOrder.Count && i < byLayer.Count; i++)
        {
            var layer = pipeline.PlaceOrder[i];
            var tasks = byLayer[i].ToArray();
            if (tasks.Length == 0) continue;
            map[layer.Id] = tasks.SelectMany(t => t.Tiles, (t, pt) => new PlaceTaskDto(pt.World.X, pt.World.Y, pt.Source)).ToList();
        }
        return map;
    }

    private sealed class TaskMapDto
    {
        public bool ProjectionClearsSource { get; set; }
        public bool ApplySourceClear { get; set; }
        public object TargetArea { get; set; }
        public object SourceArea { get; set; }
        public int SourceTileCount { get; set; }
        public int TargetTileCount { get; set; }
        public Dictionary<string, List<TaskPointDto>> ClearTasks { get; set; }
        public Dictionary<string, List<PlaceTaskDto>> PlaceTasks { get; set; }
    }

    private sealed class TaskPointDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public TaskPointDto(int x, int y) { X = x; Y = y; }
    }

    private sealed class PlaceTaskDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public ushort? TileType { get; set; }
        public ushort? WallType { get; set; }

        public PlaceTaskDto(int x, int y, Tile source)
        {
            X = x;
            Y = y;
            TileType = source.HasTile ? source.TileType : null;
            WallType = source.WallType != WallID.None ? source.WallType : null;
        }
    }
}
