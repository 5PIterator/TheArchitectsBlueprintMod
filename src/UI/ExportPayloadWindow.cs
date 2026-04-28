using System;
using ArchitectsStencilMod.UIKit;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.UI
{
    /// <summary>
    /// UIKit export payload dialog.
    /// </summary>
    internal sealed class ExportPayloadWindow : UIWindow
    {
        private readonly UITextbox _textBox = new();
        private readonly Action<string> _save;
        private readonly Action _close;

        public ExportPayloadWindow(string defaultProjectName, Action<string> save, Action close)
        {
            // --- Compose a small modal window from UIKit labels, textbox, and buttons.

            _save = save;
            _close = close;
            Width = 520;
            Height = 190;
            Anchor = AnchorPosition.Center;
            Position = new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f);

            AddChild(new UILabel("Export Payload") { Position = new Vector2(18, 16), Scale = 0.72f });
            AddChild(new UILabel("Project name") { Position = new Vector2(18, 58), Scale = 0.46f });

            _textBox.Position = new Vector2(18, 84);
            _textBox.Width = Width - 36;
            _textBox.MaxCharacters = 80;
            _textBox.Text = defaultProjectName ?? string.Empty;
            _textBox.OnEnterPress += (_, _) => SaveAndClose();
            AddChild(_textBox);

            var cancel = new UIButton("Cancel") { AutoSize = false, Width = 92, Position = new Vector2(Width - 212, Height - 48) };
            cancel.onLeftClick += (_, _) => _close();
            AddChild(cancel);

            var export = new UIButton("Export") { AutoSize = false, Width = 92, Position = new Vector2(Width - 110, Height - 48) };
            export.onLeftClick += (_, _) => SaveAndClose();
            AddChild(export);
        }

        public override void Update()
        {
            base.Update();
            if (!_textBox.HadFocus)
                _textBox.Focus();
        }

        /// <summary>Saves the entered project name and closes the dialog.</summary>
        private void SaveAndClose()
        {
            // --- Close before invoking the callback so subsequent UI can open cleanly.

            string name = _textBox.Text;
            _close();
            _save(name);
        }
    }
}
