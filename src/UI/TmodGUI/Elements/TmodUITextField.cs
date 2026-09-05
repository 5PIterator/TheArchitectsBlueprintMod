using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Single-line field framed with Bestiary search border chrome.</summary>
    public sealed class TmodUITextField : UIElement
    {
        private readonly UITextBox _textBox;

        public string Text => _textBox.Text;

        public new bool IsMouseHovering => _textBox.IsMouseHovering;

        public TmodUITextField(string defaultText, int maxLength, float width, float height = TmodTheme.TextFieldHeight)
        {
            Width.Set(width, 0f);
            Height.Set(height, 0f);

            var frame = new UIImage(TmodUIAssets.RequestTexture(TmodUIAssets.Bestiary.ButtonSearchBorderPath))
            {
                IgnoresMouseInteraction = true,
                ScaleToFit = true
            };
            frame.Width.Set(0f, 1f);
            frame.Height.Set(0f, 1f);
            Append(frame);

            _textBox = new UITextBox(defaultText ?? string.Empty, 0.9f, large: false)
            {
                DrawPanel = false,
                ShowInputTicker = true
            };
            _textBox.Width.Set(-12f, 1f);
            _textBox.Height.Set(-8f, 1f);
            _textBox.HAlign = 0.5f;
            _textBox.VAlign = 0.5f;
            _textBox.SetTextMaxLength(maxLength);
            Append(_textBox);
        }

        public void SetText(string text, float scale, bool large) => _textBox.SetText(text, scale, large);

        public new event UIElement.MouseEvent OnLeftClick
        {
            add => _textBox.OnLeftClick += value;
            remove => _textBox.OnLeftClick -= value;
        }

        public new event UIElement.MouseEvent OnMouseOut
        {
            add => _textBox.OnMouseOut += value;
            remove => _textBox.OnMouseOut -= value;
        }
    }
}
