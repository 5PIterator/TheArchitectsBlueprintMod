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
	/// PROJECTION WAND: bundles Copy, Cut, and Paste actions.
	/// Uses an on-screen action wheel to swap the current mode.
	/// RESET is a center toggle in the wheel and modifies what left click does.
	/// </summary>
	public class ProjectionWand : ModItem
	{
		private ToolAction _mode = ToolAction.COPY;
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
			Item.channel = true;
			Item.shootSpeed = 10f;
			Item.UseSound = SoundID.Item64;
			Item.ChangePlayerDirectionOnShoot = true;// Added by tML
		}

		public override bool ConsumeItem(Player player) => false;

		public override bool AltFunctionUse(Player player) => true;

		public override ModItem Clone(Item newEntity)
		{
			var clone = (ProjectionWand)base.Clone(newEntity);
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
				int raw = tag.GetInt("mode");
				// Legacy compatibility: RESET used to be a standalone ToolAction mode (raw=3).
				// ProjectionWand now models RESET as a modifier, so fall back to COPY.
				_mode = raw == 3 ? ToolAction.COPY : (ToolAction)raw;
			}
		}

		public override bool CanUseItem(Player player)
		{
			return !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;
		}

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (player.altFunctionUse == 2)
			{
				var entries = new List<ActionWheelEntry>(3)
				{
					new(
						GetActionId(ToolAction.COPY),
						() => _resetArmed ? "CLEAR COPY" : GetActionLabel(ToolAction.COPY),
						onSelect: () => _mode = ToolAction.COPY,
						isSelected: () => _mode == ToolAction.COPY
					),
					new(
						GetActionId(ToolAction.CUT),
						() => _resetArmed ? "CLEAR CUT" : GetActionLabel(ToolAction.CUT),
						onSelect: () => _mode = ToolAction.CUT,
						isSelected: () => _mode == ToolAction.CUT
					),
					new(
						GetActionId(ToolAction.PASTE),
						() => _resetArmed ? "STOP PASTE" : GetActionLabel(ToolAction.PASTE),
						onSelect: () => _mode = ToolAction.PASTE,
						isSelected: () => _mode == ToolAction.PASTE
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
				return true;
			}

			// Only execute on the click edge.
			// HoldItem() can run before UseItem() in the same tick, so _wasLeftDown is not reliable here.
			if (!Main.mouseLeftRelease)
			{
				return null;
			}

			_gestureActive = false;

			if (_resetArmed)
			{
				actionManager.Handle(_mode, ToolActionKey.LeftClick, player, cursorTile, ToolActionModifier.Reset);
				return null;
			}

			actionManager.Handle(_mode, ToolActionKey.LeftClick, player, cursorTile);
			if (_mode == ToolAction.COPY || _mode == ToolAction.CUT)
			{
				_gestureActive = true;
				_gestureAction = _mode;
			}

			return null;
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

			if (_gestureActive && (_gestureAction == ToolAction.COPY || _gestureAction == ToolAction.CUT))
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
