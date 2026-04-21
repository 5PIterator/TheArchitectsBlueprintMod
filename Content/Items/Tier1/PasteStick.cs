using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core;
using static ArchitectsStencilMod.Core.ToolActionManager;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// PASTE STICK: starts pasting at a projection with left-click and cancels an active paste with right-click (alt-use).
	/// </summary>
	public class PasteStick : ModItem
	{
		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		/// <summary>Configures size, use timing, and rarity for the paste stick.</summary>
		public override void SetDefaults()
		{
			// --- Apply default item stats suitable for a tier-1 paste trigger.

			Item.width = 32;
			Item.height = 32;
			Item.useTime = 15;
			Item.useAnimation = 15;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.value = Item.buyPrice(silver: 1);
			Item.rare = ItemRarityID.Blue;
			Item.autoReuse = false; // UseItem fires once per click
		}

		/// <summary>Enables alternate use so right-click maps to paste cancel.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Forwards left-click to start paste and cancels paste only when both buttons are pressed.</summary>
		public override bool? UseItem(Player player)
		{
			// --- Ignore remote players; resolve tile under mouse; dispatch PASTE left vs right.

			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If the player is using the primary function (left-click), trigger a paste action.
			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(ToolAction.PASTE, ToolActionKey.LeftClick, player, cursorTile);
			}
			// If the player is using the alternate function (right-click), only trigger cancel when left is not also held.
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				// Right-click alone does not cancel paste; cancellation is handled by the left+right hold gesture.
			}

			return true;
		}

		/// <summary>Emits a paste cancel event only when both left and right buttons are held.</summary>
		public override void HoldItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (Main.mouseLeft && Main.mouseRight)
			{
				actionManager.Handle(ToolAction.PASTE, ToolActionKey.RightClick, player, cursorTile);
			}
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Emerald, 1)
				.AddIngredient(ItemID.Wood, 5)
				//.AddIngredient(5710, 5) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}
