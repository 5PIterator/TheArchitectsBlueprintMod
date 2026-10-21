using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Bestiary Stat_Panel metadata card with visible label and value.</summary>
    public sealed class TmodUIMetadataDefaultRow : UIElement
    {
        private static readonly Color StatPanelBackground = new(43, 56, 101);
        private readonly UIText _label;
        private readonly UIText _valueLabel;
        private readonly bool _wrapValue;

        public TmodUIMetadataDefaultRow(
            string label,
            string value,
            bool wrapValue = false,
            float minHeight = Theme.BrowserPayloadMetadataCardMinHeight)
        {
            // --- Build a nine-slice Stat_Panel card with label/value text.
            _wrapValue = wrapValue;
            Width.Set(0f, 1f);
            Height.Set(minHeight, 0f);

            Asset<Texture2D> panelTexture = TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.StatPanelPath);
            var panel = new UIPanel(panelTexture, null, 12, 7)
            {
                Width = new StyleDimension(-6f, 1f),
                Height = new StyleDimension(0f, 1f),
                BackgroundColor = StatPanelBackground,
                BorderColor = Color.Transparent,
                Left = new StyleDimension(3f, 0f),
                PaddingLeft = 4f,
                PaddingRight = 4f,
                PaddingTop = 4f,
                PaddingBottom = 4f
            };
            Append(panel);

            _label = new UIText(label, 0.72f)
            {
                TextColor = Theme.DisabledText,
                HAlign = 0f,
                VAlign = wrapValue ? 0f : 0.5f,
                TextOriginX = 0f,
                TextOriginY = wrapValue ? 0f : 0.5f,
                IgnoresMouseInteraction = true
            };
            panel.Append(_label);

            _valueLabel = new UIText(value ?? string.Empty, 0.72f)
            {
                TextColor = Theme.SecondaryText,
                HAlign = wrapValue ? 0f : 1f,
                VAlign = wrapValue ? 0f : 0.5f,
                Top = wrapValue ? new StyleDimension(16f, 0f) : new StyleDimension(0f, 0f),
                Width = wrapValue ? StyleDimension.FromPixelsAndPercent(0f, 1f) : default,
                TextOriginX = wrapValue ? 0f : 1f,
                TextOriginY = wrapValue ? 0f : 0.5f,
                IsWrapped = wrapValue,
                IgnoresMouseInteraction = true
            };
            panel.Append(_valueLabel);

            if (wrapValue)
                AttachDynamicResize(panel, _valueLabel);
        }

        /// <summary>Updates the value text while keeping the label unchanged.</summary>
        public void SetValue(string value) => _valueLabel.SetText(value ?? string.Empty);

        private void AttachDynamicResize(UIElement container, UIText text)
        {
            // --- Grow the card when wrapped text reports a larger min height.
            text.OnInternalTextChange += () =>
            {
                float needed = text.MinHeight.Pixels + 20f;
                float min = Theme.BrowserPayloadMetadataCardMinHeight;
                Height.Set(System.Math.Max(min, needed), 0f);
                container.Height = new StyleDimension(0f, 1f);
            };
        }
    }
}
