using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// ARCHITECT'S HAND: late-game accessory; Shimmer transforms into <see cref="ArchitectsBlueprint"/>.
	/// </summary>
	public class ArchitectsHand : ModItem
	{
		public override void SetStaticDefaults()
		{
			ItemID.Sets.ShimmerTransformToItem[Type] = ModContent.ItemType<ArchitectsBlueprint>();
		}

		public override void SetDefaults()
		{
			Item.width = 32;
			Item.height = 32;
			Item.accessory = true;
			Item.rare = ItemRarityID.Yellow;
			Item.value = Item.buyPrice(gold: 15);
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient<ArchitectsStencil>(1)
				.AddIngredient(ItemID.HandOfCreation, 1)
				.AddIngredient(ItemID.Wood, 100)
				//.AddIngredient(5710, 100) // Feywood (not in 1.4.9 ItemID)
				.AddIngredient(ItemID.ShimmerBlock, 100) // Aetherium Block (1.4.9+: replace with mod block when available)
				.AddTile(TileID.Hellforge)
				.Register();
		}
	}
}
