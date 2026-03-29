using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core;
using static ArchitectsStencilMod.Core.ToolActionManager;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// BLUEPRINT WAND: interactive Blueprint bounds editing (camera-mode-like).
	/// World input mapping:
	/// - Left click: SET. If an existing Blueprint handle is hit, begin dragging it; otherwise begin a new bounds gesture.
	/// - Left hold: drag the grabbed element (pins, edges, corners, or inside-to-move).
	/// - Left release: end drag.
	/// - Right click: RESET+SET (clears the Blueprint).
	/// </summary>
	public class BlueprintWand : ModItem
	{
		private bool _wasLeftDown;
		private bool _gestureActive;

		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		public override void SetDefaults()
		{
			Item.width = 30;
			Item.height = 30;
			Item.useStyle = ItemUseStyleID.Shoot;
			Item.useTime = 10;
			Item.useAnimation = 10;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(gold: 1);
			Item.autoReuse = false; // UseItem fires once per click; hold/release handled in HoldItem
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
				_gestureActive = false;
				actionManager.Handle(ToolAction.SET, ToolActionKey.RightClick, player, cursorTile, ToolActionModifier.Reset);
				return true;
			}

			// Only execute on the click edge.
			// HoldItem() can run before UseItem() in the same tick, so _wasLeftDown is not reliable here.
			if (!Main.mouseLeftRelease)
			{
				return true;
			}

			_gestureActive = false;
			actionManager.Handle(ToolAction.SET, ToolActionKey.LeftClick, player, cursorTile);
			_gestureActive = true;

			return true;
		}

		public override void HoldItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return;

			bool leftDown = Main.mouseLeft;
			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (_gestureActive)
			{
				if (leftDown && _wasLeftDown)
					actionManager.Handle(ToolAction.SET, ToolActionKey.LeftHold, player, cursorTile);
				else if (!leftDown && _wasLeftDown)
				{
					actionManager.Handle(ToolAction.SET, ToolActionKey.LeftRelease, player, cursorTile);
					_gestureActive = false;
				}
			}

			_wasLeftDown = leftDown;
		}
	}
}
