#if DEBUG
#nullable enable

using TheArchitectsBlueprintMod.Tests;
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
            "/tab.test list | /tab.test all | /tab.test run <scenarioId> | legacy: selection|projection|import|exec|cut|cut_on|cut_off|cut_toggle";

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

            var reporter = new CommandTestReporter(caller);
            switch (args[0])
            {
                case "list":
                    BlueprintTestRunner.ListScenarios(reporter);
                    break;
                case "all":
                    BlueprintTestRunner.RunAll(reporter, pl);
                    break;
                case "run" when args.Length >= 2:
                    BlueprintTestRunner.RunScenario(reporter, pl, args[1]);
                    break;
                default:
                    if (TestScenarioRegistry.TryResolveLegacyAlias(args[0], out string? legacyId))
                        BlueprintTestRunner.RunScenario(reporter, pl, legacyId!);
                    else
                        caller.Reply(Usage, Color.Orange);
                    break;
            }
        }
    }
}
#endif
