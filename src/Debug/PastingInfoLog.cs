using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Emits concise info diagnostics for paste phase execution.
/// </summary>
internal static class PastingInfoLog
{
    /// <summary>
    /// Emits one transport-friendly progress payload to the chat/info sink.
    /// </summary>
    internal static void Write(in PasteProgress progress)
    {
        switch (progress.Kind)
        {
            case PasteProgressKind.PhaseStart:
            {
                string suffix = progress.PhaseTotal > 0 ? $" (tasks={progress.PhaseTotal})" : string.Empty;
                TABLog.Info($"--- [{progress.Phase}] START{suffix} ---", chatColor: Color.LightSkyBlue);
                return;
            }
            case PasteProgressKind.PhaseEnd:
            {
                Color color = progress.PhaseRemaining == 0 ? Color.LightGreen : Color.LightYellow;
                TABLog.Info(
                    $"--- [{progress.Phase}] END (processed={progress.PhaseProcessed}, remaining={progress.PhaseRemaining}) ---",
                    chatColor: color);
                return;
            }
            case PasteProgressKind.TaskResult:
            {
                string contentName = ResolveContentName(in progress);
                string result = ResolveResult(in progress);
                Color color = ResolveResultColor(progress.Outcome);
                string message =
                    $"[{progress.Phase}] [{progress.BatchIndex}/{progress.BatchTotal}] [{progress.PhaseIndex}/{progress.PhaseTotal}] [{progress.WorldX}, {progress.WorldY}] {contentName} - {result}";
                TABLog.Info(message, chatColor: color);
                return;
            }
            default:
                return;
        }
    }

    /// <summary>
    /// Returns whether sweep applied the payload intent at the destination world tile.
    /// </summary>
    internal static bool IsSweepSatisfied(in PasteTile tile)
    {
        int x = tile.World.X, y = tile.World.Y;
        if (!WorldGen.InWorld(x, y))
            return false;

        Tile world = Main.tile[x, y];
        if (tile.PayloadCell.IsEmpty)
            return !world.HasTile && world.WallType == WallID.None;

        if (tile.PayloadCell.HasTile())
            return world.HasTile && world.TileType == tile.PayloadCell.Block.Type;

        if (tile.PayloadCell.Wall.Type != WallID.None)
            return world.WallType == tile.PayloadCell.Wall.Type;

        return true;
    }

    /// <summary>
    /// Describes why <see cref="IsSweepSatisfied"/> failed for a sweep tile.
    /// </summary>
    internal static string ExplainSweepFailure(in PasteTile tile)
    {
        int x = tile.World.X, y = tile.World.Y;
        if (!WorldGen.InWorld(x, y))
            return "target tile outside world";

        Tile world = Main.tile[x, y];
        if (tile.PayloadCell.IsEmpty)
        {
            if (world.HasTile)
                return "tile not cleared";
            if (world.WallType != WallID.None)
                return "wall not cleared";
            return "vacant intent not applied";
        }

        if (tile.PayloadCell.HasTile() && (!world.HasTile || world.TileType != tile.PayloadCell.Block.Type))
            return "tile not placed";

        if (tile.PayloadCell.Wall.Type != WallID.None && world.WallType != tile.PayloadCell.Wall.Type)
            return "wall not placed";

        return "intent not applied";
    }

    private static string ResolveContentName(in PasteProgress progress)
    {
        switch (progress.ContentKind)
        {
            case PasteProgressContentKind.Air:
                return "Vanilla:Air";
            case PasteProgressContentKind.Tile:
                return NormalizeContentName(ContentNameResolver.GetTileName(progress.ContentType));
            case PasteProgressContentKind.Wall:
                return NormalizeContentName(ContentNameResolver.GetWallName(progress.ContentType));
            default:
                return "Vanilla:Unknown";
        }
    }

    private static string NormalizeContentName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "Vanilla:Unknown";

        return rawName.Contains(':') ? rawName : $"Vanilla:{rawName}";
    }

    private static string ResolveResult(in PasteProgress progress)
    {
        return progress.Outcome switch
        {
            PasteProgressOutcome.Ok => "OK",
            PasteProgressOutcome.Retry =>
                $"Retry: {ExplainFailReason(progress.FailReason, progress.ResultDetail)}",
            PasteProgressOutcome.Skipped =>
                $"Skipped: {ExplainFailReason(progress.FailReason, progress.ResultDetail)}",
            PasteProgressOutcome.Failed =>
                $"Error: {ExplainFailReason(progress.FailReason, progress.ResultDetail)}",
            _ => "Error: unknown issue",
        };
    }

    private static Color ResolveResultColor(PasteProgressOutcome outcome) =>
        outcome switch
        {
            PasteProgressOutcome.Ok => Color.LightGreen,
            PasteProgressOutcome.Retry => Color.Khaki,
            PasteProgressOutcome.Skipped => Color.LightYellow,
            PasteProgressOutcome.Failed => Color.OrangeRed,
            _ => Color.OrangeRed,
        };

    private static string ExplainFailReason(LayerFailReason reason, string? detail)
    {
        if (reason != LayerFailReason.None)
            return ExplainFailReason(reason);

        if (!string.IsNullOrWhiteSpace(detail))
            return detail;

        return "unknown issue";
    }

    private static string ExplainFailReason(LayerFailReason reason) =>
        reason switch
        {
            LayerFailReason.None => "unknown issue",
            LayerFailReason.MissingItems => "missing items",
            LayerFailReason.InvalidPlacement => "invalid placement",
            LayerFailReason.InsufficientPickaxePower => "insufficient pickaxe power",
            LayerFailReason.ClearBlocked => "clear blocked",
            LayerFailReason.Unknown => "unknown block",
            _ => "unknown issue",
        };
}
