using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Single-line text input; captures keys in <see cref="DrawSelf"/> when focused.</summary>
    public sealed class TmodTextField : UITextBox
    {
        private bool _focused;

        public bool IsFocused => _focused;

        public TmodTextField(string defaultText, int maxLength, float width, float height = TmodTheme.TextFieldHeight)
            : base(defaultText ?? string.Empty, 0.9f, large: false)
        {
            Width.Set(width, 0f);
            Height.Set(height, 0f);
            SetTextMaxLength(maxLength);
            ShowInputTicker = true;
            DrawPanel = true;
        }

        public void Focus()
        {
            _focused = true;
            Main.clrInput();
            PlayerInput.WritingText = true;
        }

        public void UnfocusField() => _focused = false;

        public string GetTrimmedText() => Text?.Trim() ?? string.Empty;

        public override void LeftMouseDown(UIMouseEvent evt)
        {
            base.LeftMouseDown(evt);
            if (evt.Target == this)
                Focus();
        }

        public override void Update(GameTime gameTime)
        {
            if (_focused)
            {
                PlayerInput.WritingText = true;
                return;
            }

            base.Update(gameTime);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            if (_focused)
            {
                PlayerInput.WritingText = true;
                Main.instance.HandleIME();
                string newText = Main.GetInputText(Text ?? string.Empty);
                if (newText != Text)
                    SetText(newText, TextScale, large: false);
            }

            base.DrawSelf(spriteBatch);
        }
    }
}
