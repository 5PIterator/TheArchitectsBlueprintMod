using System;
using Microsoft.Xna.Framework;

namespace TheArchitectsBlueprintMod.UI.Style
{
    /// <summary>Shared dimensions and colors for TmodGUI elements and widgets.</summary>
    public static class Theme
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

        public const float BrowserCompactCellSize = 72f;
        public const int BrowserCompactColumns = 5;
        public const int BrowserVisibleRows = 4;
        public const float BrowserScrollbarWidth = 20f;
        public const float BrowserControlPanelWidth = 64f;
        public const float BrowserControlButtonSize = 40f;
        public const float BrowserPayloadPanelWidth = 260f;
        public const float BrowserPayloadThumbnailSize = 112f;
        public const float BrowserPayloadDividerHeight = 2f;
        public const float BrowserPayloadMetadataLineHeight = 28f;
        public const float BrowserPayloadMetadataCardMinHeight = 28f;
        public const int BrowserPayloadMetadataMinRows = 3;
        public const float BrowserToolbarHeight = 32f;
        public const float BrowserToolbarButtonSize = 24f;
        public const float BrowserToolbarIconSize = 16f;
        public const float BrowserToolbarDropdownWidth = 112f;
        public const float BrowserSearchFieldWidth = 200f;
        public const float BrowserOptionsPopupAnchorLeft = -118f;
        public const float BrowserFooterHeight = 36f;
        public const float BrowserFooterButtonWidth = 96f;
        public const float BrowserFooterButtonHeight = 24f;
        public const float BrowserFooterFormatWidth = 140f;
        public const float BrowserStatusHeight = 18f;
        public const float BrowserNameplateHeight = 24f;
        public const float MessageBoxMinHeight = 120f;
        public const float Padding = 12f;
        public const float Spacing = 8f;
        public const float ButtonWidth = 100f;
        public const float ButtonHeight = 28f;
        public const float TextFieldHeight = 32f;
        public const float FooterHeight = 48f;
        public const float TitleHeight = 24f;

        public static float BrowserGridInnerWidth =>
            BrowserCompactColumns * BrowserCompactCellSize + (BrowserCompactColumns - 1) * Spacing;

        public static float BrowserEntryPanelInnerWidth => BrowserGridInnerWidth + BrowserScrollbarWidth;

        public static float BrowserEntryPanelWidth => BrowserEntryPanelInnerWidth + Padding * 2f;

        public static float BrowserGridViewportHeight =>
            BrowserVisibleRows * (BrowserCompactCellSize + Spacing) - Spacing;

        public static float BrowserWidth =>
            BrowserEntryPanelWidth
            + BrowserPayloadPanelWidth
            + Padding * 2f;

        public const float BrowserScrollbarInset = 4f;

        public static float BrowserPayloadSectionHeight =>
            BrowserNameplateHeight
            + Spacing
            + BrowserPayloadDividerHeight
            + Spacing
            + BrowserPayloadThumbnailSize
            + Spacing
            + AuthorRowHeight
            + Spacing
            + BrowserPayloadDividerHeight
            + Spacing
            + BrowserPayloadMetadataMinRows * BrowserPayloadMetadataCardMinHeight;

        public const float AuthorRowHeight = 18f;

        public static float BrowserContentHeight => Math.Max(
            BrowserGridViewportHeight,
            BrowserPayloadSectionHeight);

        public static float BrowserHeight =>
            Padding
            + BrowserToolbarHeight
            + BrowserContentHeight
            + Spacing
            + BrowserFooterHeight
            + BrowserStatusHeight
            + Padding;

        // Bestiary UI palette (vanilla UIBestiaryEntryInfoPage).
        public static readonly Color PanelBackground = new(73, 94, 171, 255);
        public static readonly Color PanelBorder = new(89, 116, 213, 255);
        public static readonly Color PanelBackgroundDim = new(35, 40, 83, 128);
        public static readonly Color WindowPanelBackground = new(18, 22, 48, 210);
        public static readonly Color WindowPanelBorder = new(55, 68, 120, 230);
        public static readonly Color InfoPageBackground = new(73, 94, 171, 255);
        public static readonly Color InfoPageBorder = new(89, 116, 213, 255);
        public static readonly Color PrimaryText = Color.White;
        public static readonly Color SecondaryText = new(200, 200, 220, 255);
        public static readonly Color PrimaryAccent = Palette.PayloadIoHighlightColor;
        public static readonly Color ButtonBackground = new(63, 82, 151, 255);
        public static readonly Color ButtonHover = new(90, 110, 190, 255);
        public static readonly Color DisabledText = new(140, 140, 150, 255);
        public static readonly Color BestiarySlotOverlay = Color.White * 0.6f;
    }
}
