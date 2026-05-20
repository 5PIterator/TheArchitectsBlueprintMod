using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Non-interactive text label.</summary>
    public sealed class TmodLabel : UITextPanel<string>
    {
        public TmodLabel(string text, float textScale = 0.9f, bool secondary = false)
            : base(text, textScale, large: false)
        {
            TextColor = secondary ? TmodTheme.SecondaryText : TmodTheme.PrimaryText;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            IgnoresMouseInteraction = true;
        }
    }
}
