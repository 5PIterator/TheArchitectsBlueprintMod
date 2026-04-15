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
	/// BLUEPRINT WAND: PIN / SET / RESET via action wheel. SET uses ToolActionManager fallback (PIN when no blueprint).
	/// </summary>
	public class BlueprintWand : ModItem
	{
		private ToolAction _mode = ToolAction.PIN;


		/// <summary>Sets wand appearance, use style, and economy stats for a tier-2 blueprint tool.</summary>
		public override void SetDefaults()
		{
			// --- Configure a non-consuming hold-up wand with modest use speed.

			Item.width = 30;
			Item.height = 30;
			Item.useStyle = ItemUseStyleID.Shoot;
			Item.useTime = 10;
			Item.useAnimation = 10;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(gold: 1);
			Item.autoReuse = false;
		}

		/// <summary>Copies persisted wheel mode when the item stack is cloned.</summary>
		public override ModItem Clone(Item newEntity)
		{
			// --- Clone base state, then copy the selected blueprint action mode.

			var clone = (BlueprintWand)base.Clone(newEntity);
			clone._mode = _mode;
			return clone;
		}

		/// <summary>Persists the current PIN/SET/RESET wheel selection.</summary>
		public override void SaveData(TagCompound tag) => tag["mode"] = (int)_mode;

		/// <summary>Restores the wheel mode from save data when present.</summary>
		public override void LoadData(TagCompound tag)
		{
			// --- Read stored enum int when the tag exists (older saves may omit it).

			if (tag.ContainsKey("mode"))
				_mode = (ToolAction)tag.GetInt("mode");
		}

		/// <summary>Enables alternate use to open or dismiss the blueprint action wheel.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Blocks weapon use while the shared action wheel is open or opening.</summary>
		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		/// <summary>Opens the mode wheel on alt-use, or dispatches the chosen PIN/SET/RESET action on left release.</summary>
		public override bool? UseItem(Player player)
		{
			// --- Alt-use toggles wheel or cancels owned wheel; primary use runs current mode on mouse release.

			if (player.whoAmI != Main.myPlayer)
				return null;

			var actionManager = ModContent.GetInstance<ToolActionManager>();
			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);

			// If the player is using the primary function (left-click), trigger the selected action.
			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(_mode, ToolActionKey.LeftClick, player, cursorTile);
			}
			// If the player is using the alternate function (right-click), toggle the action wheel or cancel if already owned.
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				if (ActionWheel.IsOwnedBy(Main.myPlayer, Item.type))
					ActionWheel.CancelActive();
				else
				{
					var entries = new List<ActionWheelEntry>(3)
					{
						new(GetActionId(ToolAction.PIN), GetActionLabel(ToolAction.PIN), onSelect: () => _mode = ToolAction.PIN,
							isSelected: () => _mode == ToolAction.PIN),
						new(GetActionId(ToolAction.SET), GetActionLabel(ToolAction.SET), onSelect: () => _mode = ToolAction.SET,
							isSelected: () => _mode == ToolAction.SET),
						new(GetActionId(ToolAction.RESET), GetActionLabel(ToolAction.RESET), onSelect: () => _mode = ToolAction.RESET,
							isSelected: () => _mode == ToolAction.RESET)
					};
					ActionWheel.OpenNew(Main.myPlayer, Item.type, entries);
				}
			}
			return true;
		}

		/// <summary>Continues PIN/SET drag gestures after a qualifying UseItem, pausing while the wheel is active.</summary>
		public override void HoldItem(Player player)
		{
			// --- When wheel is up, freeze gesture state; else forward hold/release for active blueprint modes.

			if (player.whoAmI != Main.myPlayer)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			// If both left and right buttons are down, emit a right-click cancel event (for rectangle cancellation).
			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(_mode, ToolActionKey.RightClick, player, cursorTile);
			// If the left button is held and was also down in the previous tick, emit
			if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(_mode, ToolActionKey.LeftHold, player, cursorTile);
			// If the left button was released (transitioned from down to up), emit a release event.
			else if (!Main.mouseLeft && Main.mouseLeftRelease)
				actionManager.Handle(_mode, ToolActionKey.LeftRelease, player, cursorTile);
		}
	}
}
