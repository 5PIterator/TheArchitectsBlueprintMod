using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ArchitectsStencilMod.ModUtils;

namespace ArchitectsStencilMod.UIKit;

/// <summary>
/// Shared screen-space drawing helpers for blueprint / world-overlay UIKit views.
/// </summary>
internal static class UIBlueprint
{
    private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

    public static void DrawScreenRectangle(SpriteBatch spriteBatch, Rectangle bounds, Color fill, Color border)
    {
        Texture2D pixel = ModUtils.ModUtils.DummyTexture;
        spriteBatch.Draw(pixel, bounds, PixelSource, fill);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), PixelSource, border);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), PixelSource, border);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), PixelSource, border);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), PixelSource, border);
    }
}
