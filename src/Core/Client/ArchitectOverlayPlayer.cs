using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Tracks overlay scopes for the locally held item, mirroring how vanilla updates wire visibility from the selected tool.
    /// </summary>
    public sealed class ArchitectOverlayPlayer : ModPlayer
    {
        public ArchitectOverlayScope HeldOverlayScope { get; private set; }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            HeldOverlayScope = ArchitectOverlayVisibility.GetItemScope(Player.HeldItem.type);
        }
    }
}
