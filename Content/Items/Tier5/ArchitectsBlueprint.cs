using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.Architect;

namespace TheArchitectsBlueprintMod.Content.Items
{
	/// <summary>
	/// ARCHITECT'S BLUEPRINT: late-game upgrade from Shimmering Architect's Hand.
	/// Permanently unlocks Architect Mode.
	/// </summary>
	public class ArchitectsBlueprint : ModItem
	{
		public override void SetDefaults()
		{
			Item.width = 28;
			Item.height = 28;
			Item.useStyle = ItemUseStyleID.HoldUp;
			Item.useTime = 20;
			Item.useAnimation = 20;
			Item.rare = ItemRarityID.Red;
			Item.value = Item.buyPrice(gold: 10);
			Item.UseSound = SoundID.Item4;
			Item.consumable = true;
			Item.maxStack = 1;
		}

		public override bool? UseItem(Player player)/* tModPorter Suggestion: Return null instead of false */
		{
			if (player.whoAmI != Main.myPlayer)
				return null;

			var architectPlayer = player.GetModPlayer<ArchitectModePlayer>();
			if (!architectPlayer.ArchitectGuiUnlocked)
			{
				architectPlayer.UnlockArchitectGuiPermanently();
				Main.NewText(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.UnlockChat"), byte.MaxValue, 220, 120);
				SoundEngine.PlaySound(SoundID.Item4);
				return true;
			}

			var architectMode = ModContent.GetInstance<ArchitectSystem>();
			if (!architectMode.Active && architectMode.CanOpen(player))
				architectMode.Toggle(true);
			else
				Main.NewText(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.AlreadyUnlocked"), byte.MaxValue, 220, 120);

			return true;
		}
	}
}
