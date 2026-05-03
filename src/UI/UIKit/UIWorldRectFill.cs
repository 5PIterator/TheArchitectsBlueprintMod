using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ArchitectsStencilMod.UI;

namespace ArchitectsStencilMod.UIKit;

/// <summary>Filled world rectangle in screen space (one quad, e.g. projection body tint).</summary>
internal sealed class UIWorldRectFill : UIView
{
    private Rectangle _tileBounds;
    private Color _fill;

    public UIWorldRectFill(Rectangle tileBounds, Color fill)
    {
        _tileBounds = tileBounds;
        _fill = fill;
        UpdateWhenOutOfBounds = true;
        OverridesMouse = false;
    }

    public void SetTileBounds(Rectangle tileBounds) => _tileBounds = tileBounds;

    public void SetFill(Color fill) => _fill = fill;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || _tileBounds.IsEmpty)
            return;

        var rect = new UIRect(
            new Vector2(_tileBounds.X, _tileBounds.Y) * WorldOverlayMath.TilePixelSize - Main.screenPosition,
            _tileBounds.Width * WorldOverlayMath.TilePixelSize,
            _tileBounds.Height * WorldOverlayMath.TilePixelSize)
        {
            ForegroundColor = _fill
        };
        rect.Draw(spriteBatch);
        base.Draw(spriteBatch);
    }
}
