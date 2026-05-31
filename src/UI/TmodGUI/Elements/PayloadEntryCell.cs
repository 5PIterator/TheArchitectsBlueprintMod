using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Grid slot for a payload file with Bestiary frame chrome.</summary>
    public sealed class PayloadEntryCell : TmodFramedControl
    {
        private readonly UIImage _portraitFrame;
        private readonly UIImage _image;

        public PayloadEntryCell(Func<string> getHoverText, Action? onClick = null)
            : base(
                TmodTheme.BrowserCompactCellSize,
                TmodTheme.BrowserCompactCellSize,
                TmodFrameStyle.EntrySlot(),
                getHoverText: getHoverText,
                onClick: onClick)
        {
            _portraitFrame = new UIImage(TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.PortraitFrontPath))
            {
                IgnoresMouseInteraction = true,
                ScaleToFit = true,
                Color = Color.White * 0.85f,
                HAlign = 0.5f,
                VAlign = 0.5f
            };
            _portraitFrame.Width.Set(-6f, 1f);
            _portraitFrame.Height.Set(-6f, 1f);
            ContentHost.Append(_portraitFrame);

            _image = new UIImage(TextureAssets.MagicPixel)
            {
                IgnoresMouseInteraction = true,
                ScaleToFit = true,
                Color = Color.White,
                HAlign = 0.5f,
                VAlign = 0.5f
            };
            _image.Width.Set(-10f, 1f);
            _image.Height.Set(-10f, 1f);
            _image.SetImage(TmodUIAssets.Bestiary.DefaultEntryIcon);
            ContentHost.Append(_image);
        }

        public void SetTexture(Texture2D? texture)
        {
            if (texture != null)
            {
                _image.SetImage(texture);
                _image.Color = Color.White;
                _portraitFrame.Color = Color.Transparent;
                return;
            }

            _image.SetImage(TmodUIAssets.Bestiary.DefaultEntryIcon);
            _image.Color = Color.White;
            _portraitFrame.Color = Color.White * 0.85f;
        }
    }
}
