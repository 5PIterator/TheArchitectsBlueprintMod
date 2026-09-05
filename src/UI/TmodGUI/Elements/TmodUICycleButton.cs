using System;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Wide Bestiary-framed cycle button with right-aligned value text.</summary>
    public sealed class TmodUICycleButton : TmodButtonChrome
    {
        private readonly UITextPanel<string> _label;

        public TmodUICycleButton(
            string value,
            string? hoverText,
            Action? onClick,
            float width,
            float height = TmodTheme.BrowserToolbarButtonSize,
            string? secondaryTexturePath = null)
            : base(width, height, hoverText, onClick, wideBorder: true, secondaryTexturePath: secondaryTexturePath)
        {
            _label = new UITextPanel<string>(value, 0.78f, large: false)
            {
                TextColor = TmodTheme.SecondaryText,
                BackgroundColor = Color.Transparent,
                BorderColor = Color.Transparent,
                IgnoresMouseInteraction = true,
                HAlign = 1f,
                VAlign = 0.5f,
                MarginRight = 6f
            };
            ContentHost.Append(_label);
        }

        public void SetValue(string value) => _label.SetText(value, 0.78f, false);
    }
}
