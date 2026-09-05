using System.Collections.Generic;
using System.Text;
using Terraria;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.Phases;

namespace TheArchitectsBlueprintMod.Debug;

/// <summary>
/// Progress event kind used by sinks (chat now, network/overlay later).
/// </summary>
internal enum PasteProgressKind
{
    PhaseStart,
    TaskResult,
    PhaseEnd,
}

/// <summary>
/// Task-level result semantics for sinks (OK, successful progress, deferred retry, terminal failure).
/// </summary>
internal enum PasteProgressOutcome
{
    Ok,
    InProgress,
    Retry,
    Failed,
}

/// <summary>
/// High-level paste phases used by <see cref="PasteProgress"/> events and info diagnostics.
/// </summary>
internal enum PasteInfoPhase
{
    Plan,
    Clear,
    Cut,
    Place,
    Sweep,
}

/// <summary>
/// Central progress payload for chat sink, future net packets, and overlay feeds.
/// </summary>
internal readonly struct PasteProgress
{
    public int CorrelationId { get; init; }
    public int OwnerPlayer { get; init; }
    public int OverlayId { get; init; }
    public PasteProgressKind Kind { get; init; }
    public PasteInfoPhase Phase { get; init; }
    public string? LayerId { get; init; }
    public PlaceStyle PlacementStyle { get; init; }
    public int BatchIndex { get; init; }
    public int BatchTotal { get; init; }
    public int PhaseIndex { get; init; }
    public int PhaseTotal { get; init; }
    public int PhaseProcessed { get; init; }
    public int PhaseRemaining { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public string? FailureSummary { get; init; }
    public int WorldX { get; init; }
    public int WorldY { get; init; }
    public string? ContentMessage { get; init; }
    public string? OkLabel { get; init; }
    public PasteProgressOutcome Outcome { get; init; }
    public bool Success { get; init; }
    public LayerFailReason FailReason { get; init; }
    public string? ResultDetail { get; init; }

    public PasteProgress WithTaskResult(
        in PasteTile tile,
        PasteProgressOutcome outcome,
        string? contentMessage = null,
        string? okLabel = null,
        LayerFailReason failReason = LayerFailReason.None,
        string? detail = null,
        string? layerId = null) =>
        WithTaskResult(tile.World, outcome, contentMessage, okLabel, failReason, detail, layerId);

    public PasteProgress WithTaskResult(
        Microsoft.Xna.Framework.Point world,
        PasteProgressOutcome outcome,
        string? contentMessage = null,
        string? okLabel = null,
        LayerFailReason failReason = LayerFailReason.None,
        string? detail = null,
        string? layerId = null) =>
        new()
        {
            CorrelationId = CorrelationId,
            OwnerPlayer = OwnerPlayer,
            OverlayId = OverlayId,
            Kind = PasteProgressKind.TaskResult,
            Phase = Phase,
            LayerId = layerId ?? LayerId,
            PlacementStyle = PlacementStyle,
            BatchIndex = BatchIndex,
            BatchTotal = BatchTotal,
            PhaseIndex = PhaseIndex,
            PhaseTotal = PhaseTotal,
            PhaseProcessed = PhaseProcessed,
            PhaseRemaining = PhaseRemaining,
            SuccessCount = SuccessCount,
            FailedCount = FailedCount,
            FailureSummary = FailureSummary,
            WorldX = world.X,
            WorldY = world.Y,
            ContentMessage = contentMessage,
            OkLabel = okLabel,
            Outcome = outcome,
            Success = outcome is PasteProgressOutcome.Ok,
            FailReason = failReason,
            ResultDetail = detail,
        };

    /// <summary>
    /// Classifies a place attempt using paste policy and whether the task will leave the queue.
    /// </summary>
    internal static PasteProgressOutcome ClassifyPlaceAttempt(
        in LayerResult res,
        in PasteContext context,
        bool taskCompletes)
    {
        // --- Success with remaining work is progress; policy exits and hard failures are terminal.

        if (res.Success)
            return taskCompletes ? PasteProgressOutcome.Ok : PasteProgressOutcome.InProgress;

        if (!context.RetryOnMissingItems && res.FailReason == LayerFailReason.MissingItems)
            return PasteProgressOutcome.Failed;

        if (res.FailReason == LayerFailReason.UnresolvedInteraction)
            return PasteProgressOutcome.Failed;

        if (!context.RetryOnInvalidPlacement && res.FailReason == LayerFailReason.InvalidPlacement)
            return PasteProgressOutcome.Failed;

        if (!taskCompletes)
            return PasteProgressOutcome.Retry;

        return PasteProgressOutcome.Failed;
    }

    /// <summary>
    /// Classifies a clear attempt using whether the task will leave the queue.
    /// </summary>
    internal static PasteProgressOutcome ClassifyClearAttempt(in LayerResult res, bool taskCompletes)
    {
        // --- Success with remaining work is progress; policy exits and hard failures are terminal.

        if (res.Success)
            return taskCompletes ? PasteProgressOutcome.Ok : PasteProgressOutcome.InProgress;

        if (res.FailReason == LayerFailReason.InsufficientPickaxePower)
            return PasteProgressOutcome.Failed;

        if (!taskCompletes)
            return PasteProgressOutcome.Retry;

        return PasteProgressOutcome.Failed;
    }

    /// <summary>
    /// Appends in-batch progress to <paramref name="contentMessage"/>, e.g. <c>Trees (7/8)</c>.
    /// Omits suffix for single-tile <c>1/1</c> progress.
    /// </summary>
    internal static string? AppendBatchProgressToContent(
        string? contentMessage,
        TileBatch batch,
        in LayerResult res,
        PasteProgressOutcome outcome)
    {
        if (outcome != PasteProgressOutcome.InProgress || !res.Success || res.TileBatch is null || res.TileBatch.Tiles.Count == 0)
            return contentMessage;

        string? suffix = batch.FormatBatchProgressSuffix(batch.Count, res.TileBatch.Tiles.Count);
        if (suffix is null)
            return contentMessage;

        return string.IsNullOrEmpty(contentMessage) ? suffix.TrimStart() : contentMessage + suffix;
    }

    internal static bool HasBatchProgressSuffix(string? contentMessage) =>
        contentMessage is not null && System.Text.RegularExpressions.Regex.IsMatch(contentMessage, @"\(\d+/\d+\)$");

    /// <summary>
    /// True when the task leaves the queue without applying place/sweep intent.
    /// </summary>
    internal bool IsTerminalOutcome() => Outcome == PasteProgressOutcome.Failed;

    /// <summary>Returns a generic ok label when the layer did not supply one.</summary>
    internal string ResolveGenericOkLabel() =>
        Phase switch
        {
            PasteInfoPhase.Plan => "Planned",
            PasteInfoPhase.Clear => "Cleared",
            PasteInfoPhase.Cut => "Cleared",
            PasteInfoPhase.Place => PlacementStyle == PlaceStyle.Replace ? "Replaced" : "Placed",
            PasteInfoPhase.Sweep => "Sweeped",
            _ => "OK",
        };
}

/// <summary>
/// Tracks cumulative phase progress and builds per-task progress snapshots.
/// </summary>
internal sealed class PasteProgressTracker
{
    private readonly int _ownerPlayer;
    private readonly Dictionary<string, int> _failureCounts = new();

    public int PhaseTotal { get; private set; }
    public int PhaseProcessed { get; private set; }
    public int SuccessCount { get; private set; }
    public int FailedCount { get; private set; }
    public PasteInfoPhase Phase { get; private set; }
    public PlaceStyle PlacementStyle { get; set; } = PlaceStyle.Place;
    public int CorrelationId { get; set; }
    public int OverlayId { get; set; }

    public PasteProgressTracker(int ownerPlayer)
    {
        _ownerPlayer = ownerPlayer;
    }

    /// <summary>
    /// Opens a phase and resets per-phase totals used by end-of-phase summaries.
    /// </summary>
    public PasteProgress BeginPhase(PasteInfoPhase phase, int phaseTotal)
    {
        // --- Reset counters so each phase reports only its own completions and failures.

        Phase = phase;
        PhaseTotal = phaseTotal;
        PhaseProcessed = 0;
        SuccessCount = 0;
        FailedCount = 0;
        _failureCounts.Clear();

        return new PasteProgress
        {
            CorrelationId = CorrelationId,
            OwnerPlayer = _ownerPlayer,
            OverlayId = OverlayId,
            Kind = PasteProgressKind.PhaseStart,
            Phase = phase,
            PlacementStyle = PlacementStyle,
            PhaseTotal = phaseTotal,
        };
    }

    /// <summary>
    /// Builds a task snapshot for the current tick batch without advancing completed-task count.
    /// </summary>
    public PasteProgress ForBatch(int batchIndex, int batchTotal) => new()
    {
        CorrelationId = CorrelationId,
        OwnerPlayer = _ownerPlayer,
        OverlayId = OverlayId,
        Kind = PasteProgressKind.TaskResult,
        Phase = Phase,
        PlacementStyle = PlacementStyle,
        BatchIndex = batchIndex,
        BatchTotal = batchTotal,
        PhaseIndex = PhaseProcessed + 1,
        PhaseTotal = PhaseTotal,
        PhaseProcessed = PhaseProcessed,
        SuccessCount = SuccessCount,
        FailedCount = FailedCount,
    };

    /// <summary>Builds a plan-phase task log snapshot (no tick batch during queue build).</summary>
    public PasteProgress ForPlanTask() => new()
    {
        CorrelationId = CorrelationId,
        OwnerPlayer = _ownerPlayer,
        OverlayId = OverlayId,
        Kind = PasteProgressKind.TaskResult,
        Phase = PasteInfoPhase.Plan,
        PlacementStyle = PlacementStyle,
        BatchIndex = 0,
        BatchTotal = 0,
        PhaseIndex = 0,
        PhaseTotal = 0,
        PhaseProcessed = 0,
        SuccessCount = SuccessCount,
        FailedCount = FailedCount,
    };

    /// <summary>
    /// Records one task leaving the queue (success or terminal failure).
    /// </summary>
    public void RecordCompletion(
        PasteProgressOutcome outcome,
        LayerFailReason failReason = LayerFailReason.None,
        string? detail = null)
    {
        // --- Count only terminal exits; intermediate progress/retries stay in the queue.

        if (PhaseProcessed < PhaseTotal)
            PhaseProcessed++;

        if (outcome == PasteProgressOutcome.Ok)
        {
            SuccessCount++;
            return;
        }

        if (outcome != PasteProgressOutcome.Failed)
            return;

        FailedCount++;
        RecordFailureReason(failReason, detail);
    }

    /// <summary>
    /// Records the failure reason for a terminal execution failure.
    /// </summary>
    public void RecordFailureReason(LayerFailReason reason, string? detail = null)
    {
        string key = FormatFailureKey(reason, detail);
        _failureCounts[key] = _failureCounts.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    /// <summary>
    /// Builds the phase-end progress payload including success/error totals.
    /// </summary>
    public PasteProgress EndPhase(int remaining) => new()
    {
        CorrelationId = CorrelationId,
        OwnerPlayer = _ownerPlayer,
        OverlayId = OverlayId,
        Kind = PasteProgressKind.PhaseEnd,
        Phase = Phase,
        PlacementStyle = PlacementStyle,
        PhaseTotal = PhaseTotal,
        PhaseProcessed = PhaseProcessed,
        PhaseRemaining = remaining,
        SuccessCount = SuccessCount,
        FailedCount = FailedCount,
        FailureSummary = FormatFailureSummary(),
    };

    /// <summary>
    /// Formats grouped failure totals for phase-end summaries.
    /// </summary>
    public string FormatFailureSummary()
    {
        if (_failureCounts.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (KeyValuePair<string, int> pair in _failureCounts)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(pair.Value);
            sb.Append(' ');
            sb.Append(pair.Key);
        }

        return sb.ToString();
    }

    private static string FormatFailureKey(LayerFailReason reason, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail))
            return detail.Trim();

        return reason switch
        {
            LayerFailReason.MissingItems => "missing items",
            LayerFailReason.UnresolvedInteraction => "unresolved interaction",
            LayerFailReason.InvalidPlacement => "invalid placement",
            LayerFailReason.InsufficientPickaxePower => "insufficient pickaxe power",
            LayerFailReason.ClearBlocked => "clear blocked",
            LayerFailReason.Unknown => "unknown block",
            _ => "unknown issue",
        };
    }
}
