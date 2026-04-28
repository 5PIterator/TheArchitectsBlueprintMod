using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ArchitectsStencilMod.ModUtils;
using ArchitectsStencilMod.UI;

namespace ArchitectsStencilMod.UIKit;

/// <summary>World pin handle drawn in screen space (game interface scale).</summary>
internal sealed class UIPoint : UIView
{
    public const int BaseSizePx = 12;
    public const int HoverSizePx = 16;

    private Point _tile;
    private Color _color;

    public UIPoint(Point tile, Color color)
    {
        _tile = tile;
        _color = color;
        UpdateWhenOutOfBounds = true;
        OverridesMouse = false;
    }

    public void SetTile(Point tile) => _tile = tile;

    public void SetColor(Color color) => _color = color;

    public override void Update()
    {
        Point mouse = new(Main.mouseX, Main.mouseY);
        bool hovered = WorldOverlayMath.GetPointHandleRect(_tile, BaseSizePx).Contains(mouse);
        int sizePx = hovered ? HoverSizePx : BaseSizePx;
        Rectangle r = WorldOverlayMath.GetPointHandleRect(_tile, sizePx);
        Position = new Vector2(r.X, r.Y);
        Width = r.Width;
        Height = r.Height;
        base.Update();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        Texture2D pixel = ModUtils.ModUtils.DummyTexture;
        var r = new Rectangle((int)DrawPosition.X, (int)DrawPosition.Y, (int)Width, (int)Height);
        spriteBatch.Draw(pixel, r, new Rectangle(0, 0, 1, 1), _color * 0.9f);
        base.Draw(spriteBatch);
    }
}
