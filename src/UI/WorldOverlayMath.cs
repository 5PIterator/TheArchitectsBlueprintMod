using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.UI;

/// <summary>Shared tile-to-screen math for world overlays (game interface scale).</summary>
public static class WorldOverlayMath
{
    public const int TilePixelSize = 16;

    public static Rectangle GetPointHandleRect(Point tile, int sizePx)
    {
        int centerX = tile.X * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.X;
        int centerY = tile.Y * TilePixelSize + TilePixelSize / 2 - (int)Main.screenPosition.Y;
        return new Rectangle(centerX - sizePx / 2, centerY - sizePx / 2, sizePx, sizePx);
    }
}
