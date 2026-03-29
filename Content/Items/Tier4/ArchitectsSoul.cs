using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// ARCHITECT'S SOUL: late-game upgrade that will eventually unlock the full GUI-driven experience.
	/// </summary>
	public class ArchitectsSoul : ModItem
	{
		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
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
