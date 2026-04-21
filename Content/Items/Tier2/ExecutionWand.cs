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
	/// EXECUTION WAND: PASTE / RESET only (tier-2 paste branch; projection wand keeps COPY).
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

		public override void HoldItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer || ActionWheel.IsActive || ActionWheel.IsPendingOpen)
				return;

			Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
			var actionManager = ModContent.GetInstance<ToolActionManager>();

			if (_mode == ToolAction.PASTE && Main.mouseLeft && Main.mouseRight)
				actionManager.Handle(ToolAction.PASTE, ToolActionKey.RightClick, player, cursorTile);
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
