using System;
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
    /// Honors <see cref="SuppressTileDrop"/> while wall kills run.
    /// </summary>
    public sealed class SuppressTileDropGlobalWall : GlobalWall
    {
        public override bool Drop(int i, int j, int type, ref int dropType)
        {
            if (SuppressTileDrop.IsActive)
                return false;
            return base.Drop(i, j, type, ref dropType);
        }
    }

    /// <summary>
    /// Wraps destructive <see cref="WorldGen"/> calls with drop suppression via <see cref="SuppressTileDropGlobalTile"/>
    /// and <see cref="SuppressTileDropGlobalWall"/>.
    /// <see cref="KillTile"/> also passes <c>noItem</c>; wire/actuator kills rely on <see cref="GlobalTile.CanDrop"/>.
    /// </summary>
    public static class SuppressTileDrop
    {
        private static int _depth;

        internal static bool IsActive => _depth > 0;

        internal static void RunSuppressed(Action action)
        {
            _depth++;
            try
            {
                action();
            }
            finally
            {
                _depth--;
            }
        }

        public static void KillTile(int i, int j)
        {
            RunSuppressed(() => WorldGen.KillTile(i, j, fail: false, effectOnly: false, noItem: true));
        }

        public static void KillWall(int i, int j) =>
            RunSuppressed(() => WorldGen.KillWall(i, j, fail: false));

        public static void EmptyLiquid(int x, int y) =>
            RunSuppressed(() => WorldGen.EmptyLiquid(x, y));

        public static void KillWire(int i, int j) =>
            RunSuppressed(() => WorldGen.KillWire(i, j));

        public static void KillWire2(int i, int j) =>
            RunSuppressed(() => WorldGen.KillWire2(i, j));

        public static void KillWire3(int i, int j) =>
            RunSuppressed(() => WorldGen.KillWire3(i, j));

        public static void KillWire4(int i, int j) =>
            RunSuppressed(() => WorldGen.KillWire4(i, j));

        public static void KillActuator(int i, int j) =>
            RunSuppressed(() => WorldGen.KillActuator(i, j));
    }
}
