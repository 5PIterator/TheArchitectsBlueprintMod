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
    /// <see cref="KillTile"/> also passes <c>noItem</c>. Wire/actuator kills clear tile flags directly because
    /// <see cref="WorldGen.KillWire"/> and <see cref="WorldGen.KillActuator"/> spawn items via <see cref="Item.NewItem"/>.
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
            // Mute Dig while also suppressing item drops for sandbox/discovery/paste clears.
            SuppressTileSound.Run(() =>
                RunSuppressed(() => WorldGen.KillTile(i, j, fail: false, effectOnly: false, noItem: true)));
        }

        public static void KillWall(int i, int j) =>
            SuppressTileSound.Run(() =>
                RunSuppressed(() => WorldGen.KillWall(i, j, fail: false)));

        public static void EmptyLiquid(int x, int y) =>
            RunSuppressed(() => WorldGen.EmptyLiquid(x, y));

        public static void KillWire(int i, int j) =>
            RunSuppressed(() => ClearWireFlag(i, j, state => state.RedWire, (state, value) => state with { RedWire = value }));

        public static void KillWire2(int i, int j) =>
            RunSuppressed(() => ClearWireFlag(i, j, state => state.BlueWire, (state, value) => state with { BlueWire = value }));

        public static void KillWire3(int i, int j) =>
            RunSuppressed(() => ClearWireFlag(i, j, state => state.GreenWire, (state, value) => state with { GreenWire = value }));

        public static void KillWire4(int i, int j) =>
            RunSuppressed(() => ClearWireFlag(i, j, state => state.YellowWire, (state, value) => state with { YellowWire = value }));

        public static void KillActuator(int i, int j) =>
            RunSuppressed(() => ClearWireFlag(i, j, state => state.HasActuator, (state, value) => state with { HasActuator = value }));

        private static void ClearWireFlag(
            int i,
            int j,
            Func<TileWallWireStateData, bool> isSet,
            Func<TileWallWireStateData, bool, TileWallWireStateData> withValue)
        {
            TileWallWireStateData state = Main.tile[i, j].Get<TileWallWireStateData>();
            if (!isSet(state))
                return;

            Main.tile[i, j].Get<TileWallWireStateData>() = withValue(state, false);
        }
    }
}
