#if DEBUG
#nullable enable

using System;
using TheArchitectsBlueprintMod.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
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

        /// <summary>Tile rectangle centered on <see cref="Main.MouseWorld"/> so tests avoid the player body.</summary>
        internal static Rectangle TileRectAtCursor(int width, int height)
        {
            Point center = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
            return new Rectangle(center.X - width / 2, center.Y - height / 2, width, height);
        }
    }
}
#endif
