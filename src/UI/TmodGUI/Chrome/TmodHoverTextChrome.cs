using System;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Chrome base that shows cursor tooltip text while hovered.</summary>
    public abstract class TmodHoverTextChrome : UIElement
    {
        private string? _hoverText;
        private readonly Func<string>? _getHoverText;

        protected TmodHoverTextChrome(string? hoverText = null, Func<string>? getHoverText = null)
        {
            _hoverText = hoverText;
            _getHoverText = getHoverText;
        }

        public string? HoverText
        {
            get => ResolveHoverText();
            set => _hoverText = value;
        }

        protected string? ResolveHoverText()
        {
            if (_getHoverText != null)
                return _getHoverText();

            return _hoverText;
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            if (!IsMouseHovering)
                return;

            string? text = ResolveHoverText();
            if (!string.IsNullOrEmpty(text))
                Main.instance.MouseText(text, 0, 0);
        }
    }
}
