using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Phases
{
    public enum ClearItemDropStyle
    {
        Void = 0,
        Drop = 1,
        Store = 2,
    }

    public readonly struct ClearPhaseStrategy
    {
        public ClearItemDropStyle ItemDrop { get; init; }
    }

    /// <summary>Executes destination clear tasks layer-by-layer.</summary>
    internal sealed class ClearPhase : Phase<ClearPhaseStrategy>
    {
        public ClearPhase(PastingOperation operation, PastePhasePolicy<ClearPhaseStrategy> policy) : base(operation, policy) { }

        public override string StageName => "CLEAR";

        public override PasteInfoPhase? InfoPhase => PasteInfoPhase.Clear;

        public override Phase? Next(PastingOperation op)
        {
            if (op._cutPhase.Active)
                return op._cutPhase;
            if (op._placePhase.Active)
                return op._placePhase;
            if (op._sweepPhase.Active)
                return op._sweepPhase;
            return null;
        }

        public override int CountRemaining() => PastingOperation.CountQueueTasks(Operation.clearTasks);

        public override void OnEnter() => Operation.BeginInfoPhase(PasteInfoPhase.Clear);

        public override void ExecuteTick()
        {
            Operation.AdvanceToNextNonEmptyLayer(Operation.clearTasks);
            if (Operation._currentLayerIndex >= Operation.clearTasks.Count)
            {
                Operation.AdvancePhase();
                return;
            }

            Operation.SyncOverlayDoneTrackingIfNeeded();
            var qClear = Operation.clearTasks[Operation._currentLayerIndex];
            var layerClear = Operation.Pipeline.ClearOrder[Operation._currentLayerIndex];
            TABLog.Debug($"Clear layer {layerClear.Id} (idx={Operation._currentLayerIndex}) tasks={qClear.Count}", "Paste");
            Operation._currentStepClearTiles.Clear();
            Operation._currentStepPlaceTiles.Clear();
            ProcessClearQueue(qClear, Operation.Context, TickSize, Operation._currentStepClearTiles, Operation._overlayDoneThisLayer);
        }
    }
}
