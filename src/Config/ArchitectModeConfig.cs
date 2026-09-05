using System.ComponentModel;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using TheArchitectsBlueprintMod.Core.ArchitectMode;

namespace TheArchitectsBlueprintMod.Config
{
    /// <summary>Client-side Architect Mode utilities, including a panic clear for accidental permanent unlocks.</summary>
    public sealed class ArchitectModeConfig : ModConfig
    {
        public static ArchitectModeConfig Instance = null!;

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Panic")]
        [DefaultValue(false)]
        public bool ClearPermanentArchitectModeUnlock;

        public override void OnLoaded()
        {
            Instance = this;
        }

        public override void OnChanged()
        {
            // --- Apply one-shot panic clear, then reset the toggle so it does not re-fire.

            Instance = this;

            if (ClearPermanentArchitectModeUnlock)
            {
                TryClearPermanentUnlock();
                ClearPermanentArchitectModeUnlock = false;
            }
        }

        /// <summary>Clears the local player's permanent Architect Mode unlock and closes an active session.</summary>
        private static void TryClearPermanentUnlock()
        {
            // --- Require an in-world local player, then wipe unlock and exit Architect Mode if open.

            if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
            {
                Main.NewText(
                    Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.PanicNeedsWorld"),
                    byte.MaxValue,
                    180,
                    100);
                return;
            }

            var architectPlayer = player.GetModPlayer<ArchitectModePlayer>();
            bool wasUnlocked = architectPlayer.ArchitectGuiUnlocked;
            architectPlayer.ClearPermanentUnlock();

            var architectMode = ModContent.GetInstance<ArchitectModeSystem>();
            if (architectMode.Active)
                architectMode.Toggle(false);

            Main.NewText(
                Language.GetTextValue(
                    wasUnlocked
                        ? "Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.PanicCleared"
                        : "Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.PanicAlreadyCleared"),
                220,
                220,
                120);
        }
    }
}
