using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Wide Bestiary icon button with left-aligned value text.</summary>
    public sealed class TmodUICycleButton : TmodButtonChrome
    {
        private readonly UIText _label;

        public TmodUICycleButton(
            string value,
            string? hoverText,
            Action? onClick,
            string iconTexturePath,
            float? widthOverride = null,
            float? heightOverride = null)
            : base(
                ResolveWidth(iconTexturePath, widthOverride),
                ResolveHeight(iconTexturePath, heightOverride),
                TmodFrameStyle.BestiaryWideIconButton(iconTexturePath),
                hoverText,
                onClick)
        {
            // --- Place value text at the Bestiary offset beside the icon art.
            _label = new UIText(value, 0.8f)
            {
                TextColor = Theme.PrimaryText,
                Left = new StyleDimension(34f, 0f),
                Top = new StyleDimension(2f, 0f),
                VAlign = 0.5f,
                TextOriginX = 0f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            ContentHost.Append(_label);
        }

        /// <summary>Legacy constructor for non-icon cycle buttons using wide border chrome.</summary>
        public TmodUICycleButton(
            string value,
            string? hoverText,
            Action? onClick,
            float width,
            float height = Theme.BrowserToolbarButtonSize,
            string? secondaryTexturePath = null)
            : base(
                width,
                height,
                secondaryTexturePath != null
                    ? TmodFrameStyle.BestiaryWideIconButton(secondaryTexturePath)
                    : TmodFrameStyle.ButtonBorder(wide: true),
                hoverText,
                onClick)
        {
            _label = new UIText(value, 0.8f)
            {
                TextColor = Theme.PrimaryText,
                Left = new StyleDimension(secondaryTexturePath != null ? 34f : 8f, 0f),
                Top = new StyleDimension(2f, 0f),
                VAlign = 0.5f,
                TextOriginX = 0f,
                TextOriginY = 0.5f,
                IgnoresMouseInteraction = true
            };
            ContentHost.Append(_label);
        }

        /// <summary>Updates the visible cycle value text.</summary>
        public void SetValue(string value) => _label.SetText(value);

        private static float ResolveWidth(string texturePath, float? widthOverride)
        {
            if (widthOverride.HasValue)
                return widthOverride.Value;

            Texture2D? texture = TmodUIAssets.RequestTexture(texturePath).Value;
            return texture?.Width ?? Theme.BrowserToolbarDropdownWidth;
        }

        private static float ResolveHeight(string texturePath, float? heightOverride)
        {
            if (heightOverride.HasValue)
                return heightOverride.Value;

            Texture2D? texture = TmodUIAssets.RequestTexture(texturePath).Value;
            return texture?.Height ?? Theme.BrowserToolbarButtonSize;
        }
    }
}
