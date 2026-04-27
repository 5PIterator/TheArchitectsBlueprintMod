using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using ArchitectsStencilMod.Core;
using static ArchitectsStencilMod.Core.ToolActionManager;
using ArchitectsStencilMod.ModUtils;

namespace ArchitectsStencilMod.Content.Items
{
	/// <summary>
	/// ARCHITECT'S STENCIL: PIN / SET / INC / EXC / COPY / PASTE / RESET in one tool via action wheel.
	/// </summary>
	public class ArchitectsStencil : ModItem
	{
		private ToolAction _mode = ToolAction.PIN;


		/// <summary>Sets endgame stencil stats; single UseItem per click, HoldItem drives drags.</summary>
		public override void SetDefaults()
		{
			// --- Fast hold-up tool: autoReuse off so LeftClick is not spammed while the button is held.

			Item.width = 32;
			Item.height = 32;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 8;
			Item.useAnimation = 8;
			Item.rare = ItemRarityID.Yellow;
			Item.value = Item.buyPrice(gold: 5);
			Item.autoReuse = false;
		}

		/// <summary>Copies persisted wheel mode when the item stack is cloned.</summary>
		public override ModItem Clone(Item newEntity)
		{
			// --- Clone base state, then copy the unified tool mode.

			var clone = (ArchitectsStencil)base.Clone(newEntity);
			clone._mode = _mode;
			return clone;
		}

		/// <summary>Persists the full tool-action wheel selection.</summary>
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

		/// <summary>Enables alternate use to open or dismiss the unified action wheel.</summary>
		public override bool AltFunctionUse(Player player) => true;

		/// <summary>Blocks use while the shared action wheel is open or opening.</summary>
		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		/// <summary>Opens the tool wheel on alt-use (after rectangle cancel when needed), or dispatches the current mode on primary use.</summary>
		public override bool? UseItem(Player player)
		{
			// --- Alt-use cancels rectangle preview, toggles wheel, or opens full action wheel; primary use runs current mode.

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
					IReadOnlyList<ToolAction> actions =
					[
						ToolAction.PIN, ToolAction.SET, ToolAction.INCLUDE, ToolAction.EXCLUDE,
						ToolAction.COPY, ToolAction.PASTE, ToolAction.RESET
					];
					ActionWheel.OpenToolActionWheel(
						Main.myPlayer,
						Item.type,
						actions,
						getCurrent: () => _mode,
						setCurrent: a => _mode = a);
				}
			}

			return true;
		}

		/// <summary>Forwards left drag and release; clears stale rectangle preview while the wheel is open.</summary>
		public override void HoldItem(Player player)
		{
			// --- While the wheel is visible, drop rectangle preview; otherwise use an IncludeStick-style hold chain.

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
				.AddIngredient<SelectionWand>(1)
				.AddIngredient<ProjectionWand>(1)
				.AddIngredient<InclusionWand>(1)
				.AddIngredient<ExecutionWand>(1)
				.AddIngredient<BlueprintCore>(1)
				.AddIngredient(ItemID.Wood, 50)
				//.AddIngredient(5710, 50) // Feywood (not in 1.4.9 ItemID)
				.AddTile(TileID.TinkerersWorkbench)
				.Register();
		}
	}
}
