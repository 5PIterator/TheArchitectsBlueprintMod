using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Systems
{
	/// <summary>Shared <see cref="RecipeGroup"/> ids for cross-evil crafting.</summary>
	public sealed class ArchitectRecipeGroups : ModSystem
	{
		public const string EvilBossSummonGroupName = "ArchitectsStencilMod:EvilBossSummon";

		public override void AddRecipeGroups()
		{
			RecipeGroup.RegisterGroup(EvilBossSummonGroupName, new RecipeGroup(
				() => "Worm Food / Bloody Spine",
				ItemID.WormFood,
				ItemID.BloodySpine));
		}
	}
}
