#if DEBUG
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Tests
{
    /// <summary>Runs registered test scenarios (chat command or debug stick).</summary>
    internal static class BlueprintTestRunner
    {
        internal static void RunAll(CommandCaller caller, Player player) =>
            RunAll(new CommandTestReporter(caller), player);

        internal static void RunAll(Player player, Action<string, Color> emit) =>
            RunAll(new ChatTestReporter(emit), player);

        internal static void RunAll(ITestReporter reporter, Player player) =>
            TestScenarioRegistry.RunAll(reporter, player);

        internal static void RunScenario(ITestReporter reporter, Player player, string id) =>
            TestScenarioRegistry.Run(reporter, player, id);

        internal static void ListScenarios(ITestReporter reporter) =>
            TestScenarioRegistry.ListScenarioIds(reporter);
    }
}
#endif
