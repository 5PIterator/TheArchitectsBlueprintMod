using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using static TheArchitectsBlueprintMod.Core.ToolActionManager;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// EXCLUDE STICK: excludes tiles from the current Selection.
	/// Usage:
	/// - Left click + drag: preview a rectangle
	/// - Left release: apply (tiles in rectangle become excluded)
	/// - Right click: cancel the current rectangle gesture
	/// </summary>
	public class ExcludeStick : ModItem
	{
		private bool _leftDragSeenDown;

		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.TheArchitectsBlueprintMod.hjson' file.
		/// <summary>Configures size, use timing, and rarity for the exclude-selection stick.</summary>
		public override void SetDefaults()
		{
			// --- Apply default item stats suitable for a tier-1 selection tool.

			Item.width = 32;
			Item.height = 32;
			Item.useTime = 15;
			Item.useAnimation = 15;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.value = Item.buyPrice(silver: 1);
			Item.rare = ItemRarityID.Blue;
			Item.autoReuse = false; // UseItem fires once per click; hold/release handled in HoldItem
		}

		/// <summary>Enables alternate use so right-click maps to cancel rectangle preview.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Forwards a discrete click to EXCLUDE (left) or rectangle cancel (right alt-use).</summary>
		public override bool? UseItem(Player player)
		{
			// --- Ignore remote players; resolve tile under mouse; dispatch EXCLUDE click vs right-cancel.

			if (player.whoAmI != Main.myPlayer)
				return null;

			if (!ArchitectMapInput.TryGetCursorTile(player, out Point cursorTile))
				return true;

			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If the player is using the primary function (left-click), trigger an exclude action.
			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(ToolAction.EXCLUDE, ToolActionKey.LeftClick, player, cursorTile);
			}
			// If the player is using the alternate function (right-click), trigger a cancel action.
			// Note: if the left button is still held, ignore the right-click to prevent interference with the hold/drag gesture.
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				actionManager.Handle(ToolAction.EXCLUDE, ToolActionKey.RightClick, player, cursorTile);
			}

			return true;
		}

		/// <summary>Emits EXCLUDE hold and release events while the left mouse button is dragged.</summary>
		public override void HoldItem(Player player)
		{
			// --- Track left-button drag state and cancel the preview with right-click.

			if (player.whoAmI != Main.myPlayer)
				return;

			if (!ArchitectMapInput.TryGetCursorTile(player, out Point cursorTile))
				return;

			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If both left and right buttons are down, emit a right-click cancel event (for rectangle cancellation).
			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(ToolAction.EXCLUDE, ToolActionKey.RightClick, player, cursorTile);
			// If the left button is held and was also down in the previous tick, emit a hold event.
			else if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(ToolAction.EXCLUDE, ToolActionKey.LeftHold, player, cursorTile);
			else if (_leftDragSeenDown && !Main.mouseLeft)
			{
				actionManager.Handle(ToolAction.EXCLUDE, ToolActionKey.LeftRelease, player, cursorTile);
				_leftDragSeenDown = false;
			}

			if (Main.mouseLeft)
				_leftDragSeenDown = true;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(ItemID.Amber, 1)
				.AddIngredient(ItemID.Wood, 5)
				//.AddIngredient(5710, 5) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}
