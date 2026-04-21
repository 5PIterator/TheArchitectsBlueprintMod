using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Systems;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>Crafting core for Architect's Stencil; crafted at an evil altar.</summary>
	public class BlueprintCore : ModItem
	{
		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 28;
			Item.rare = ItemRarityID.LightRed;
			Item.value = Item.buyPrice(gold: 5);
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.LargeDiamond, 1)
				.AddIngredient(ItemID.LargeAmethyst, 1)
				.AddIngredient(ItemID.LargeEmerald, 1)
				.AddIngredient(ItemID.LargeSapphire, 1)
				.AddIngredient(ItemID.LargeRuby, 1)
				.AddIngredient(ItemID.LargeTopaz, 1)
				.AddIngredient(ItemID.LargeAmber, 1)
				.AddRecipeGroup(RecipeGroup.recipeGroupIDs[ArchitectRecipeGroups.EvilBossSummonGroupName], 1)
				.AddIngredient(ItemID.FallenStar, 50) // Star Cloud (1.4.9+: replace with mod Star Cloud when available)
				.AddIngredient(ItemID.Wood, 25)
				//.AddIngredient(5710, 25) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.DemonAltar)
				.Register();
		}
	}
}
