using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Phases
{
    public readonly struct CutPhaseStrategy
    {
        public bool EnableCutPasteProcess { get; init; }
    }

    /// <summary>Executes source-area cut clears layer-by-layer.</summary>
    internal sealed class CutPhase : Phase<CutPhaseStrategy>
    {
        public CutPhase(PastingOperation operation, PastePhasePolicy<CutPhaseStrategy> policy) : base(operation, policy) { }

        public override string StageName => "CUT";

        public override PasteInfoPhase? InfoPhase => PasteInfoPhase.Cut;

        public override Phase? Next(PastingOperation op)
        {
            if (op._placePhase.Active)
                return op._placePhase;
            if (op._sweepPhase.Active)
                return op._sweepPhase;
            return null;
        }

        public override int CountRemaining() => PastingOperation.CountQueueTasks(Operation.cutTasks);

        public override void OnEnter() => Operation.BeginInfoPhase(PasteInfoPhase.Cut);

        public override void ExecuteTick()
        {
            Operation.AdvanceToNextNonEmptyLayer(Operation.cutTasks);
            if (Operation._currentLayerIndex >= Operation.cutTasks.Count)
            {
                Operation.AdvancePhase();
                return;
            }

            Operation.SyncOverlayDoneTrackingIfNeeded();
            var qCut = Operation.cutTasks[Operation._currentLayerIndex];
            var layerCut = Operation.Pipeline.ClearOrder[Operation._currentLayerIndex];
            TABLog.Debug($"Cut layer {layerCut.Id} (idx={Operation._currentLayerIndex}) tasks={qCut.Count}", "Paste");
            Operation._currentStepClearTiles.Clear();
            Operation._currentStepPlaceTiles.Clear();
            ProcessClearQueue(qCut, Operation.Context, TickSize, Operation._currentStepClearTiles, Operation._overlayDoneThisLayer);
        }
    }
}
