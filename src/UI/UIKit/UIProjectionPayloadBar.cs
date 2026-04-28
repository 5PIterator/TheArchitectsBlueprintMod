using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.ModUtils;
using ArchitectsStencilMod.UI;

namespace ArchitectsStencilMod.UIKit;

/// <summary>Import/export payload controls for the front-most local projection (game-scaled overlay).</summary>
internal sealed class UIProjectionPayloadBar : UIView
{
    private const int ButtonWidth = 58;
    private const int ButtonHeight = 24;
    private const int ButtonGap = 6;
    private const int ButtonOffset = 8;
    private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

    private bool _previousLeftDown;

    public UIProjectionPayloadBar()
    {
        OverridesMouse = false;
        UpdateWhenOutOfBounds = true;
    }

    public override void Update()
    {
        bool leftClicked = Main.mouseLeft && !_previousLeftDown;
        _previousLeftDown = Main.mouseLeft;

        Player? pl = Main.LocalPlayer;
        Visible = false;
        Projection? proj = null;
        if (pl != null
            && pl.whoAmI == Main.myPlayer
            && !Main.gameMenu
            && !PayloadFileDialogs.IsActive
            && ProjectionSystem.TryGetFrontmostProjection(pl, out Projection p)
            && !p.IsLocked)
        {
            proj = p;
            Visible = true;
        }

        if (!Visible || proj is null)
        {
            base.Update();
            return;
        }

        Point origin = GetButtonGroupOrigin(proj);
        X = origin.X;
        Y = origin.Y;
        Width = ButtonWidth * 2 + ButtonGap;
        Height = ButtonHeight;

        if (leftClicked)
        {
            Point mouse = new(Main.mouseX, Main.mouseY);
            Rectangle import = new(origin.X, origin.Y, ButtonWidth, ButtonHeight);
            Rectangle export = new(origin.X + ButtonWidth + ButtonGap, origin.Y, ButtonWidth, ButtonHeight);
            if (import.Contains(mouse))
                HandleImport();
            else if (export.Contains(mouse))
                HandleExport();
        }

        base.Update();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        Player? pl = Main.LocalPlayer;
        if (pl is null || pl.whoAmI != Main.myPlayer)
            return;

        if (!ProjectionSystem.TryGetFrontmostProjection(pl, out Projection projection) || projection.IsLocked)
            return;

        Point origin = GetButtonGroupOrigin(projection);
        DrawButton(spriteBatch, new Rectangle(origin.X, origin.Y, ButtonWidth, ButtonHeight), "Import");
        DrawButton(spriteBatch, new Rectangle(origin.X + ButtonWidth + ButtonGap, origin.Y, ButtonWidth, ButtonHeight), "Export");
    }

    private static void HandleImport()
    {
        Player? pl = Main.LocalPlayer;
        if (pl is null || pl.whoAmI != Main.myPlayer)
            return;
        if (!ProjectionSystem.TryGetFrontmostProjection(pl, out Projection projection) || projection.IsLocked)
            return;
        pl.mouseInterface = true;
        PayloadSerialization.BeginImport(projection);
    }

    private static void HandleExport()
    {
        Player? pl = Main.LocalPlayer;
        if (pl is null || pl.whoAmI != Main.myPlayer)
            return;
        if (!ProjectionSystem.TryGetFrontmostProjection(pl, out Projection projection) || projection.IsLocked)
            return;
        pl.mouseInterface = true;
        PayloadSerialization.BeginExport(projection);
    }

    private static void DrawButton(SpriteBatch spriteBatch, Rectangle bounds, string label)
    {
        Point mouse = new(Main.mouseX, Main.mouseY);
        bool hovered = bounds.Contains(mouse);

        Texture2D pixel = ModUtils.ModUtils.DummyTexture;
        Color fill = hovered ? new Color(90, 86, 126, 245) : new Color(44, 42, 64, 235);
        Color border = hovered ? Color.White : new Color(155, 150, 190, 255);
        spriteBatch.Draw(pixel, bounds, PixelSource, fill);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), PixelSource, border);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), PixelSource, border);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), PixelSource, border);
        spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), PixelSource, border);

        DynamicSpriteFont font = FontAssets.MouseText.Value;
        Vector2 size = font.MeasureString(label) * 0.72f;
        Vector2 pos = new(bounds.Center.X - size.X * 0.5f, bounds.Center.Y - size.Y * 0.5f);
        spriteBatch.DrawString(font, label, pos, Color.White, 0f, Vector2.Zero, 0.72f, SpriteEffects.None, 0f);
    }

    private static Point GetButtonGroupOrigin(Projection projection)
    {
        int groupWidth = ButtonWidth * 2 + ButtonGap;
        int x = (int)(projection.Area.Right * WorldOverlayMath.TilePixelSize - Main.screenPosition.X) + ButtonOffset;
        int y = (int)(projection.Area.Y * WorldOverlayMath.TilePixelSize - Main.screenPosition.Y);

        x = Math.Clamp(x, ButtonOffset, Math.Max(ButtonOffset, Main.screenWidth - groupWidth - ButtonOffset));
        y = Math.Clamp(y, ButtonOffset, Math.Max(ButtonOffset, Main.screenHeight - ButtonHeight - ButtonOffset));
        return new Point(x, y);
    }

    private static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();
}
