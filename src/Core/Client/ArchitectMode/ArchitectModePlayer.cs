using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TheArchitectsBlueprintMod.Core.ArchitectMode
{
    /// <summary>Tracks Architect Mode GUI unlock and whether Architect's Hand is currently equipped.</summary>
    public sealed class ArchitectModePlayer : ModPlayer
    {
        /// <summary>Permanent unlock from using Architect's Blueprint.</summary>
        public bool ArchitectGuiUnlocked { get; private set; }

        /// <summary>Set each frame by <see cref="Content.Items.ArchitectsHand"/> via <see cref="UpdateAccessory"/>.</summary>
        public bool HandEquipped { get; private set; }

        /// <summary>True when the GUI entry tab should appear and mode can be toggled.</summary>
        public bool CanAccessArchitectGui => ArchitectGuiUnlocked || HandEquipped;

        /// <summary>Marks Architect Mode as permanently unlocked for this character.</summary>
        public void UnlockArchitectGuiPermanently() => ArchitectGuiUnlocked = true;

        /// <summary>Clears the permanent Architect Mode unlock for this character (config panic button).</summary>
        public void ClearPermanentUnlock() => ArchitectGuiUnlocked = false;

        /// <summary>Called from Architect's Hand while the accessory is equipped.</summary>
        internal void NotifyHandEquipped() => HandEquipped = true;

        public override void ResetEffects() => HandEquipped = false;

        public override void PreUpdate()
        {
            // --- Keep inventory closed for the whole Architect Mode session.

            if (Player.whoAmI != Main.myPlayer)
                return;

            if (!ModContent.GetInstance<ArchitectModeSystem>().Active)
                return;

            Main.playerInventory = false;
            Player.controlInv = false;
        }

        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            // --- Exit on Esc/inventory like Camera Mode, then suppress hotbar while active.

            if (Player.whoAmI != Main.myPlayer)
                return;

            var architectMode = ModContent.GetInstance<ArchitectModeSystem>();
            if (!architectMode.Active)
                return;

            // Esc / inventory key closes Architect Mode (Camera Mode parity).
            if (triggersSet.Inventory)
                architectMode.Toggle(false);

            triggersSet.Inventory = false;
            triggersSet.Hotbar1 = false;
            triggersSet.Hotbar2 = false;
            triggersSet.Hotbar3 = false;
            triggersSet.Hotbar4 = false;
            triggersSet.Hotbar5 = false;
            triggersSet.Hotbar6 = false;
            triggersSet.Hotbar7 = false;
            triggersSet.Hotbar8 = false;
            triggersSet.Hotbar9 = false;
            triggersSet.Hotbar10 = false;
        }

        public override void SaveData(TagCompound tag) => tag["architectGuiUnlocked"] = ArchitectGuiUnlocked;

        public override void LoadData(TagCompound tag)
        {
            // --- Prefer the current key; fall back to the legacy blueprint unlock flag.

            if (tag.ContainsKey("architectGuiUnlocked"))
                ArchitectGuiUnlocked = tag.GetBool("architectGuiUnlocked");
            else if (tag.ContainsKey("blueprintGuiUnlocked"))
                ArchitectGuiUnlocked = tag.GetBool("blueprintGuiUnlocked");
        }
    }
}
