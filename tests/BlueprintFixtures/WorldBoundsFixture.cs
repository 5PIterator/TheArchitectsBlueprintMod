#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Tests.Fixtures
{
    /// <summary>Shared world-bound checks for blueprint test setup.</summary>
    internal static class WorldBoundsFixture
    {
        internal static bool IsRectangleInWorld(Rectangle tileRectWorld)
        {
            if (tileRectWorld.Width <= 0 || tileRectWorld.Height <= 0)
                return false;
            return WorldGen.InWorld(tileRectWorld.X, tileRectWorld.Y, 0)
                && WorldGen.InWorld(tileRectWorld.Right - 1, tileRectWorld.Bottom - 1, 0);
        }

        internal static bool TryEnsureRectangleInWorld(Rectangle tileRectWorld, out string? error)
        {
            error = null;
            if (tileRectWorld.Width <= 0 || tileRectWorld.Height <= 0)
            {
                error = $"Invalid bounds: {tileRectWorld.Width}x{tileRectWorld.Height}.";
                return false;
            }

            if (!IsRectangleInWorld(tileRectWorld))
            {
                error = $"Blueprint bounds ({tileRectWorld.X},{tileRectWorld.Y},{tileRectWorld.Width}x{tileRectWorld.Height}) are outside the world.";
                return false;
            }

            return true;
        }
    }
}
#endif
