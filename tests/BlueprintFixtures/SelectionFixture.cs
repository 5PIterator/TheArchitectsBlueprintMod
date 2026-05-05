#if DEBUG
#nullable enable

using System;
using ArchitectsStencilMod.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Tests.Fixtures
{
    /// <summary>Creates <see cref="Selection"/> regions for tests (validated against <see cref="WorldBoundsFixture"/>).</summary>
    internal static class SelectionFixture
    {
        internal static bool TryCreate(Player player, Rectangle worldTileBounds, out Selection selection, out string? error)
        {
            selection = null!;
            ArgumentNullException.ThrowIfNull(player);
            if (!WorldBoundsFixture.TryEnsureRectangleInWorld(worldTileBounds, out error))
                return false;

            var sel = new Selection(player.whoAmI);
            sel.Define(worldTileBounds);
            selection = sel;
            return true;
        }

        internal static Rectangle TileRectFromPlayerCenter(Player player, int width, int height, int offsetYTiles = 0)
        {
            Point o = Terraria.Utils.ToTileCoordinates(player.Center);
            if (offsetYTiles != 0)
                o.Y -= offsetYTiles;
            return new Rectangle(o.X, o.Y, width, height);
        }
    }
}
#endif
