using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Default metadata row: value text with localized key as hover tooltip.</summary>
    public sealed class TmodUIMetadataDefaultRow : UIElement
    {
        private readonly string _tooltipLabel;
        private readonly UIText _valueLabel;

        public TmodUIMetadataDefaultRow(string tooltipLabel, string value, float height = TmodTheme.BrowserPayloadMetadataLineHeight)
        {
            _tooltipLabel = tooltipLabel;
            Height.Set(height, 0f);
            Width.Set(0f, 1f);

            var panel = new UIPanel
            {
                BackgroundColor = TmodTheme.PanelBackgroundDim,
                BorderColor = TmodTheme.PanelBorder,
                Width = new StyleDimension(0f, 1f),
                Height = new StyleDimension(0f, 1f)
            };
            panel.SetPadding(4f);
            Append(panel);

            _valueLabel = new UIText(value, 0.78f)
            {
                TextColor = TmodTheme.SecondaryText,
                HAlign = 0.5f,
                VAlign = 0.5f,
                TextOriginX = 0.5f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            panel.Append(_valueLabel);

            OnUpdate += OnRowUpdate;
        }

        public void SetValue(string value) => _valueLabel.SetText(value);

        private void OnRowUpdate(UIElement element)
        {
            if (IsMouseHovering && !string.IsNullOrEmpty(_tooltipLabel))
                Main.instance.MouseText(_tooltipLabel, 0, 0);
        }
    }
}
