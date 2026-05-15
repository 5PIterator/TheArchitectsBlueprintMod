using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Utils
{
    /// <summary>
    /// Honors <see cref="SuppressTileDrop"/> while tile kills run (including nested framing/drop work on the same stack).
    /// </summary>
    public sealed class SuppressTileDropGlobalTile : GlobalTile
    {
        public override bool CanDrop(int i, int j, int type)
        {
            if (SuppressTileDrop.IsActive)
                return false;
            return base.CanDrop(i, j, type);
        }
    }

    /// <summary>
    /// Wraps <see cref="WorldGen.KillTile"/> with <c>noItem</c> and coordinates <see cref="SuppressTileDropGlobalTile"/>
    /// so multi-tile structures do not spawn items. Suppression is raised before the call because
    /// <see cref="Terraria.ModLoader.GlobalTile.CanDrop"/> can run before <see cref="Terraria.ModLoader.GlobalTile.KillTile"/>.
    /// </summary>
    public static class SuppressTileDrop
    {
        private static int _depth;

        internal static bool IsActive => _depth > 0;

        public static void KillTile(int i, int j)
        {
            _depth++;
            try
            {
                WorldGen.KillTile(i, j, fail: false, effectOnly: false, noItem: true);
            }
            finally
            {
                _depth--;
            }
        }
    }
}
