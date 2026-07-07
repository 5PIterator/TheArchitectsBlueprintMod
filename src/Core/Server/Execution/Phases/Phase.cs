using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Phases
{
    /// <summary>Shared lifecycle for a paste operation stage.</summary>
    internal abstract class Phase
    {
        protected readonly PastingOperation Operation;

        protected Phase(PastingOperation operation, bool active, int tickSize, int tickSpeed)
        {
            Operation = operation;
            Active = active;
            TickSize = Math.Max(1, Math.Min(tickSize, operation.PasteConfig.Limits.Server.MaxPhaseTickSize));
            TickSpeed = Math.Max(0, tickSpeed);
        }

        public bool Active { get; }

        public int TickSize { get; }

        public int TickSpeed { get; }

        public abstract string StageName { get; }

        public abstract PasteInfoPhase? InfoPhase { get; }

        public virtual void OnEnter() { }

        public abstract void ExecuteTick();

        public abstract Phase? Next(PastingOperation op);

        public abstract int CountRemaining();

        protected int ProcessClearQueue(
            Queue<ClearingTask> queue,
            in PasteContext context,
            int tickSize,
            List<Point> currentStepTiles,
            HashSet<Point>? overlayDoneThisLayer)
        {
            int success = 0;
            int batchTotal = Math.Min(tickSize, queue.Count);
            for (int i = 0; i < batchTotal; i++)
            {
                if (queue.Count == 0)
                    break;

                ClearingTask task = queue.Dequeue();
                PasteProgress progress = Operation.CreateBatchProgress(i + 1, batchTotal);
                task.AddTilesTo(currentStepTiles, Operation.PasteConfig.Limits.Server.MaxTilesPerCategory);
                if (task.Execute(in context, in progress))
                {
                    Operation.RecordTaskCompletion();
                    success++;
                    if (overlayDoneThisLayer is not null)
                    {
                        for (int ti = 0; ti < task.Tiles.Count; ti++)
                            overlayDoneThisLayer.Add(task.Tiles[ti].World);
                    }
                }
                else
                {
                    queue.Enqueue(task);
                    if (task.Tiles.Count > 0)
                        TABLog.Debug($"Requeue clear task world=({task.Tiles[0].World.X},{task.Tiles[0].World.Y}) remainingTiles={task.Tiles.Count}", "Paste");
                }
            }

            return success;
        }

        protected int ProcessPlaceQueue(
            Queue<PlacingTask> queue,
            in PasteContext context,
            int tickSize,
            List<Point> currentStepTiles,
            HashSet<Point>? overlayDoneThisLayer,
            HashSet<Point>? successfulPlacedWorldTiles)
        {
            int success = 0;
            int batchTotal = Math.Min(tickSize, queue.Count);
            for (int i = 0; i < batchTotal; i++)
            {
                if (queue.Count == 0)
                    break;

                PlacingTask task = queue.Dequeue();
                PasteProgress progress = Operation.CreateBatchProgress(i + 1, batchTotal);
                task.AddTilesTo(currentStepTiles, Operation.PasteConfig.Limits.Server.MaxTilesPerCategory);
                if (task.Execute(in context, in progress))
                {
                    Operation.RecordTaskCompletion();
                    success++;
                    if (overlayDoneThisLayer is not null)
                    {
                        for (int ti = 0; ti < task.Tiles.Count; ti++)
                            overlayDoneThisLayer.Add(task.Tiles[ti].World);
                    }
                    if (successfulPlacedWorldTiles is not null)
                    {
                        for (int ti = 0; ti < task.Tiles.Count; ti++)
                            successfulPlacedWorldTiles.Add(task.Tiles[ti].World);
                    }
                }
                else
                {
                    queue.Enqueue(task);
                    if (task.Tiles.Count > 0)
                        TABLog.Debug($"Requeue place task world=({task.Tiles[0].World.X},{task.Tiles[0].World.Y}) remainingTiles={task.Tiles.Count}", "Paste");
                }
            }

            return success;
        }
    }

    /// <summary>Per-phase policy: execution pacing and the phase-specific strategy object.</summary>
    public sealed class PastePhasePolicy<TStrategy>
    {
        public bool Active = true;

        public int TickSize = 1;

        public int TickSpeed = 100;

        public TStrategy Strategy = default!;
    }

    /// <summary>Typed paste phase with policy-bound strategy settings.</summary>
    internal abstract class Phase<TStrategy> : Phase
    {
        public PastePhasePolicy<TStrategy> EffectivePolicy { get; }

        protected Phase(PastingOperation operation, PastePhasePolicy<TStrategy> policy)
            : base(operation, policy.Active, policy.TickSize, policy.TickSpeed)
        {
            EffectivePolicy = policy;
        }

        public TStrategy Strategy => EffectivePolicy.Strategy;
    }
}
