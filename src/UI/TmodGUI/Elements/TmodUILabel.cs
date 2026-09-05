using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Non-interactive themed text label.</summary>
    public sealed class TmodUILabel : UITextPanel<string>
    {
        public TmodUILabel(string text, float textScale = 0.9f, bool secondary = false)
            : base(text, textScale, large: false)
        {
            TextColor = secondary ? TmodTheme.SecondaryText : TmodTheme.PrimaryText;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            IgnoresMouseInteraction = true;
        }
    }
}
