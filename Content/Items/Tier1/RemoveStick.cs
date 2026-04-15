using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core;
using static ArchitectsStencilMod.Core.ToolActionManager;
using ArchitectsStencilMod.Utils;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// REMOVE STICK: deselects tiles within the current Blueprint.
	/// Usage:
	/// - Left click + drag: preview a rectangle
	/// - Left release: apply (tiles in rectangle become deselected)
	/// - Right click: cancel the current rectangle gesture
	/// </summary>
	public class RemoveStick : ModItem
	{

		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		/// <summary>Configures size, use timing, and rarity for the remove-selection stick.</summary>
		public override void SetDefaults()
		{
			// --- Apply default item stats suitable for a tier-1 selection tool.

			Item.width = 32;
			Item.height = 32;
			Item.useTime = 15;
			Item.useAnimation = 15;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.value = Item.buyPrice(silver: 1);
			Item.rare = ItemRarityID.Blue;
			Item.autoReuse = false; // UseItem fires once per click; hold/release handled in HoldItem
		}

		/// <summary>Enables alternate use so right-click maps to cancel rectangle preview.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Forwards a discrete click to REMOVE (left) or rectangle cancel (right alt-use).</summary>
		public override bool? UseItem(Player player)
		{
			// --- Ignore remote players; resolve tile under mouse; dispatch REMOVE click vs right-cancel.

			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If the player is using the primary function (left-click), trigger a remove action.
			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(ToolAction.REMOVE, ToolActionKey.LeftClick, player, cursorTile);
			}
			// If the player is using the alternate function (right-click), trigger a cancel action.
			// Note: if the left button is still held, ignore the right-click to prevent interference with the hold/drag gesture.
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				actionManager.Handle(ToolAction.REMOVE, ToolActionKey.RightClick, player, cursorTile);
			}

			return true;
		}

		/// <summary>Emits REMOVE hold and release events while the left mouse button is dragged.</summary>
		public override void HoldItem(Player player)
		{
			// --- Track left-button drag state and cancel the preview with right-click.

			if (player.whoAmI != Main.myPlayer)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If both left and right buttons are down, emit a right-click cancel event (for rectangle cancellation).
			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(ToolAction.REMOVE, ToolActionKey.RightClick, player, cursorTile);
			// If the left button is held and was also down in the previous tick, emit a hold event.
			else if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(ToolAction.REMOVE, ToolActionKey.LeftHold, player, cursorTile);
			// If the left button was released (transitioned from down to up), emit a release event.
			else if (!Main.mouseLeft && Main.mouseLeftRelease)
				actionManager.Handle(ToolAction.REMOVE, ToolActionKey.LeftRelease, player, cursorTile);
		}
	}
}
