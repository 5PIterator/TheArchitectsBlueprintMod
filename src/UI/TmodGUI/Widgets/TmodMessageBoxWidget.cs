using System;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Widgets
{
    /// <summary>Simple dialog with a message body and an OK button.</summary>
    public sealed class TmodMessageBoxWidget : TmodDialogWidget
    {
        private readonly string _message;
        private readonly Action? _onOk;

        public TmodMessageBoxWidget(string message, string title = "Message", Action? onOk = null)
            : base(title, TmodTheme.DialogWidth, TmodTheme.MessageBoxMinHeight + 80f)
        {
            _message = message;
            _onOk = onOk;
        }

        public static void Open(string message, string title = "Message", Action? onOk = null)
        {
            TmodGUI.Show(new TmodMessageBoxWidget(message, title, onOk));
        }

        protected override void BuildContent(UIElement content)
        {
            var body = new TmodLabel(_message, textScale: 0.85f);
            TmodLayout.FillWidth(body, 60f);
            content.Append(body);
        }

        protected override void BuildFooter(UIElement footer)
        {
            var ok = new TmodButton("OK", OnOk);
            TmodLayout.AnchorBottomRight(ok, 0f, 0f, TmodTheme.ButtonWidth, TmodTheme.ButtonHeight);
            footer.Append(ok);
        }

        private void OnOk()
        {
            _onOk?.Invoke();
            RequestClose();
        }
    }
}
