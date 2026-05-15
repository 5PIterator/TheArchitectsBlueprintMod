using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using ArchitectsBlueprintMod.Core;
using static ArchitectsBlueprintMod.Core.ToolActionManager;
using ArchitectsBlueprintMod.Utils;

namespace ArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// PROJECTION WAND: COPY / PASTE / RESET via action wheel. Hold/release only for COPY.
	/// </summary>
	public class ProjectionWand : ModItem
	{
		private ToolAction _mode = ToolAction.COPY;
		private bool _leftDragSeenDown;


		/// <summary>Sets channeling wand defaults suited to dragging copy projections.</summary>
		public override void SetDefaults()
		{
			// --- Configure a channeled hold-up wand with audio and direction snap on use.

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
			Item.ChangePlayerDirectionOnShoot = true;
		}

		/// <summary>Copies persisted wheel mode when the item stack is cloned.</summary>
		public override ModItem Clone(Item newEntity)
		{
			// --- Clone base state, then copy the selected projection action mode.

			var clone = (ProjectionWand)base.Clone(newEntity);
			clone._mode = _mode;
			return clone;
		}

		/// <summary>Persists COPY/PASTE/RESET wheel selection.</summary>
		public override void SaveData(TagCompound tag) => tag["mode"] = (int)_mode;

		/// <summary>Restores wheel mode when the stored value is a defined <see cref="ToolAction"/>.</summary>
		public override void LoadData(TagCompound tag)
		{
			// --- Ignore missing or invalid enum values to avoid corrupt mode state.

			if (!tag.ContainsKey("mode"))
				return;
			int raw = tag.GetInt("mode");
			if (System.Enum.IsDefined(typeof(ToolAction), raw))
				_mode = (ToolAction)raw;
		}

		/// <summary>Enables alternate use: pick mode or (when in PASTE) RST-equivalent reset on right, or Shift+right to open the mode wheel.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Blocks use while the shared action wheel is open or opening.</summary>
		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		/// <summary>Opens the mode wheel on alt-use, or dispatches the chosen COPY/PASTE/RESET action on primary use.</summary>
		public override bool? UseItem(Player player)
		{
			// --- Alt-use toggles wheel or cancels owned wheel; primary use runs current mode.

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
					var entries = new List<ActionWheelEntry>(3)
					{
						new(GetActionId(ToolAction.COPY), GetActionLabel(ToolAction.COPY), onSelect: () => _mode = ToolAction.COPY,
							isSelected: () => _mode == ToolAction.COPY),
						new(GetActionId(ToolAction.PASTE), GetActionLabel(ToolAction.PASTE), onSelect: () => _mode = ToolAction.PASTE,
							isSelected: () => _mode == ToolAction.PASTE),
						new(GetActionId(ToolAction.RESET), GetActionLabel(ToolAction.RESET), onSelect: () => _mode = ToolAction.RESET,
							isSelected: () => _mode == ToolAction.RESET)
					};
					ActionWheel.OpenNew(Main.myPlayer, Item.type, entries);
				}
			}

			return true;
		}

		/// <summary>Forwards left drag and release for the current mode; both buttons emit right-cancel like the blueprint wand.</summary>
		public override void HoldItem(Player player)
		{
			// --- Mirror blueprint wand: dual-button cancel, then left hold vs release edge handling.

			if (player.whoAmI != Main.myPlayer || ActionWheel.IsActive || ActionWheel.IsPendingOpen)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(_mode, ToolActionKey.RightClick, player, cursorTile);
			else if (Main.mouseLeft && !Main.mouseLeftRelease)
				actionManager.Handle(_mode, ToolActionKey.LeftHold, player, cursorTile);
			else if (_leftDragSeenDown && !Main.mouseLeft)
			{
				actionManager.Handle(_mode, ToolActionKey.LeftRelease, player, cursorTile);
				_leftDragSeenDown = false;
			}

			if (Main.mouseLeft)
				_leftDragSeenDown = true;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient<CopyStick>(1)
				.AddIngredient(ItemID.Ruby, 1)
				.AddIngredient(ItemID.AcornAxe, 1) // Axe of Regrowth
				.AddIngredient(ItemID.Wood, 25)
				//.AddIngredient(5710, 25) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}
