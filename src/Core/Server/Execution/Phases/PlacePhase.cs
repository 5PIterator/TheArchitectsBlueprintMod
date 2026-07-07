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
            ProcessPlaceQueue(
                qPlace,
                Operation.Context,
                TickSize,
                Operation._currentStepPlaceTiles,
                Operation._overlayDoneThisLayer,
                successfulPlacedWorldTiles: null);
        }
    }
}
