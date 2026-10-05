#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Core.ScenarioTests;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Debug chat trigger for blueprint test scenarios.</summary>
    public sealed class BlueprintTestCommand : ModCommand
    {
        public override string Command => "tab.test";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => ScenarioTestRunner.Usage;

        public override string Description =>
            "[Debug] Runs blueprint test scenarios (payload, selection, projection, paste).";

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

            ScenarioTestRunner.HandleCommand(caller, pl, args);
        }
    }
}
#endif
