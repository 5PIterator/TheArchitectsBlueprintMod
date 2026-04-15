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
	/// STENCIL WAND: ADD / REMOVE via action wheel (rectangle gestures for tile mask edits).
	/// </summary>
	public class StencilWand : ModItem
	{
		private ToolAction _mode = ToolAction.ADD;


		/// <summary>Sets wand appearance and reuse behavior for rectangle ADD/REMOVE.</summary>
		public override void SetDefaults()
		{
			// --- Hold-up wand: UseItem once per click; HoldItem drives drag (matches AddStick / blueprint pattern).

			Item.width = 30;
			Item.height = 30;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 10;
			Item.useAnimation = 10;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(gold: 2);
			Item.autoReuse = false;
		}

		/// <summary>Copies persisted wheel mode when the item stack is cloned.</summary>
		public override ModItem Clone(Item newEntity)
		{
			// --- Clone base state, then copy the stencil action mode.

			var clone = (StencilWand)base.Clone(newEntity);
			clone._mode = _mode;
			return clone;
		}

		/// <summary>Persists ADD/REMOVE wheel selection.</summary>
		public override void SaveData(TagCompound tag) => tag["mode"] = (int)_mode;

		/// <summary>Restores wheel mode when the stored value is a defined <see cref="ToolAction"/>.</summary>
		public override void LoadData(TagCompound tag)
		{
			// --- Ignore missing or invalid enum values to avoid corrupt mode state.

			if (!tag.ContainsKey("mode"))
				return;
			int raw = tag.GetInt("mode");
			if (!System.Enum.IsDefined(typeof(ToolAction), raw))
				return;
			var loaded = (ToolAction)raw;
			_mode = loaded == ToolAction.RESET ? ToolAction.ADD : loaded;
			if (_mode != ToolAction.ADD && _mode != ToolAction.REMOVE)
				_mode = ToolAction.ADD;
		}

		/// <summary>Enables alternate use to open or dismiss the stencil action wheel.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Blocks use while the shared action wheel is open or opening.</summary>
		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		/// <summary>Opens the mode wheel on alt-use (after preview cancel when needed), or dispatches ADD/REMOVE on primary use.</summary>
		public override bool? UseItem(Player player)
		{
			// --- Alt-use cancels rectangle preview, toggles wheel, or opens mode wheel; primary use runs current mode.

			if (player.whoAmI != Main.myPlayer)
				return null;

			var actionManager = ModContent.GetInstance<ToolActionManager>();
			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);

			if (player.altFunctionUse == 0)
			{
				actionManager.Handle(_mode, ToolActionKey.LeftClick, player, cursorTile);
			}
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				if (ActionWheel.IsOwnedBy(Main.myPlayer, Item.type))
					ActionWheel.CancelActive();
				else
				{
					var entries = new List<ActionWheelEntry>(2)
					{
						new(GetActionId(ToolAction.ADD), GetActionLabel(ToolAction.ADD), onSelect: () => _mode = ToolAction.ADD,
							isSelected: () => _mode == ToolAction.ADD),
						new(GetActionId(ToolAction.REMOVE), GetActionLabel(ToolAction.REMOVE), onSelect: () => _mode = ToolAction.REMOVE,
							isSelected: () => _mode == ToolAction.REMOVE)
					};
					ActionWheel.OpenNew(Main.myPlayer, Item.type, entries);
				}
			}

			return true;
		}

		/// <summary>Forwards left drag and release; clears stale rectangle preview while the wheel is open.</summary>
		public override void HoldItem(Player player)
		{
			// --- While the wheel is visible, drop rectangle preview; otherwise use an AddStick-style hold chain.

			if (player.whoAmI != Main.myPlayer)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(_mode, ToolActionKey.RightClick, player, cursorTile);
			else if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(_mode, ToolActionKey.LeftHold, player, cursorTile);
			else if (!Main.mouseLeft && Main.mouseLeftRelease)
				actionManager.Handle(_mode, ToolActionKey.LeftRelease, player, cursorTile);
		}
	}
}
