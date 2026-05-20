using System;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Clickable text button with themed colors.</summary>
    public sealed class TmodButton : UITextPanel<string>
    {
        private readonly Action? _onClick;

        public TmodButton(string text, Action? onClick = null, float width = TmodTheme.ButtonWidth, float height = TmodTheme.ButtonHeight)
            : base(text, 0.9f, large: false)
        {
            _onClick = onClick;
            Width.Set(width, 0f);
            Height.Set(height, 0f);
            TextColor = TmodTheme.PrimaryText;
            BackgroundColor = TmodTheme.ButtonBackground;
            BorderColor = TmodTheme.PanelBorder;
            OnLeftClick += OnClick;
            OnMouseOver += (_, _) => BackgroundColor = TmodTheme.ButtonHover;
            OnMouseOut += (_, _) => BackgroundColor = TmodTheme.ButtonBackground;
        }

        private void OnClick(UIMouseEvent evt, UIElement listeningElement)
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            _onClick?.Invoke();
        }
    }
}
