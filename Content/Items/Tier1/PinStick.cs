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
	/// PIN STICK: pins a new Blueprint bounds selection.
	/// Usage:
	/// - Left click: place Pin B and start a fresh Blueprint.
	/// - Left hold: drag Pin A to define the bounds.
	/// - Left release: finalize pins.
	/// - Right click: RESET+PIN (clears the Blueprint).
	///
	/// Note: PIN always starts fresh; use <see cref="BlueprintWand"/> for SET-style post-editing.
	/// </summary>
	public class PinStick : ModItem
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
			Item.UseSound = SoundID.Item1;
			Item.autoReuse = false; // UseItem fires once per click; hold/release handled in HoldItem
		}

		public override bool AltFunctionUse(Player player) => true;

		// Pin Stick cannot be right-clicked in the inventory.
		public override bool CanRightClick() => false;

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (player.altFunctionUse == 2)
			{
				actionManager.Handle(ToolAction.PIN, ToolActionKey.RightClick, player, cursorTile, ToolActionModifier.Reset);
			}
			else
			{
				actionManager.Handle(ToolAction.PIN, ToolActionKey.LeftClick, player, cursorTile);
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
				actionManager.Handle(ToolAction.PIN, ToolActionKey.LeftHold, player, cursorTile);
			else if (!leftDown && _wasLeftDown)
				actionManager.Handle(ToolAction.PIN, ToolActionKey.LeftRelease, player, cursorTile);

			_wasLeftDown = leftDown;
		}
	}
}

