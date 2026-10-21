using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Wide Bestiary-framed text action button with enable/disable support.</summary>
    public sealed class TmodUITextActionButton : TmodButtonChrome
    {
        private readonly UIText _label;
        private readonly Action? _onClick;
        private bool _enabled = true;

        public TmodUITextActionButton(
            string text,
            Action? onClick,
            float width = Theme.BrowserFooterButtonWidth,
            float height = 0f,
            string? hoverText = null)
            : base(
                width,
                height > 0f ? height : ResolveWideBorderHeight(),
                TmodFrameStyle.ButtonBorder(wide: true),
                hoverText,
                onClick: null)
        {
            _onClick = onClick;
            _label = new UIText(text, 0.8f)
            {
                TextColor = Theme.PrimaryText,
                HAlign = 0.5f,
                VAlign = 0.5f,
                TextOriginX = 0.5f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            ContentHost.Append(_label);
            OnLeftClick += OnActionClick;
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;

                _enabled = value;
                _label.TextColor = value ? Theme.PrimaryText : Theme.DisabledText;
                SetVisibility(value ? 1f : 0.45f, value ? 0.72f : 0.35f);
            }
        }

        /// <summary>Updates the visible button label.</summary>
        public void SetText(string text) => _label.SetText(text);

        private void OnActionClick(UIMouseEvent evt, UIElement listeningElement)
        {
            if (!_enabled)
                return;

            _onClick?.Invoke();
        }

        /// <summary>Uses the natural Button_Wide_Border height so hover chrome fits.</summary>
        private static float ResolveWideBorderHeight()
        {
            // --- Prefer the wide-border texture height; fall back to footer constant.
            Texture2D? texture = TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.ButtonWideBorderPath).Value;
            return texture?.Height ?? Theme.BrowserFooterButtonHeight;
        }
    }
}
