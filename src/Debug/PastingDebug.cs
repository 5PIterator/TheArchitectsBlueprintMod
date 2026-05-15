using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Pasting debug helpers. All output goes through <see cref="TABLog"/> when <see cref="ClientLogConfig.ShowDebugLogs"/> is enabled.
/// </summary>
internal static class PastingDebug
{
    /// <summary>
    /// Call when a paste operation starts.
    /// </summary>
    internal static void OnPasteStart(Projection projection)
    {
        if (!TABLog.IsDebugEnabled) return;
        TABLog.Debug(
            $"Paste started: sourceClear={projection.CutSource}, targetArea=({projection.Area.X},{projection.Area.Y}) {projection.Area.Width}x{projection.Area.Height}",
            "Paste");
    }

    /// <summary>
    /// Call when a paste operation ends.
    /// </summary>
    internal static void OnPasteEnd()
    {
        if (!TABLog.IsDebugEnabled) return;
        TABLog.Debug("Paste ended", "Paste");
    }

    /// <summary>
    /// Logs a tile placement attempt with interaction resolution details.
    /// </summary>
    internal static void LogPlacement(
        ushort tileType,
        int style,
        int? baseTileId,
        int x,
        int y,
        bool success,
        string phase)
    {
        if (!TABLog.IsDebugEnabled) return;

        string tileName = ContentNameResolver.GetTileName(tileType) ?? $"Tile{tileType}";
        string mode = baseTileId.HasValue ? "conversion" : "direct";
        string baseName = baseTileId.HasValue ? (ContentNameResolver.GetTileName(baseTileId.Value) ?? $"Tile{baseTileId}") : "-";
        string result = success ? "OK" : "FAIL";

        TABLog.Debug($"Place [{phase}] ({x},{y}) {tileName} style={style} mode={mode} base={baseName} => {result}", "Paste");
    }

    /// <summary>
    /// Logs interaction lookup result for a tile. Helps diagnose priority issues.
    /// </summary>
    internal static void LogInteractionLookup(ushort tileType, int style, int? baseTileId, string? firstItem)
    {
        if (!TABLog.IsDebugEnabled) return;

        string tileName = ContentNameResolver.GetTileName(tileType) ?? $"Tile{tileType}";
        string mode = baseTileId.HasValue ? "conversion" : "direct";
        TABLog.Debug($"Interaction lookup: {tileName} style={style} => first={firstItem ?? "?"} mode={mode}", "Paste");
    }
}
