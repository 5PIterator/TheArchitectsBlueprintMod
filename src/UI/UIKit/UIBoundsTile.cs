using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ArchitectsStencilMod.ModUtils;

namespace ArchitectsStencilMod.UIKit;

/// <summary>Tile rectangle with filled interior and border (legacy DrawTileBounds).</summary>
internal sealed class UIBoundsTile : UIView
{
    private Rectangle _tileBounds;
    private Color _color;
    private int _borderWidth;

    public UIBoundsTile(Rectangle tileBounds, Color color, int borderWidth)
    {
        _tileBounds = tileBounds;
        _color = color;
        _borderWidth = borderWidth;
        UpdateWhenOutOfBounds = true;
        OverridesMouse = false;
    }

    public void SetTileBounds(Rectangle tileBounds) => _tileBounds = tileBounds;

    public void SetColor(Color color) => _color = color;

    public void SetBorderWidth(int borderWidth) => _borderWidth = borderWidth;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || _tileBounds.IsEmpty)
            return;

        var pos = new Vector2(_tileBounds.X, _tileBounds.Y);
        var size = new Vector2(_tileBounds.Width, _tileBounds.Height);
        ModUtils.ModUtils.DrawBorderedRect(spriteBatch, _color, pos, size, _borderWidth);
        base.Draw(spriteBatch);
    }
}
