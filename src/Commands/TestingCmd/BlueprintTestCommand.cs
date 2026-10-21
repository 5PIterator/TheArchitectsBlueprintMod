#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core.TestCases;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Debug chat Test Trigger for blueprint Test Cases.</summary>
    public sealed class BlueprintTestCommand : ModCommand
    {
        public override string Command => "tab.test";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => TestCaseRunner.Usage;

        public override string Description =>
            "[Debug] Runs blueprint Test Cases (payload, selection, projection, paste).";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            // --- Reject dedicated-server / menu / non-local callers, then hand off to the runner.

            if (Main.dedServ)
            {
                caller.Reply("Blueprint tests run on the client, not on a dedicated server.");
                return;
            }

            if (Main.gameMenu)
            {
                caller.Reply("Join a world first.");
                return;
            }

            if (caller.Player is not Player pl || pl.whoAmI != Main.myPlayer)
            {
                if (caller.Player is null)
                    caller.Reply("No player context.");
                return;
            }

            TestCaseRunner.HandleCommand(caller, pl, args);
        }
    }
}
#endif
