#if DEBUG
#nullable enable

using System.Linq;
using TheArchitectsBlueprintMod.Tests;
using TheArchitectsBlueprintMod.Tests.Samples;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Debug-only chat trigger for payload IO scenarios.</summary>
    public sealed class PayloadIoTestCommand : ModCommand
    {
        public override string Command => "tab.test.payload";
        public override CommandType Type => CommandType.Chat;

        public override string Usage => "/tab.test.payload clear|fill";
        public override string Description => "[Debug] Runs payload_io_clear or payload_io_fill scenarios.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.dedServ)
            {
                caller.Reply("Payload IO tests run on the client, not on a dedicated server.");
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

            if (args.Length < 1 || (args[0] != "clear" && args[0] != "fill"))
            {
                caller.Reply("Usage: /tab.test.payload clear|fill", Color.Orange);
                return;
            }

            string slug = TestScenarioRegistry.Samples[0].Slug;
            string scenarioId = args[0] == "clear" ? $"payload_io_clear_{slug}" : $"payload_io_fill_{slug}";
            BlueprintTestRunner.RunScenario(new CommandTestReporter(caller), pl, scenarioId);
        }
    }
}
#endif
