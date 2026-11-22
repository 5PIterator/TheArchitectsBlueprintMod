namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>
    /// Log severity aligned with tModLoader <c>Mod.Logger</c> sinks, plus <see cref="Off"/> to disable a floor.
    /// </summary>
    public enum TabLogLevel
    {
        /// <summary>Disable this floor (no emission / no chat echo when used as a minimum).</summary>
        Off,
        Debug,
        Info,
        Warn,
        Error,
    }
}
