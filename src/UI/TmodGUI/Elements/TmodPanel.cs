using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Themed panel background for dialogs and containers.</summary>
    public class TmodPanel : UIPanel
    {
        public TmodPanel()
        {
            BackgroundColor = TmodTheme.PanelBackground;
            BorderColor = TmodTheme.PanelBorder;
            SetPadding(TmodTheme.Padding);
        }

        public static TmodPanel CreateCentered(float width, float height)
        {
            var panel = new TmodPanel();
            TmodLayout.CenterInParent(panel, width, height);
            return panel;
        }
    }
}
