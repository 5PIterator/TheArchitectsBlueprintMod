using Microsoft.Xna.Framework;
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

        /// <summary>Transparent border overlay matching Bestiary info panels.</summary>
        public static TmodPanel CreateBorderOverlay()
        {
            var panel = new TmodPanel
            {
                BackgroundColor = Color.Transparent,
                IgnoresMouseInteraction = true
            };
            panel.Width.Set(0f, 1f);
            panel.Height.Set(0f, 1f);
            return panel;
        }

        public static TmodPanel CreateCentered(float width, float height)
        {
            var panel = new TmodPanel();
            TmodLayout.CenterInParent(panel, width, height);
            return panel;
        }

        /// <summary>Vertical stack child that fills the panel content width.</summary>
        public TmodStackPanel AppendVerticalStack(float spacing = TmodTheme.Spacing, bool crossAxisCenter = false)
        {
            var stack = TmodLayout.CreateVerticalStack(spacing, crossAxisCenter);
            TmodLayout.FillParent(stack);
            Append(stack);
            return stack;
        }

        /// <summary>Horizontal stack child that fills the panel content width.</summary>
        public TmodStackPanel AppendHorizontalStack(float spacing = TmodTheme.Spacing, bool crossAxisCenter = true)
        {
            var stack = TmodLayout.CreateHorizontalStack(spacing, crossAxisCenter);
            TmodLayout.FillParent(stack);
            Append(stack);
            return stack;
        }
    }
}
