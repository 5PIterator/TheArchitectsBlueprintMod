using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace ArchitectsStencilMod.Config
{
    /// <summary>
    /// Client-side paste planning preferences. Step size is additionally capped by <see cref="PastingConfig.MaxPasteStepSize"/> on the server.
    /// </summary>
    public sealed class ClientPasteConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("PastePlanning")]

        /// <summary>
        /// When enabled (default), tile placement is only planned when the tile can actually be placed.
        /// </summary>
        [DefaultValue(true)]
        public bool ForceTiles = true;

        /// <summary>
        /// When enabled (default), clears align with planned placement (selected source cells); deselected source cells do not clear the target.
        /// When disabled, clears are planned for the full projection footprint (opaque paste).
        /// </summary>
        [DefaultValue(true)]
        public bool TransparentPaste = true;

        /// <summary>
        /// Target tiles processed per paste step. The effective value is at most the server's maximum.
        /// </summary>
        [DefaultValue(5)]
        [Range(1, 512)]
        public int PasteStepSize = 5;
    }
}
