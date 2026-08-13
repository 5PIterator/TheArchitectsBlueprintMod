using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Pasting debug helpers. All output goes through <see cref="TABLog"/> and is always written to the mod log.
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
        string phase,
        ushort? itemCheckTileType = null,
        TileItemAvailability? itemIssue = null,
        int? alternate = null,
        short? frameX = null,
        short? frameY = null)
    {
        // --- Emit style/mode result, and include alternate/frame when provided for multi-style diagnosis.

        if (!TABLog.IsDebugEnabled) return;

        string tileName = ContentNameResolver.GetTileName(tileType) ?? $"Tile{tileType}";
        string mode = baseTileId.HasValue ? "conversion" : "direct";
        string baseName = baseTileId.HasValue ? (ContentNameResolver.GetTileName(baseTileId.Value) ?? $"Tile{baseTileId}") : "-";
        string result = success ? "OK" : FormatPlacementFailure(itemCheckTileType, itemIssue);

        // Append alternate/frame when callers supply them (PlaceObject multi-style path).
        string variant = string.Empty;
        if (alternate.HasValue || frameX.HasValue || frameY.HasValue)
        {
            string altText = alternate.HasValue ? alternate.Value.ToString() : "?";
            string fxText = frameX.HasValue ? frameX.Value.ToString() : "?";
            string fyText = frameY.HasValue ? frameY.Value.ToString() : "?";
            variant = $" alt={altText} frame=({fxText},{fyText})";
        }

        TABLog.Debug(
            $"Place [{phase}] ({x},{y}) {tileName} style={style}{variant} mode={mode} base={baseName} => {result}",
            "Paste");
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

    private static string FormatPlacementFailure(ushort? itemCheckTileType, TileItemAvailability? itemIssue)
    {
        if (!itemIssue.HasValue || itemIssue == TileItemAvailability.Available)
            return "FAIL";

        string checkedName = itemCheckTileType.HasValue
            ? (ContentNameResolver.GetTileName(itemCheckTileType.Value) ?? $"Tile{itemCheckTileType}")
            : "?";
        string reason = itemIssue switch
        {
            TileItemAvailability.MissingFromInventory => "missing item",
            TileItemAvailability.UnresolvedInteraction => "unresolved interaction",
            _ => "FAIL",
        };
        return $"FAIL ({reason} for {checkedName})";
    }
}
