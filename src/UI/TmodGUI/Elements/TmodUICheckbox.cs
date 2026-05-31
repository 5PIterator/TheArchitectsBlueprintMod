using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Vanilla <c>Settings_Toggle</c> via <see cref="UIToggleImage"/>; tooltip on hover only.</summary>
    public sealed class TmodUICheckbox : TmodHoverTextChrome
    {
        private const int ToggleDrawSize = 16;
        private static readonly Point ToggleOnOffset = new(16, 0);
        private static readonly Point ToggleOffOffset = new(0, 0);

        private readonly string _hoverTextWhenOff;
        private readonly string _hoverTextWhenOn;
        private readonly UIToggleImage _toggle;
        private bool _selected;

        public TmodUICheckbox(string hoverTextWhenOff, string hoverTextWhenOn, bool initialState = false)
            : base()
        {
            _hoverTextWhenOff = hoverTextWhenOff;
            _hoverTextWhenOn = hoverTextWhenOn;
            _selected = initialState;

            Width.Set(TmodTheme.BrowserControlButtonSize, 0f);
            Height.Set(TmodTheme.BrowserControlButtonSize, 0f);

            _toggle = new UIToggleImage(
                TmodUIAssets.RequestTexture(TmodUIAssets.Shared.SettingsTogglePath),
                ToggleDrawSize,
                ToggleDrawSize,
                ToggleOnOffset,
                ToggleOffOffset)
            {
                HAlign = 0.5f,
                VAlign = 0.5f,
                IgnoresMouseInteraction = true,
            };
            _toggle.SetState(initialState);
            Append(_toggle);

            OnLeftClick += (_, _) =>
            {
                Selected = !Selected;
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;

                _selected = value;
                _toggle.SetState(value);
            }
        }

        public event EventHandler? SelectedChanged;

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            if (!IsMouseHovering)
                return;

            string text = _selected ? _hoverTextWhenOn : _hoverTextWhenOff;
            if (!string.IsNullOrEmpty(text))
                Main.instance.MouseText(text, 0, 0);
        }
    }
}
