#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.ScenarioTests
{
    /// <summary>Shared preconditions for in-game scenario tests.</summary>
    internal static class ScenarioTestGuards
    {
        public static bool RequireInWorld(string scenarioId, IScenarioTestReporter reporter)
        {
            if (!Main.gameMenu)
                return true;

            reporter.Fail(scenarioId, "Join a world before running scenario tests.");
            return false;
        }

        public static bool RequireLocalPlayer(Player player, string scenarioId, IScenarioTestReporter reporter)
        {
            if (player.whoAmI == Main.myPlayer)
                return true;

            reporter.Fail(scenarioId, "Scenario runner player is not the local player.");
            return false;
        }

        public static bool RequireCursorProjection(
            Player player,
            string scenarioId,
            IScenarioTestReporter reporter,
            out Projection? projection)
        {
            projection = null;
            if (!ArchitectMapInput.TryGetCursorTile(player, out Point cursorTile))
            {
                reporter.Fail(scenarioId, "Could not resolve cursor tile (map open without architect tool?).");
                return false;
            }
            BlueprintSystem blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            if (blueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection? found) && found is not null)
            {
                projection = found;
                return true;
            }

            reporter.Fail(scenarioId, "No projection under cursor. Hover a projection and rerun.");
            return false;
        }
    }
}
#endif
