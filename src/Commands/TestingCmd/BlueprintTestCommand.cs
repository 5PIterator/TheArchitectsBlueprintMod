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

        public override string Usage =>
            "/tab.test list | /tab.test all | /tab.test run <scenarioId> | /tab.test scenarios list|all|run <scenarioId>";

        public override string Description =>
            "[Debug] Runs blueprint test scenarios (fixture chain setup + scenario-specific checks).";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
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

            if (args.Length < 1)
            {
                caller.Reply(Usage, Color.Orange);
                return;
            }

            var reporter = new CommandScenarioTestReporter(caller);
            switch (args[0])
            {
                case "scenarios":
                    ScenarioTestRunner.HandleCommand(caller, pl, args);
                    break;
                case "list":
                    ScenarioTestRunner.ListScenarios(reporter);
                    break;
                case "all":
                    ScenarioTestRunner.RunAll(reporter, pl);
                    break;
                case "run" when args.Length >= 2:
                    ScenarioTestRunner.RunScenario(reporter, pl, args[1]);
                    break;
                default:
                    caller.Reply(Usage, Color.Orange);
                    break;
            }
        }
    }
}
#endif
