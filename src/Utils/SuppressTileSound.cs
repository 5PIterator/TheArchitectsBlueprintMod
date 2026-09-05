using System;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Utils
{
    /// <summary>
    /// Honors <see cref="SuppressTileSound"/> while tile kills run so paste can own the Dig cue.
    /// </summary>
    public sealed class SuppressTileSoundGlobalTile : GlobalTile
    {
        public override bool KillSound(int i, int j, int type, bool fail)
        {
            if (SuppressTileSound.IsActive)
                return false;
            return base.KillSound(i, j, type, fail);
        }
    }

    /// <summary>
    /// Honors <see cref="SuppressTileSound"/> while wall kills run so paste can own the Dig cue.
    /// </summary>
    public sealed class SuppressTileSoundGlobalWall : GlobalWall
    {
        public override bool KillSound(int i, int j, int type, bool fail)
        {
            if (SuppressTileSound.IsActive)
                return false;
            return base.KillSound(i, j, type, fail);
        }
    }

    /// <summary>
    /// Wraps destructive <see cref="Terraria.WorldGen"/> kills so vanilla Dig audio is skipped.
    /// </summary>
    public static class SuppressTileSound
    {
        private static int _depth;

        internal static bool IsActive => _depth > 0;

        /// <summary>Runs <paramref name="action"/> with kill sounds suppressed for the nested stack.</summary>
        public static void Run(Action action)
        {
            // --- Bump depth around the action so GlobalTile/Wall KillSound returns false.

            _depth++;
            try
            {
                // Execute the muted world mutation.
                action();
            }
            finally
            {
                _depth--;
            }
        }
    }
}
