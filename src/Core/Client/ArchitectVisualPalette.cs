using Microsoft.Xna.Framework;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Single source of truth for overlay colors across all Architect's Stencil systems.
    /// Colors map to the gem/material theme defined in notes.md (PIN=diamond, SET=sapphire,
    /// CPY=amethyst, INC=topaz, EXC=amber, CUT=ruby, PST=emerald, RST=obsidian).
    /// </summary>
    public static class ArchitectVisualPalette
    {
        /// <summary>Diamond white — selection bounds outline (PIN action).</summary>
        public static readonly Color SelectionBoundsColor = new(240, 245, 255);

        /// <summary>White — pin point A marker.</summary>
        public static readonly Color PinAColor = Color.White;

        /// <summary>Sapphire blue — pin point B marker (SET action).</summary>
        public static readonly Color PinBColor = new(15, 82, 186);

        /// <summary>Amethyst purple — projection/copy target border (CPY action).</summary>
        public static readonly Color ProjectionCopyColor = new(153, 102, 204);

        /// <summary>Ruby red — projection target border when cut-from-source is active (CUT action).</summary>
        public static readonly Color ProjectionCutColor = new(200, 20, 60);

        /// <summary>Red — source area border when cut-from-source is active.</summary>
        public static readonly Color ProjectionSourceCutColor = new(220, 50, 50);

        /// <summary>Light brown — inventory container outline.</summary>
        public static readonly Color InventoryOutlineColor = new(139, 69, 19);

        /// <summary>Emerald green — paste / PST action tint.</summary>
        public static readonly Color PasteColor = new(80, 200, 120);

        /// <summary>Light red — clear phase overlay tint.</summary>
        public static readonly Color ClearPhaseColor = new(255, 100, 100);

        /// <summary>Light blue — place phase overlay tint.</summary>
        public static readonly Color PlacePhaseColor = new(100, 180, 255);

        /// <summary>Topaz yellow — INCLUDE rectangle gesture preview.</summary>
        public static readonly Color IncludePreviewColor = new(255, 200, 0);

        /// <summary>Amber orange — EXCLUDE rectangle gesture preview.</summary>
        public static readonly Color ExcludePreviewColor = new(255, 140, 0);

        /// <summary>Obsidian gray — RESET action tint.</summary>
        public static readonly Color ResetColor = new(80, 80, 80);

        /// <summary>Bright cyan — projection targeted by payload import/export UI.</summary>
        public static readonly Color PayloadIoHighlightColor = new(0, 220, 255);
    }
}
