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
	/// INCLUDE STICK: includes tiles back into the current Selection.
	/// Usage:
	/// - Left click + drag: preview a rectangle
	/// - Left release: apply (tiles in rectangle become included)
	/// - Right click: cancel the current rectangle gesture
	/// </summary>
	public class IncludeStick : ModItem
	{
		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		/// <summary>Configures size, use timing, and rarity for the include-selection stick.</summary>
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

		/// <summary>Forwards a discrete click to INCLUDE (left) or rectangle cancel (right alt-use).</summary>
		public override bool? UseItem(Player player)
		{
			// --- Ignore remote players; resolve tile under mouse; dispatch INCLUDE click vs right-cancel.

			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If the player is using the item normally (left-click), trigger an INCLUDE action.
			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(ToolAction.INCLUDE, ToolActionKey.LeftClick, player, cursorTile);
			}
			// If the player is using the alternate function (right-click), trigger a cancel action.
			// Note: if the left button is still held, ignore the right-click to prevent interference with the hold/drag gesture.
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				actionManager.Handle(ToolAction.INCLUDE, ToolActionKey.RightClick, player, cursorTile);
			}

			return true;
		}

		/// <summary>Emits INCLUDE hold and release events while the left mouse button is dragged.</summary>
		public override void HoldItem(Player player)
		{
			// --- Track left-button edge transitions to synthesize hold/release for rectangle editing.

			if (player.whoAmI != Main.myPlayer)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If both left and right buttons are down, emit a right-click cancel event (for rectangle cancellation).
			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(ToolAction.INCLUDE, ToolActionKey.RightClick, player, cursorTile);
			// If the left button is held and was also down in the previous tick, emit a hold event.
			else if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(ToolAction.INCLUDE, ToolActionKey.LeftHold, player, cursorTile);
			// If the left button was released (transitioned from down to up), emit a release event.
			else if (!Main.mouseLeft && Main.mouseLeftRelease)
				actionManager.Handle(ToolAction.INCLUDE, ToolActionKey.LeftRelease, player, cursorTile);
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Topaz, 1)
				.AddIngredient(ItemID.Wood, 5)
				//.AddIngredient(5710, 5) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}
