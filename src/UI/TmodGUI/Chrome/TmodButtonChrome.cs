using System;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Bestiary button frame with configurable border and optional secondary layer.</summary>
    public class TmodButtonChrome : TmodFramedControl
    {

        public TmodButtonChrome(
            float width,
            float height,
            string? hoverText = null,
            Action? onClick = null,
            bool wideBorder = false,
            string? secondaryTexturePath = null)
            : base(
                width,
                height,
                TmodFrameStyle.ButtonBorder(wideBorder, secondaryTexturePath),
                hoverText,
                onClick: onClick)
        {
        }

        protected TmodButtonChrome(
            float width,
            float height,
            TmodFrameStyle style,
            string? hoverText = null,
            Action? onClick = null)
            : base(width, height, style, hoverText, onClick: onClick)
        {
        }
    }
}