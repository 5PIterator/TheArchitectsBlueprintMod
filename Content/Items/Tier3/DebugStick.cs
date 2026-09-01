using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Utils;
#if DEBUG
using TheArchitectsBlueprintMod.Core.ScenarioTests;
#endif

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// DEBUG STICK: action wheel plus stepped sessions via <see cref="DebugStepper"/> (discovery, stepped paste).
	/// </summary>
	public class DebugStick : ModItem
	{
		private static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();
		private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
		private enum DebugStickAction
		{
			ToggleProjectionCutSource = 0,
			StepPaste = 1,
			RunBlueprintTests = 2,
		}

		private DebugStickAction _mode = DebugStickAction.ToggleProjectionCutSource;

		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 28;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 8;
			Item.useAnimation = 8;
			Item.rare = ItemRarityID.Orange;
			Item.value = Item.buyPrice(silver: 10);
			Item.autoReuse = false;
		}

		public override ModItem Clone(Item newEntity)
		{
			var clone = (DebugStick)base.Clone(newEntity);
			clone._mode = _mode;
			return clone;
		}

		public override void SaveData(TagCompound tag) => tag["debugMode"] = (int)_mode;

		public override void LoadData(TagCompound tag)
		{
			if (!tag.ContainsKey("debugMode"))
				return;
			int raw = tag.GetInt("debugMode");
			if (Enum.IsDefined(typeof(DebugStickAction), raw))
				_mode = (DebugStickAction)raw;
		}

		public override bool AltFunctionUse(Player player) => true;

		public override bool CanUseItem(Player player) => !ActionWheel.IsActive && !ActionWheel.IsPendingOpen;

		public override bool? UseItem(Player player)
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			if (!ArchitectMapInput.TryGetCursorTile(player, out Point cursorTile))
				return true;

			if (player.altFunctionUse == 0)
			{
				if (DebugStepper.TryAdvanceSession(player))
					return true;
				ApplyDebugMode(player, cursorTile);
			}
			else if (player.altFunctionUse == 2 && !Main.mouseLeft)
			{
				if (ActionWheel.IsOwnedBy(Main.myPlayer, Item.type))
					ActionWheel.CancelActive();
				else
					ActionWheel.OpenNew(Main.myPlayer, Item.type, CreateWheelEntries());
			}

			return true;
		}

		/// <summary>Builds wheel slices. Add a <see cref="DebugStickAction"/> value, then append a matching entry here.</summary>
		private List<ActionWheelEntry> CreateWheelEntries()
		{
			return
			[
				new ActionWheelEntry(
					"toggle_projection_cut",
					"CUT",
					onSelect: () => _mode = DebugStickAction.ToggleProjectionCutSource,
					isSelected: () => _mode == DebugStickAction.ToggleProjectionCutSource),
				new ActionWheelEntry(
					"step_paste",
					"STP",
					onSelect: () => _mode = DebugStickAction.StepPaste,
					isSelected: () => _mode == DebugStickAction.StepPaste),
#if DEBUG
				new ActionWheelEntry(
					"run_tests",
					"TST",
					onSelect: () => _mode = DebugStickAction.RunBlueprintTests,
					isSelected: () => _mode == DebugStickAction.RunBlueprintTests),
#endif
			];
		}

		private void ApplyDebugMode(Player player, Point cursorTile)
		{
			switch (_mode)
			{
				case DebugStickAction.ToggleProjectionCutSource:

					ProjectionSystem.ToggleCutSource(cursorTile);
					return;
				case DebugStickAction.StepPaste:
					BeginPasteStepSession(player, cursorTile);
					return;
#if DEBUG
				case DebugStickAction.RunBlueprintTests:
					ScenarioTestRunner.RunAll(new DelegateScenarioTestReporter((msg, color) => Main.NewText(msg, color)), player);
					return;
#endif
				default:
					throw new ArgumentOutOfRangeException(nameof(_mode), _mode, "Unknown debug stick action.");
			}
		}
		private void BeginPasteStepSession(Player player, Point cursorTile)
		{
			if (!BlueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection projection) ||
			    projection is null ||
			    !projection.IsDefined)
			{
				Main.NewText("Debug: no projection at cursor.", Color.OrangeRed);
				return;
			}

			ModContent.GetInstance<PastingSystem>().BeginDebugStepSession(player, projection);
		}
	}
}
