using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    public enum TmodFrameHoverMode
    {
        AlphaOverlay,
        LayerReorder
    }

    /// <summary>Configurable base/hover frame layers for a themed control.</summary>
    public sealed class TmodFrameStyle
    {
        public TmodFrameHoverMode HoverMode { get; init; } = TmodFrameHoverMode.AlphaOverlay;
        public Asset<Texture2D>? BaseTexture { get; init; }
        public string? BasePath { get; init; }
        public Asset<Texture2D>? HoverTexture { get; init; }
        public string? HoverPath { get; init; }
        public string? SecondaryPath { get; init; }
        public string? SlotBackPath { get; init; }
        public string? FrontPath { get; init; }
        public string? GlowPath { get; init; }
        public Color OverlayColor { get; init; } = Theme.BestiarySlotOverlay;
        public float ActiveAlpha { get; init; } = 1f;
        public float InactiveAlpha { get; init; } = 0.72f;

        public static TmodFrameStyle ButtonBorder(bool wide = false, string? secondaryPath = null) =>
            new()
            {
                HoverMode = TmodFrameHoverMode.AlphaOverlay,
                BasePath = wide
                    ? TmodUIAssets.Bestiary.ButtonWideBorderPath
                    : TmodUIAssets.Bestiary.ButtonBorderPath,
                HoverPath = TmodUIAssets.Bestiary.SlotOverlayPath,
                SecondaryPath = secondaryPath,
                ActiveAlpha = 1f,
                InactiveAlpha = 0.72f
            };

        public static TmodFrameStyle EntrySlot() =>
            new()
            {
                HoverMode = TmodFrameHoverMode.LayerReorder,
                SlotBackPath = TmodUIAssets.Bestiary.SlotBackPath,
                HoverPath = TmodUIAssets.Bestiary.SlotOverlayPath,
                FrontPath = TmodUIAssets.Bestiary.SlotFrontPath,
                GlowPath = TmodUIAssets.Bestiary.SlotSelectionPath
            };

        public static TmodFrameStyle Image(
            Asset<Texture2D> baseTexture,
            Asset<Texture2D> hoverTexture,
            float activeAlpha = 1f,
            float inactiveAlpha = 0.4f) =>
            new()
            {
                HoverMode = TmodFrameHoverMode.AlphaOverlay,
                BaseTexture = baseTexture,
                HoverTexture = hoverTexture,
                ActiveAlpha = activeAlpha,
                InactiveAlpha = inactiveAlpha
            };

        /// <summary>Single icon with optional wide-border hover highlight.</summary>
        public static TmodFrameStyle Icon(
            string basePath,
            string? hoverPath = null,
            float inactiveAlpha = 0.72f) =>
            Image(
                TmodUIAssets.RequestTexture(basePath),
                TmodUIAssets.RequestTexture(hoverPath ?? TmodUIAssets.Bestiary.ButtonWideBorderPath),
                inactiveAlpha: inactiveAlpha);

        public Asset<Texture2D> ResolveBase()
        {
            if (BaseTexture != null)
                return BaseTexture;

            if (!string.IsNullOrEmpty(BasePath))
                return TmodUIAssets.RequestTexture(BasePath);

            throw new InvalidOperationException("Frame style has no base texture configured.");
        }

        public Asset<Texture2D> ResolveHover()
        {
            if (HoverTexture != null)
                return HoverTexture;

            if (!string.IsNullOrEmpty(HoverPath))
                return TmodUIAssets.RequestTexture(HoverPath);

            throw new InvalidOperationException("Frame style has no hover texture configured.");
        }
    }

    /// <summary>Hover tooltip, click/hover sounds, content host, and swappable frame layers.</summary>
    public class TmodFramedControl : TmodHoverTextChrome
    {
        private readonly Action? _onClick;
        private readonly TmodFrameStyle _style;
        private readonly UIElement _contentHost;
        private AlphaOverlayLayer? _alphaFrame;
        private UIImage? _overlay;
        private UIImage? _frameFront;
        private UIImage? _frameGlow;
        private bool _hovered;
        private bool _hoverSoundPlayed;

        public UIElement ContentHost => _contentHost;

        protected TmodFramedControl(
            float width,
            float height,
            TmodFrameStyle style,
            string? hoverText = null,
            Func<string>? getHoverText = null,
            Action? onClick = null)
            : base(hoverText, getHoverText)
        {
            _onClick = onClick;
            _style = style;
            SetPadding(0f);

            if (width > 0f)
                Width.Set(width, 0f);
            if (height > 0f)
                Height.Set(height, 0f);

            _contentHost = new UIElement
            {
                HAlign = 0.5f,
                VAlign = 0.5f,
                IgnoresMouseInteraction = true,
                OverflowHidden = true
            };
            _contentHost.Width.Set(-4f, 1f);
            _contentHost.Height.Set(-4f, 1f);
            _contentHost.SetPadding(0f);

            if (style.HoverMode == TmodFrameHoverMode.LayerReorder)
            {
                if (!string.IsNullOrEmpty(style.SlotBackPath))
                    _contentHost.Append(CreateFrameImage(style.SlotBackPath));

                Append(_contentHost);
                BuildLayerReorderFrame(style);
            }
            else
            {
                BuildAlphaOverlayFrame(style);
                Append(_contentHost);
            }

            if (_onClick != null)
                OnLeftClick += OnFrameClick;

            OnMouseOver += OnFrameMouseOver;
            OnMouseOut += OnFrameMouseOut;
        }

        private void BuildAlphaOverlayFrame(TmodFrameStyle style)
        {
            // Frame layers draw above ContentHost; append order matches legacy TmodButtonChrome.
            _alphaFrame = new AlphaOverlayLayer(style.ResolveBase(), style.ResolveHover(), () => IsMouseHovering)
            {
                ActiveAlpha = style.ActiveAlpha,
                InactiveAlpha = style.InactiveAlpha
            };
            _alphaFrame.Width.Set(0f, 1f);
            _alphaFrame.Height.Set(0f, 1f);
            Append(_alphaFrame);

            if (!string.IsNullOrEmpty(style.SecondaryPath))
            {
                var secondary = new UIImage(TmodUIAssets.RequestTexture(style.SecondaryPath))
                {
                    IgnoresMouseInteraction = true,
                    ScaleToFit = true
                };
                secondary.Width.Set(0f, 1f);
                secondary.Height.Set(0f, 1f);
                Append(secondary);
            }
        }

        private void BuildLayerReorderFrame(TmodFrameStyle style)
        {
            _overlay = CreateFrameImage(style.HoverPath!, fill: true);
            _overlay.Color = style.OverlayColor;
            Append(_overlay);

            _frameFront = CreateFrameImage(style.FrontPath!, fill: true);
            Append(_frameFront);

            _frameGlow = CreateFrameImage(style.GlowPath!, fill: true);
            _frameGlow.Color = Color.Transparent;
            Append(_frameGlow);
        }

        private static UIImage CreateFrameImage(string texturePath, bool fill = false)
        {
            Asset<Texture2D> asset = TmodUIAssets.RequestTexture(texturePath);
            var image = new UIImage(asset)
            {
                IgnoresMouseInteraction = true
            };

            if (fill)
            {
                image.ScaleToFit = true;
                image.Width.Set(0f, 1f);
                image.Height.Set(0f, 1f);
                return image;
            }

            image.HAlign = 0.5f;
            image.VAlign = 0.5f;
            return image;
        }

        private void OnFrameClick(UIMouseEvent evt, UIElement listeningElement)
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            _onClick?.Invoke();
        }

        private void OnFrameMouseOver(UIMouseEvent evt, UIElement listeningElement)
        {
            if (_hoverSoundPlayed)
                return;

            _hoverSoundPlayed = true;
            SoundEngine.PlaySound(SoundID.MenuTick);

            if (_style.HoverMode == TmodFrameHoverMode.LayerReorder)
                SetLayerReorderHover(true);
        }

        private void OnFrameMouseOut(UIMouseEvent evt, UIElement listeningElement)
        {
            _hoverSoundPlayed = false;

            if (_style.HoverMode == TmodFrameHoverMode.LayerReorder)
                SetLayerReorderHover(false);
        }

        private void SetLayerReorderHover(bool hovered)
        {
            if (_overlay == null || _frameFront == null || _frameGlow == null || _hovered == hovered)
                return;

            _hovered = hovered;
            RemoveChild(_overlay);
            RemoveChild(_frameFront);
            RemoveChild(_frameGlow);

            if (hovered)
            {
                Append(_frameFront);
                Append(_frameGlow);
                _frameGlow.Color = Color.White;
                return;
            }

            _overlay.Color = _style.OverlayColor;
            Append(_overlay);
            Append(_frameFront);
            _frameGlow.Color = Color.Transparent;
        }

        public void SetVisibility(float whenActive, float whenInactive) =>
            _alphaFrame?.SetVisibility(whenActive, whenInactive);

        public void SetHoverImage(Asset<Texture2D> hoverTexture) =>
            _alphaFrame?.SetHoverTexture(hoverTexture);

        public void SetBaseImage(Asset<Texture2D> baseTexture) =>
            _alphaFrame?.SetBaseTexture(baseTexture);

        /// <summary>Non-interactive base/hover UIImage layers for alpha-overlay framed controls.</summary>
        private sealed class AlphaOverlayLayer : UIElement
        {
            private readonly UIImage _baseImage;
            private readonly UIImage _hoverImage;
            private readonly Func<bool> _getHovered;
            public float ActiveAlpha { get; set; } = 1f;
            public float InactiveAlpha { get; set; } = 0.72f;

            public AlphaOverlayLayer(
                Asset<Texture2D> baseTexture,
                Asset<Texture2D> hoverTexture,
                Func<bool> getHovered)
            {
                _getHovered = getHovered;
                IgnoresMouseInteraction = true;

                _baseImage = CreateScaledImage(baseTexture);
                _hoverImage = CreateScaledImage(hoverTexture);
                Append(_baseImage);
                Append(_hoverImage);
                ApplyColors();
            }

            public void SetHoverTexture(Asset<Texture2D> hoverTexture)
            {
                _hoverImage.SetImage(hoverTexture);
                ApplyColors();
            }

            public void SetBaseTexture(Asset<Texture2D> baseTexture)
            {
                _baseImage.SetImage(baseTexture);
                ApplyColors();
            }

            public void SetVisibility(float whenActive, float whenInactive)
            {
                ActiveAlpha = MathHelper.Clamp(whenActive, 0f, 1f);
                InactiveAlpha = MathHelper.Clamp(whenInactive, 0f, 1f);
                ApplyColors();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                ApplyColors();
            }

            private void ApplyColors()
            {
                bool hovered = _getHovered();
                _baseImage.Color = Color.White * (hovered ? ActiveAlpha : InactiveAlpha);
                _hoverImage.Color = hovered ? Color.White : Color.Transparent;
            }

            private static UIImage CreateScaledImage(Asset<Texture2D> texture)
            {
                var image = new UIImage(texture)
                {
                    IgnoresMouseInteraction = true,
                    ScaleToFit = true
                };
                image.Width.Set(0f, 1f);
                image.Height.Set(0f, 1f);
                return image;
            }
        }
    }
}
