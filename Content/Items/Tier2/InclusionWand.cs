using System.Collections.Generic;
using Microsoft.Xna.Framework;
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
	/// INCLUSION WAND: INCLUDE / EXCLUDE via action wheel (rectangle gestures for tile mask edits).
	/// </summary>
	public class InclusionWand : ModItem
	{
		private ToolAction _mode = ToolAction.INCLUDE;

		public override void SetDefaults()
		{
			Item.width = 30;
			Item.height = 30;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 10;
			Item.useAnimation = 10;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(gold: 2);
			Item.autoReuse = false;
		}

		public override ModItem Clone(Item newEntity)
		{
			var clone = (InclusionWand)base.Clone(newEntity);
			clone._mode = _mode;
			return clone;
		}

		public override void SaveData(TagCompound tag) => tag["mode"] = (int)_mode;

		public override void LoadData(TagCompound tag)
		{
			if (!tag.ContainsKey("mode"))
				return;
			int raw = tag.GetInt("mode");
			if (!System.Enum.IsDefined(typeof(ToolAction), raw))
				return;
			var loaded = (ToolAction)raw;
			_mode = loaded == ToolAction.RESET ? ToolAction.INCLUDE : loaded;
			if (_mode != ToolAction.INCLUDE && _mode != ToolAction.EXCLUDE)
				_mode = ToolAction.INCLUDE;
		}

		public override bool AltFunctionUse(Player player) => true;

		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		public override bool? UseItem(Player player)
		{
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
					new(GetActionId(ToolAction.INCLUDE), GetActionLabel(ToolAction.INCLUDE), onSelect: () => _mode = ToolAction.INCLUDE,
						isSelected: () => _mode == ToolAction.INCLUDE),
					new(GetActionId(ToolAction.EXCLUDE), GetActionLabel(ToolAction.EXCLUDE), onSelect: () => _mode = ToolAction.EXCLUDE,
						isSelected: () => _mode == ToolAction.EXCLUDE)
				};
					ActionWheel.OpenNew(Main.myPlayer, Item.type, entries);
				}
			}

			return true;
		}

		public override void HoldItem(Player player)
		{
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

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient<IncludeStick>(1)
				.AddIngredient<ExcludeStick>(1)
				.AddIngredient(ItemID.AmberStaff, 1)
				.AddIngredient(ItemID.Wood, 25)
				//.AddIngredient(5710, 25) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}
