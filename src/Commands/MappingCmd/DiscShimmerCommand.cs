using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsBlueprintMod.Core.TileMapping;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Chat/console command to run shimmer transformation discovery.
    /// Part 1: Discovers ItemID.Sets.ShimmerTransformToItem and recipe decrafting, saves ShimmerMap.itr.
    /// Part 2: Pairs result items with Placement.itr tiles, exports Shimmer.itr (item-first, loadable).
    /// Usage: /abp.discShimmer
    /// </summary>
    public sealed class DiscShimmerCommand : ModCommand
    {
        public override string Command => "abp.discShimmer";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/abp.discShimmer";
        public override string Description => "Runs shimmer discovery; writes ShimmerMap.itr and Shimmer.itr.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("This command is server-only in multiplayer.");
                return;
            }

            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            ShimmerDiscovery.Run(Mod, msg => caller.Reply(msg), Main.player[plr]);
        }
    }
}
