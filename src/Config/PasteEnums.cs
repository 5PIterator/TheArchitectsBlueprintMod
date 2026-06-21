namespace TheArchitectsBlueprintMod.Config
{
    public enum ClearPhaseStrategy
    {
        All = 0,
        Transparent = 1,
        Negative = 2,
    }

    public enum PlacePhaseStrategy
    {
        Place = 0,
        Replace = 1,
    }

    public enum SweepPhaseStrategy
    {
        Detail = 0,
        Cheat = 1,
    }

    /// <summary>Per-phase execution tuning: enable flag, work units per tick, and delay before the next tick.</summary>
    public sealed class PastePhaseSettings
    {
        [System.ComponentModel.DefaultValue(true)]
        public bool Active = true;

        [System.ComponentModel.DefaultValue(100)]
        [Terraria.ModLoader.Config.Range(1, 512)]
        public int TickSize = 100;

        [System.ComponentModel.DefaultValue(100)]
        [Terraria.ModLoader.Config.Range(0, 5000)]
        public int TickDelayMs = 100;
    }

}
