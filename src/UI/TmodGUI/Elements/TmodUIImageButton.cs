using System;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Clickable button whose frame is drawn from base/hover textures (see <see cref="TmodFrameStyle.Image"/>).</summary>
    public sealed class TmodUIImageButton : TmodButtonChrome
    {
        public TmodUIImageButton(
            Asset<Texture2D> baseTexture,
            Asset<Texture2D> hoverTexture,
            string? hoverText = null,
            Action? onClick = null,
            float width = TmodTheme.ButtonWidth,
            float height = TmodTheme.ButtonHeight)
            : base(width, height, TmodFrameStyle.Image(baseTexture, hoverTexture), hoverText, onClick)
        {
        }

        public TmodUIImageButton(Asset<Texture2D> image, string? hoverText = null, Action? onClick = null)
            : this(image, image, hoverText, onClick)
        {
        }

        public TmodUIImageButton(
            float width,
            float height,
            TmodFrameStyle style,
            string? hoverText = null,
            Action? onClick = null)
            : base(width, height, style, hoverText, onClick)
        {
        }

        public void SetText(string text) => HoverText = string.IsNullOrEmpty(text) ? null : text;

        public void SetIcon(string basePath, string? hoverPath = null)
        {
            SetBaseImage(TmodUIAssets.RequestTexture(basePath));
            SetHoverImage(TmodUIAssets.RequestTexture(hoverPath ?? TmodUIAssets.Bestiary.ButtonWideBorderPath));
        }
    }
}
