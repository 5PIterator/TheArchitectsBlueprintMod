using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Grid slot for a payload file with Bestiary frame chrome and status badges.</summary>
    public sealed class PayloadEntryCell : TmodFramedControl
    {
        private static readonly Color InvalidTint = new(220, 80, 80, 220);
        private static readonly Color LegacyTint = new(220, 180, 60, 220);
        private static readonly Color MissingModsTint = new(220, 120, 40, 220);

        private readonly UIImage _portraitFrame;
        private readonly UIImage _image;
        private readonly UIText _badge;

        public PayloadEntryCell(Func<string> getHoverText, Action? onClick = null)
            : base(
                Theme.BrowserCompactCellSize,
                Theme.BrowserCompactCellSize,
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

            _badge = new UIText(string.Empty, 0.55f)
            {
                TextColor = Color.White,
                HAlign = 1f,
                VAlign = 0f,
                Top = new StyleDimension(2f, 0f),
                Left = new StyleDimension(-2f, 0f),
                TextOriginX = 1f,
                TextOriginY = 0f,
                IgnoresMouseInteraction = true
            };
            ContentHost.Append(_badge);
        }

        /// <summary>Applies the decoded thumbnail texture, or restores the default icon.</summary>
        public void SetTexture(Texture2D? texture)
        {
            // --- Swap image asset and hide portrait chrome when a real thumbnail exists.
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

        /// <summary>Shows a compact status badge for invalid, legacy, or missing-mod entries.</summary>
        public void SetStatus(bool isValid, bool isLegacy, bool hasMissingMods)
        {
            // --- Prefer the most severe status; clear badge when none apply.
            if (!isValid)
            {
                _badge.SetText("!");
                _badge.TextColor = InvalidTint;
                return;
            }

            if (hasMissingMods)
            {
                _badge.SetText("M");
                _badge.TextColor = MissingModsTint;
                return;
            }

            if (isLegacy)
            {
                _badge.SetText("L");
                _badge.TextColor = LegacyTint;
                return;
            }

            _badge.SetText(string.Empty);
        }
    }
}
