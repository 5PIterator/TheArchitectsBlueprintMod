using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core.Phases
{
    public enum ClearStyle
    {
        All = 0,
        Transparent = 1,
        Negative = 2,
    }

    public readonly struct PlanPhaseStrategy
    {
        public ClearStyle Style { get; init; }
    }

    /// <summary>Incrementally builds clear/cut/place task queues.</summary>
    internal sealed class PlanPhase : Phase<PlanPhaseStrategy>
    {
        public PlanPhase(PastingOperation operation, PastePhasePolicy<PlanPhaseStrategy> policy) : base(operation, policy) { }

        public override string StageName => "PLAN";

        public override PasteInfoPhase? InfoPhase => null;

        public override Phase? Next(PastingOperation op) => op.ResolveFirstEnabledExecutionPhase();

        public override int CountRemaining() => 0;

        public override void ExecuteTick()
        {
            bool morePrep = Operation.Init();
            if (!morePrep)
            {
                Operation._currentPhase = Operation.ResolveFirstEnabledExecutionPhase();
                Operation._currentLayerIndex = 0;
                Operation._currentPhase?.OnEnter();
                TABLog.Debug($"Stage: EXEC {Operation._currentPhase?.StageName ?? "DONE"}", "Paste");
            }
        }
    }
}
