using Terraria;
using Terraria.ModLoader;
using ArchitectsStencilMod.Core.TileMapping;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Chat/console command to run shimmer transformation discovery.
    /// Part 1: Discovers ItemID.Sets.ShimmerTransformToItem and recipe decrafting, saves ShimmerMap.itr.
    /// Part 2: Pairs result items with Placement.itr tiles, exports Shimmer.itr (item-first, loadable).
    /// </summary>
    public sealed class DiscShimmerCommand : ModCommand
    {
        public override string Command => "asm.discShimmer";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/asm.discShimmer";
        public override string Description => "Runs shimmer discovery; writes ShimmerMap.itr and Shimmer.itr.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            int plr = caller.Player?.whoAmI ?? Main.myPlayer;
            ShimmerDiscovery.Run(Mod, msg => caller.Reply(msg), Main.player[plr]);
        }
    }
}
