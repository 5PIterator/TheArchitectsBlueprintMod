using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// ARCHITECT'S STENCIL: all-in-one tool combining selection, editing, and projection actions.
	/// </summary>
	public class ArchitectsStencil : ModItem
	{
		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		public override void SetDefaults()
		{
			Item.width = 32;
			Item.height = 32;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 8;
			Item.useAnimation = 8;
			Item.rare = ItemRarityID.Yellow;
			Item.value = Item.buyPrice(gold: 5);
			Item.autoReuse = true;
		}

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			WorldGen.PlaceTile(
				cursorTile.X,
				cursorTile.Y,
				TileID.Dirt,
				mute: true,
				forced: true,
				plr: player.whoAmI,
				style: 1
			);
			//pastingSystem.TryPlaceSingleTile(TileID.JungleGrass, cursorTile, player, forcePaste: false);
			return true;
		}
	}
}
