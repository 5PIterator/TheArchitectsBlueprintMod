using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ArchitectsStencilMod.ModUtils;
using ArchitectsStencilMod.UI;

namespace ArchitectsStencilMod.UIKit;

/// <summary>Many tile cells filled as quads in screen space.</summary>
internal sealed class UITileFill : UIView
{
    private Point[] _tiles = [];
    private Color _fill;

    public UITileFill(Point[] tiles, Color fill)
    {
        _tiles = tiles ?? [];
        _fill = fill;
        UpdateWhenOutOfBounds = true;
        OverridesMouse = false;
    }

    public void SetTiles(Point[] tiles) => _tiles = tiles ?? [];

    public void SetFill(Color fill) => _fill = fill;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || _tiles.Length == 0)
            return;

        Texture2D pixel = ModUtils.ModUtils.DummyTexture;
        var src = new Rectangle(0, 0, 1, 1);
        var scale = new Vector2(WorldOverlayMath.TilePixelSize, WorldOverlayMath.TilePixelSize);
        for (int i = 0; i < _tiles.Length; i++)
        {
            var t = _tiles[i];
            var pos = new Vector2(t.X * WorldOverlayMath.TilePixelSize, t.Y * WorldOverlayMath.TilePixelSize) - Main.screenPosition;
            spriteBatch.Draw(pixel, pos, src, _fill, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        base.Draw(spriteBatch);
    }
}
