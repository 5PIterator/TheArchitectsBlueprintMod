using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Payload detail thumbnail with Bestiary portrait frame chrome.</summary>
    public sealed class TmodUIPayloadPreview : UIElement
    {
        private readonly UIImage _portraitFrame;
        private readonly UIImage _thumbnail;

        public TmodUIPayloadPreview(float size = Theme.BrowserPayloadThumbnailSize)
        {
            Width.Set(size, 0f);
            Height.Set(size, 0f);
            IgnoresMouseInteraction = true;

            _portraitFrame = new UIImage(TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.PortraitFrontPath))
            {
                IgnoresMouseInteraction = true,
                ScaleToFit = true,
                Color = Color.White * 0.9f
            };
            _portraitFrame.Width.Set(0f, 1f);
            _portraitFrame.Height.Set(0f, 1f);
            Append(_portraitFrame);

            _thumbnail = new UIImage(TextureAssets.MagicPixel)
            {
                IgnoresMouseInteraction = true,
                ScaleToFit = true,
                Color = new Color(30, 35, 50, 220),
                HAlign = 0.5f,
                VAlign = 0.5f
            };
            _thumbnail.Width.Set(-8f, 1f);
            _thumbnail.Height.Set(-8f, 1f);
            Append(_thumbnail);
        }

        public void SetThumbnail(Texture2D? texture)
        {
            if (texture != null)
            {
                _thumbnail.SetImage(texture);
                _thumbnail.Color = Color.White;
                _portraitFrame.Color = Color.Transparent;
                return;
            }

            _thumbnail.SetImage(TextureAssets.MagicPixel);
            _thumbnail.Color = new Color(30, 35, 50, 220);
            _portraitFrame.Color = Color.White * 0.9f;
        }
    }
}
