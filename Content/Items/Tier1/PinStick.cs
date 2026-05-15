using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using static TheArchitectsBlueprintMod.Core.ToolActionManager;

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// PIN STICK: pins a new Selection bounds.
	/// Usage:
	/// - Left click: place Pin B and start a fresh Selection.
	/// - Left hold: drag Pin A to define the bounds.
	/// - Left release: finalize pins.
	/// - Right click: cancel the current PIN gesture and remove the active selection.
	///
	/// Note: PIN always starts fresh; use <see cref="SelectionWand"/> for SET-style post-editing.
	/// </summary>
	public class PinStick : ModItem
	{
		private bool _leftDragSeenDown;

		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.TheArchitectsBlueprintMod.hjson' file.
		/// <summary>Configures size, use timing, sound, and rarity for the pin stick.</summary>
		public override void SetDefaults()
		{
			// --- Apply default item stats suitable for tier-1 blueprint pinning.

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

		/// <summary>Enables alternate use so right-click cancels the current PIN gesture and removes the blueprint.</summary>
		public override bool AltFunctionUse(Player player) => true;

		// Pin Stick cannot be right-clicked in the inventory.
		/// <summary>Prevents inventory quick-stack style right-click on this tool.</summary>
		public override bool CanRightClick() => false;

		/// <summary>Forwards PIN click/hold start (left) or cancel and remove the blueprint (right alt-use).</summary>
		public override bool? UseItem(Player player)
		{
			// --- Ignore remote players; map left to PIN click and right-alt to cancel and remove the blueprint.

			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If the player is using the primary function (left-click), trigger a pin action.
			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(ToolAction.PIN, ToolActionKey.LeftClick, player, cursorTile);
			}
			// If the player is using the alternate function (right-click), trigger a cancel action.
			// Note: if the left button is still held, ignore the right-click to prevent interference with the hold/drag gesture.
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				actionManager.Handle(ToolAction.PIN, ToolActionKey.RightClick, player, cursorTile);
			}

			return true;
		}

		/// <summary>Emits PIN hold and release events while the left mouse button is dragged.</summary>
		public override void HoldItem(Player player)
		{
			// --- Track left-button drag state and cancel the gesture with right-click.

			if (player.whoAmI != Main.myPlayer)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If both left and right buttons are down, emit a right-click cancel event (for rectangle cancellation).
			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(ToolAction.PIN, ToolActionKey.RightClick, player, cursorTile);
			// If the left button is held and was also down in the previous tick, emit a hold event.
			else if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(ToolAction.PIN, ToolActionKey.LeftHold, player, cursorTile);
			else if (_leftDragSeenDown && !Main.mouseLeft)
			{
				actionManager.Handle(ToolAction.PIN, ToolActionKey.LeftRelease, player, cursorTile);
				_leftDragSeenDown = false;
			}

			if (Main.mouseLeft)
				_leftDragSeenDown = true;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Diamond, 1)
				.AddIngredient(ItemID.Wood, 5)
				//.AddIngredient(5710, 5) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}

