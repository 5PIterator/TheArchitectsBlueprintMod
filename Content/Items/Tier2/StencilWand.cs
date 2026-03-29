using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using ArchitectsStencilMod.Core;
using static ArchitectsStencilMod.Core.ToolActionManager;
using ArchitectsStencilMod.Utils;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// STENCIL WAND: combines Add and Remove behaviors.
	/// Uses an on-screen action wheel to swap the current mode.
	/// </summary>
	public class StencilWand : ModItem
	{
		private ToolAction _mode = ToolAction.ADD;
		private bool _resetArmed;

		private bool _wasLeftDown;
		private bool _gestureActive;
		private ToolAction _gestureAction;

		// The Display Name and Tooltip of this item can be edited in the 'Localization/en-US_Mods.ArchitectsStencilMod.hjson' file.
		public override void SetDefaults()
		{
			Item.width = 30;
			Item.height = 30;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 10;
			Item.useAnimation = 10;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(gold: 2);
			Item.autoReuse = true;
		}

		public override bool ConsumeItem(Player player) => false;

		public override ModItem Clone(Item newEntity)
		{
			var clone = (StencilWand)base.Clone(newEntity);
			clone._mode = _mode;
			clone._resetArmed = _resetArmed;
			return clone;
		}

		public override void SaveData(TagCompound tag)
		{
			tag["mode"] = (int)_mode;
		}

		public override void LoadData(TagCompound tag)
		{
			if (tag.ContainsKey("mode"))
			{
				_mode = (ToolAction)tag.GetInt("mode");
			}
		}

		public override bool AltFunctionUse(Player player) => true;

		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			if (player.altFunctionUse != 2)
			{
				// Only execute on the click edge.
				// HoldItem() can run before UseItem() in the same tick, so _wasLeftDown is not reliable here.
				if (!Main.mouseLeftRelease)
				{
					return null;
				}

				var actionManager = ModContent.GetInstance<ToolActionManager>();
				Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);

				_gestureActive = false;
				if (_resetArmed)
				{
					actionManager.Handle(_mode, ToolActionKey.LeftClick, player, cursorTile, ToolActionModifier.Reset);
					return null;
				}

				actionManager.Handle(_mode, ToolActionKey.LeftClick, player, cursorTile);
				_gestureActive = true;
				_gestureAction = _mode;
				return null;
			}

			var selectionSystem = ModContent.GetInstance<SelectionSystem>();
			if (selectionSystem.TryGetPreview(player, out _, out _, out _))
			{
				var actionManager = ModContent.GetInstance<ToolActionManager>();
				actionManager.Handle(_mode, ToolActionKey.RightClick, player, Terraria.Utils.ToTileCoordinates(Main.MouseWorld));
				return true;
			}

			if (ActionWheel.IsOwnedBy(Main.myPlayer, Item.type))
			{
				ActionWheel.CancelActive();
			}
			else
			{
				Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
				var actionManager = ModContent.GetInstance<ToolActionManager>();

				var entries = new List<ActionWheelEntry>(2)
				{
					new(
						GetActionId(ToolAction.ADD),
						() => _resetArmed ? "SELECT ALL" : GetActionLabel(ToolAction.ADD),
						onSelect: () =>
						{
							_mode = ToolAction.ADD;
						},
						isSelected: () => _mode == ToolAction.ADD
					),
					new(
						GetActionId(ToolAction.REMOVE),
						() => _resetArmed ? "DESELECT ALL" : GetActionLabel(ToolAction.REMOVE),
						onSelect: () =>
						{
							_mode = ToolAction.REMOVE;
						},
						isSelected: () => _mode == ToolAction.REMOVE
					)
				};

				var center = new ActionWheelEntry(
					id: "reset",
					labelGetter: () => "RESET",
					onSelect: () => _resetArmed = !_resetArmed,
					isSelected: () => _resetArmed
				);

				ActionWheel.OpenNew(
					ownerPlayer: Main.myPlayer,
					ownerHeldItemType: Item.type,
					entries: entries,
					centerEntry: center
				);
			}
			return true;
		}

		public override void HoldItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return;

			if (ActionWheel.IsActive || ActionWheel.IsPendingOpen)
			{
				_wasLeftDown = Main.mouseLeft;
				_gestureActive = false;
				return;
			}

			bool leftDown = Main.mouseLeft;
			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (_gestureActive)
			{
				if (leftDown && _wasLeftDown)
					actionManager.Handle(_gestureAction, ToolActionKey.LeftHold, player, cursorTile);
				else if (!leftDown && _wasLeftDown)
				{
					actionManager.Handle(_gestureAction, ToolActionKey.LeftRelease, player, cursorTile);
					_gestureActive = false;
				}
			}

			_wasLeftDown = leftDown;
		}
	}
}

