using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsBlueprintMod.Core;
using static ArchitectsBlueprintMod.Core.ToolActionManager;

namespace ArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// PASTE STICK: left-click starts paste on a projection; right-click (alt-use) runs the same reset/stop path as RST (stops pastes, removes ghost, may clear in-bounds selection).
	/// </summary>
	public class PasteStick : ModItem
	{
		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsBlueprintMod.hjson' file.
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

		/// <summary>Enables alternate use for right-hand reset/stop (RST-equivalent) behavior.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Left: start/retry paste. Right: PASTE action reset (stops in-flight pastes, then RST hit-test.</summary>
		public override bool? UseItem(Player player)
		{
			// --- Ignore remote players; resolve tile under mouse; dispatch PASTE left vs right.

			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (player.altFunctionUse == 0)
				actionManager.Handle(ToolAction.PASTE, ToolActionKey.LeftClick, player, cursorTile);
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
				actionManager.Handle(ToolAction.PASTE, ToolActionKey.RightClick, player, cursorTile);

			return true;
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
