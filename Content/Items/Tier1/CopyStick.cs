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
	/// COPY STICK: creates and moves a Copy Projection of the active Blueprint.
	/// </summary>
	public class CopyStick : ModItem
	{
		private bool _wasLeftDown;

		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		public override void SetDefaults()
		{
			Item.width = 32;
			Item.height = 32;
			Item.useTime = 15;
			Item.useAnimation = 15;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.value = Item.buyPrice(silver: 1);
			Item.rare = ItemRarityID.Blue;
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
				actionManager.Handle(ToolAction.COPY, ToolActionKey.RightClick, player, cursorTile, ToolActionModifier.Reset);
			}
			else
			{
				actionManager.Handle(ToolAction.COPY, ToolActionKey.LeftClick, player, cursorTile);
			}

			return true;
		}

		public override void HoldItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return;

			bool leftDown = Main.mouseLeft;
			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (leftDown && _wasLeftDown)
				actionManager.Handle(ToolAction.COPY, ToolActionKey.LeftHold, player, cursorTile);
			else if (!leftDown && _wasLeftDown)
				actionManager.Handle(ToolAction.COPY, ToolActionKey.LeftRelease, player, cursorTile);

			_wasLeftDown = leftDown;
		}
	}
}
