using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Phases
{
    public enum PlaceStyle
    {
        Place = 0,
        Replace = 1,
    }

    public readonly struct PlacePhaseStrategy
    {
        public bool RequireItems { get; init; }

        public bool RetryOnMissingItems { get; init; }

        public bool RetryOnInvalidPlacement { get; init; }

        public PlaceStyle Style { get; init; }
    }

    /// <summary>Executes place tasks layer-by-layer.</summary>
    internal sealed class PlacePhase : Phase<PlacePhaseStrategy>
    {
        public PlacePhase(PastingOperation operation, PastePhasePolicy<PlacePhaseStrategy> policy) : base(operation, policy) { }

        public override string StageName => "PLACE";

        public override PasteInfoPhase? InfoPhase => PasteInfoPhase.Place;

        public override Phase? Next(PastingOperation op) => op._sweepPhase.Active ? op._sweepPhase : null;

        public override int CountRemaining() => PastingOperation.CountQueueTasks(Operation.placeTasks);

        public override void OnEnter() => Operation.BeginInfoPhase(PasteInfoPhase.Place);

        public override void ExecuteTick()
        {
            // Finish in-flight wall/block mutations before Paint (and later layers) run.
            if (Operation.TryWaitForCurrentLayerEffects(Operation.placeTasks))
                return;

            Operation.AdvanceToNextNonEmptyLayer(Operation.placeTasks);
            if (Operation._currentLayerIndex >= Operation.placeTasks.Count)
            {
                Operation.AdvancePhase();
                return;
            }

            Operation.SyncOverlayDoneTrackingIfNeeded();
            var qPlace = Operation.placeTasks[Operation._currentLayerIndex];
            var layerPlace = Operation.Pipeline.PlaceOrder[Operation._currentLayerIndex];
            TABLog.Debug($"Place layer {layerPlace.Id} (idx={Operation._currentLayerIndex}) tasks={qPlace.Count}", "Paste");
            Operation._currentStepPlaceTiles.Clear();
            Operation._currentStepClearTiles.Clear();
            if (layerPlace.Id == LayerSystem.Frame.Id)
            {
                ProcessFramePlaceQueue(
                    qPlace,
                    Operation._currentLayerIndex,
                    Operation._currentStepPlaceTiles,
                    Operation._overlayDoneThisLayer);
                return;
            }

            ProcessPlaceQueue(
                qPlace,
                Operation.Context,
                TickSize,
                Operation._currentStepPlaceTiles,
                Operation._overlayDoneThisLayer,
                successfulPlacedWorldTiles: null);
        }

        /// <summary>Places the globally best ready frame candidate instead of blindly dequeuing FIFO tasks.</summary>
        private void ProcessFramePlaceQueue(
            Queue<PlacingTask> queue,
            int frameLayerIndex,
            List<Point> currentStepTiles,
            HashSet<Point>? overlayDoneThisLayer)
        {
            if (queue.Count == 0)
                return;

            int batchTotal = TickSize;
            for (int pass = 0; pass < batchTotal && queue.Count > 0; pass++)
            {
                PasteContext context = Operation.BuildFramePlaceContext(frameLayerIndex);
                IReadOnlyList<FrameOperations.FrameCandidate>? pending = context.PendingFramePlaceCandidates;
                if (pending is null || pending.Count == 0)
                    break;

                int globalIndex = FrameOperations.SelectNextGlobalPlaceCandidateIndex(pending, in context);
                if (globalIndex < 0)
                {
                    string blockers = FrameOperations.FormatPendingPlaceBlockers(pending, in context);
                    TABLog.Debug(
                        $"Frame place tick: no ready candidate queue={queue.Count} pending={pending.Count} {blockers}",
                        "Paste");

                    // Retry only while a pending frame supporter may still appear; otherwise drop stuck tasks.
                    if (FrameOperations.HasPendingPlaceSupportWait(pending, in context)
                        || (context.RetryOnMissingItems && HasPendingMissingItems(pending, in context)))
                    {
                        break;
                    }

                    DropPermanentlyBlockedFrameTasks(queue, pending, overlayDoneThisLayer);
                    break;
                }

                Point selectedOrigin = pending[globalIndex].OriginWorld;
                if (!TryExtractFramePlaceTask(queue, selectedOrigin, out PlacingTask readyTask))
                {
                    TABLog.Debug(
                        $"Frame place tick: ready origin=({selectedOrigin.X},{selectedOrigin.Y}) missing from queue",
                        "Paste");
                    break;
                }

                TABLog.Debug(
                    $"Frame place tick: selected origin=({selectedOrigin.X},{selectedOrigin.Y}) queue={queue.Count}",
                    "Paste");
                PasteProgress progress = Operation.CreateBatchProgress(pass + 1, batchTotal);
                readyTask.AddTilesTo(currentStepTiles, Operation.PasteConfig.Limits.Server.MaxTilesPerCategory);
                if (readyTask.Execute(in context, in progress, out PasteProgressOutcome outcome, out var failReason, out string? detail))
                {
                    Operation.RecordTaskCompletion(outcome, failReason, detail);
                    if (overlayDoneThisLayer is not null)
                    {
                        for (int ti = 0; ti < readyTask.Tiles.Count; ti++)
                            overlayDoneThisLayer.Add(readyTask.Tiles[ti].World);
                    }
                }
                else
                {
                    queue.Enqueue(readyTask);
                    if (readyTask.Tiles.Count > 0)
                    {
                        TABLog.Debug(
                            $"Requeue place task world=({readyTask.Tiles[0].World.X},{readyTask.Tiles[0].World.Y}) remainingTiles={readyTask.Tiles.Count}",
                            "Paste");
                    }
                }
            }
        }

        /// <summary>Removes and returns the queued place task that owns <paramref name="originWorld"/>.</summary>
        private static bool TryExtractFramePlaceTask(
            Queue<PlacingTask> queue,
            Point originWorld,
            out PlacingTask readyTask)
        {
            readyTask = null!;
            int queueCount = queue.Count;
            var retained = new List<PlacingTask>(queueCount);
            for (int i = 0; i < queueCount; i++)
            {
                PlacingTask task = queue.Dequeue();
                if (readyTask is null && FrameOperations.BatchContainsPlaceOrigin(task.Tiles, originWorld))
                    readyTask = task;
                else
                    retained.Add(task);
            }

            for (int i = 0; i < retained.Count; i++)
                queue.Enqueue(retained[i]);

            return readyTask is not null;
        }

        /// <summary>Returns true when any pending frame candidate is waiting on inventory for its place item.</summary>
        private static bool HasPendingMissingItems(
            IReadOnlyList<FrameOperations.FrameCandidate> pending,
            in PasteContext context)
        {
            // --- Missing-item retries are only meaningful when require+retry flags are both on.
            if (!context.RequireItems || context.IsSweep)
                return false;

            for (int i = 0; i < pending.Count; i++)
            {
                if (!FrameOperations.HasRequiredItemsForFrameCandidate(pending[i], in context))
                    return true;
            }

            return false;
        }

        /// <summary>Dequeues every frame place task and records invalid-placement failures so later layers can run.</summary>
        private void DropPermanentlyBlockedFrameTasks(
            Queue<PlacingTask> queue,
            IReadOnlyList<FrameOperations.FrameCandidate> pending,
            HashSet<Point>? overlayDoneThisLayer)
        {
            // --- Permanently invalid candidates must leave the queue; retryOnInvalidPlacement does not apply here.
            while (queue.Count > 0)
            {
                PlacingTask task = queue.Dequeue();
                string detail = FrameOperations.FormatPendingPlaceBlockers(pending, Operation.Context);
                Operation.RecordTaskCompletion(
                    PasteProgressOutcome.Failed,
                    LayerFailReason.InvalidPlacement,
                    detail);
                if (overlayDoneThisLayer is not null)
                {
                    for (int ti = 0; ti < task.Tiles.Count; ti++)
                        overlayDoneThisLayer.Add(task.Tiles[ti].World);
                }

                TABLog.Debug(
                    $"Frame place tick: drop permanentlyBlocked remainingTiles={task.Tiles.Count} {detail}",
                    "Paste");
            }
        }
    }
}
