using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// ARCHITECT'S BLUEPRINT: late-game upgrade from Shimmering Architect's Hand.
	/// </summary>
	public class ArchitectsBlueprint : ModItem
	{
		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 28;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 20;
			Item.useAnimation = 20;
			Item.rare = ItemRarityID.Red;
			Item.value = Item.buyPrice(gold: 10);
			Item.UseSound = SoundID.Item4;
		}
	}
}
