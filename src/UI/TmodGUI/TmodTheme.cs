using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.TmodGUI
{
    /// <summary>Shared dimensions and colors for TmodGUI elements and widgets.</summary>
    public static class TmodTheme
    {
        public const float DialogWidth = 520f;
        public const float ExportDialogHeight = 190f;
        public const float ImportDialogHeight = 420f;
        public const float ImportDialogWidth = 640f;
        public const float ImportDialogPanelHeight = 460f;
        public const float ImportMetadataColumnWidth = 180f;
        public const float ImportCardWidth = 112f;
        public const float ImportCardHeight = 152f;
        public const float ImportThumbnailSize = 100f;
        public const float BrowserWidth = 1080f;
        public const float BrowserHeight = 640f;
        public const float BrowserFilePanelWidth = 640f;
        public const float BrowserBlueprintThumbnailSize = 300f;
        public const float BrowserCompactCardSize = 100f;
        public const float BrowserListCardHeight = 78f;
        public const float MessageBoxMinHeight = 120f;
        public const float Padding = 16f;
        public const float Spacing = 8f;
        public const float ButtonWidth = 100f;
        public const float ButtonHeight = 32f;
        public const float TextFieldHeight = 36f;
        public const float FooterHeight = 48f;
        public const float TitleHeight = 28f;

        public static readonly Color PanelBackground = new Color(73, 94, 171, 200);
        public static readonly Color PanelBorder = new Color(100, 120, 200, 255);
        public static readonly Color PrimaryText = Color.White;
        public static readonly Color SecondaryText = new Color(200, 200, 220, 255);
        public static readonly Color PrimaryAccent = ArchitectVisualPalette.PayloadIoHighlightColor;
        public static readonly Color ButtonBackground = new Color(63, 82, 151, 255);
        public static readonly Color ButtonHover = new Color(90, 110, 190, 255);
        public static readonly Color DisabledText = new Color(140, 140, 150, 255);
    }
}
