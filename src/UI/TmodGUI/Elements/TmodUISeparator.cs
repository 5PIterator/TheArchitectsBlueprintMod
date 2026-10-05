using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Full-width horizontal rule using vanilla Images/UI/Divider.</summary>
    public sealed class TmodUISeparator : UIElement
    {
        public TmodUISeparator(float height = Theme.BrowserPayloadDividerHeight)
        {
            Width.Set(0f, 1f);
            Height.Set(height, 0f);
            IgnoresMouseInteraction = true;

            var divider = new UIImage(TmodUIAssets.RequestTexture(TmodUIAssets.Shared.DividerPath))
            {
                IgnoresMouseInteraction = true,
                ScaleToFit = true
            };
            divider.Width.Set(0f, 1f);
            divider.Height.Set(0f, 1f);
            Append(divider);
        }
    }
}
