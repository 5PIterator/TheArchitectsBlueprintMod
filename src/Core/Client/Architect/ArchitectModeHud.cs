using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.Core.Architect
{
    /// <summary>Suppresses vanilla HUD layers during Architect Mode and anchors the equip-page entry tab.</summary>
    internal static class ArchitectModeHud
    {
        private const int VanillaEquipPageIconSize = 32;

        /// <summary>
        /// Vanilla Camera page-icon top-left X offset from the right edge of the UI virtual screen
        /// (same constant Main uses for the equipment page strip).
        /// </summary>
        private const int VanillaCameraIconOffsetFromRight = 162;

        /// <summary>Vanilla interface layers skipped after Camera Mode draws (same HUD elements hidden).</summary>
        private static readonly string[] SuppressedLayerNames =
        [
            "Vanilla: Resource Bars",
            "Vanilla: Inventory",
            "Vanilla: Info Accessories Bar",
            "Vanilla: Settings Button",
            "Vanilla: Hotbar",
            "Vanilla: Builder Accessories Bar",
            "Vanilla: Radial Hotbars",
            "Vanilla: Map / Minimap",
            "Vanilla: Entity Health Bars",
            "Vanilla: Invasion Progress Bars",
            "Vanilla: NPC / Sign Dialog",
            "Vanilla: Player Chat",
            "Vanilla: Cursor",
            "Vanilla: Mouse Item / NPC Head",
            "Vanilla: Interact Item Icon",
            "Vanilla: Mouse Over",
        ];

        /// <summary>True when the equip-page entry tab should appear (inventory open, mode inactive).</summary>
        public static bool ShouldShowEntryTab(bool architectModeActive) =>
            Main.playerInventory && !architectModeActive;

        /// <summary>Disables vanilla HUD layers that Camera Mode skips while Architect Mode is active.</summary>
        public static void ApplyLayerSuppression(List<GameInterfaceLayer> layers, bool active)
        {
            // --- Match Camera Mode: hide hotbar, resource bars, minimap, etc.; keep mod overlays at Mouse Text.

            if (!active)
                return;

            for (int i = 0; i < layers.Count; i++)
            {
                if (!ShouldSuppressLayer(layers[i].Name))
                    continue;

                layers[i].Active = false;
            }
        }

        /// <summary>
        /// Returns the equip-page tab rect immediately left of the Camera page icon (32×32 UI space).
        /// Callers must already be in UI zoom (<see cref="PlayerInput.SetZoom_UI"/>).
        /// </summary>
        public static Rectangle GetEntryTabRect()
        {
            // --- Mirror vanilla Camera icon top-left, then sit one page-icon width to its left.

            PlayerInput.SetZoom_UI();

            int tabSize = VanillaEquipPageIconSize;
            int cameraTopLeftX = Main.screenWidth - VanillaCameraIconOffsetFromRight;
            int cameraTopLeftY = GetVanillaEquipPageIconTopY();

            return new Rectangle(cameraTopLeftX - tabSize, cameraTopLeftY, tabSize, tabSize);
        }

        private static int GetVanillaEquipPageIconTopY() =>
            174 - PlayerInput.SettingsForUI.PushEquipmentAreaUp.ToInt() * 30;

        private static bool ShouldSuppressLayer(string layerName)
        {
            foreach (string suppressed in SuppressedLayerNames)
            {
                if (layerName.Equals(suppressed, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
