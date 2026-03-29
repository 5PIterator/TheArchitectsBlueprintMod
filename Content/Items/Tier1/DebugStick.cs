using System.Collections.Generic;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.Layers;
using ArchitectsStencilMod.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using static ArchitectsStencilMod.Core.ToolActionManager;

namespace ArchitectsStencilMod.Content.Items
{

    /// <summary>
    /// Debug stick: when held during discovery (or other steppable processes),
    /// left-click steps, right-click cancels.
    /// For pasting: initializes like PasteStick (PASTE action), but drives execution manually via stepping.
    /// Temporary: hold Left Shift + left-click to place a wood Table at the cursor and immediately run
    /// <see cref="FrameOperations.Clear"/> on its frame (repro for frame clear tasks).
    /// </summary>
    public class DebugStick : ModItem
    {
        private bool _placeTable = true;
        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 28;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.rare = ItemRarityID.Orange;
            Item.value = Item.buyPrice(silver: 10);
            Item.autoReuse = true;
            Item.useTurn = true;
            Item.shootsEveryUse = true;
            Item.noMelee = true;
        }

        public override bool AltFunctionUse(Player player) => true;

        public override bool? UseItem(Player player)
        {
            if (player.whoAmI != Main.myPlayer)
                return null;

            Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
            var actionManager = ModContent.GetInstance<ToolActionManager>();

            if (player.altFunctionUse == 2)
            {
                if (_placeTable)
                {
                    DebugPlaceOrClearWoodTable(cursorTile, true);
                }
                actionManager.Handle(ToolAction.PASTE, ToolActionKey.RightClick, player, cursorTile, ToolActionModifier.Reset);
            }
            else
            {
                if (_placeTable)
                {
                    DebugPlaceOrClearWoodTable(cursorTile, false);
                }
                actionManager.Handle(ToolAction.PASTE, ToolActionKey.LeftClick, player, cursorTile);
            }

            return true;
        }

        /// <summary>
        /// TEMP: Place vanilla wood Table (2×1 tile-anchored), sync, then clear via the same frame path as paste tasks.
        /// Requires valid placement (solid support under both cells); click the intended anchor tile.
        /// Clear uses <see cref="SuppressTileDrop.KillTile"/> so <c>CanDrop</c> sees suppression
        /// (multi-tile drop checks can run before <c>GlobalTile.KillTile</c>).
        /// </summary>
        private static void DebugPlaceOrClearWoodTable(Point cursorTile, bool place)
        {
            int x = cursorTile.X, y = cursorTile.Y;
            if (!WorldGen.InWorld(x, y, 2))
            {
                Main.NewText("[DebugStick] cursor out of world.");
                return;
            }

            ushort tileType = TileID.Tables;
            int style = 0;
            int alternate = 0;
            if (place)
            {
                if (!WorldGen.PlaceObject(x, y, tileType, mute: false, style, alternate, random: -1, direction: -1))
                {
                    Main.NewText("[DebugStick] PlaceObject failed (need valid table placement / solid floor).");
                    return;
                }
            }
            else
            {
                SuppressTileDrop.KillTile(x, y);
            }
        }
    }
}
