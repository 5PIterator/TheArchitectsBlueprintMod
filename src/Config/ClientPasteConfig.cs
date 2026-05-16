using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>
    /// Client-side paste planning preferences. Step size is additionally capped by <see cref="PastingConfig.MaxPasteStepSize"/> on the server.
    /// </summary>
    public sealed class ClientPasteConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("PastePlanning")]

        /// <summary>
        /// When enabled (default), do not queue placement where required items are missing, and treat item failures during placement as task-complete.
        /// </summary>
        [DefaultValue(true)]
        public bool IgnoreItems = true;

        /// <summary>
        /// When enabled (default), do not queue placement where the tile cannot be placed by world/rules, and treat invalid-placement failures as task-complete.
        /// </summary>
        [DefaultValue(true)]
        public bool IgnoreInvalid = true;

        /// <summary>
        /// When enabled (default), clears align with planned placement (selected source cells); deselected source cells do not clear the target.
        /// When disabled, clears are planned for the full projection footprint (opaque paste).
        /// </summary>
        [DefaultValue(true)]
        public bool TransparentPaste = true;

        /// <summary>
        /// Target tiles processed per paste step. The effective value is at most the server's maximum.
        /// </summary>
        [DefaultValue(1)]
        [Range(1, 15)]
        public int PasteStepSize = 1;
    }
}
