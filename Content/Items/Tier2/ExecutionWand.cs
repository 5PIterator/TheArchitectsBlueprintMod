using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.ArchitectMode;
using static TheArchitectsBlueprintMod.Core.ToolActionManager;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// EXECUTION WAND: PASTE / RESET only (tier-2 paste branch; projection wand keeps COPY). Default is RESET so alt-use can open the mode wheel; pick PASTE to paste, then right-click is RST-equivalent.
	/// </summary>
	public class ExecutionWand : ModItem
	{
		private ToolAction _mode = ToolAction.PASTE;

		public override void SetDefaults()
		{
			Item.width = 30;
			Item.height = 30;
			Item.useTime = 15;
			Item.useAnimation = 15;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(gold: 2);
			Item.autoReuse = false;
		}

		public override ModItem Clone(Item newEntity)
		{
			var clone = (ExecutionWand)base.Clone(newEntity);
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
			_mode = loaded is ToolAction.PASTE or ToolAction.RESET ? loaded : ToolAction.PASTE;
		}

		public override bool AltFunctionUse(Player player) => true;

		public override bool CanUseItem(Player player) =>
			!ArchitectModeGuards.SuppressHeldToolInput(player)
			&& !ActionWheel.IsActive
			&& !ActionWheel.IsPendingOpen;

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			if (ArchitectModeGuards.SuppressHeldToolInput(player))
				return null;

			var actionManager = ModContent.GetInstance<ToolActionManager>();
			if (!ArchitectMapInput.TryGetCursorTile(player, out Point cursorTile))
				return true;

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

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient<PasteStick>(1)
				.AddIngredient(ItemID.Obsidian, 1)
				.AddIngredient(ItemID.FlaskofFire, 1)
				.AddIngredient(ItemID.Wood, 25)
				//.AddIngredient(5710, 25) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.LivingLoom)
				.Register();
		}
	}
}
