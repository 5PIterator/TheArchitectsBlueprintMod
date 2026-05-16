#if DEBUG
#nullable enable

using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Shared post-setup and post-teardown checks for blueprint test fixtures.</summary>
    internal static class BlueprintTestAssertions
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();

        internal static bool VerifySelection(Player player, Rectangle expectedBounds, Selection? selection, out string? error)
        {
            error = null;
            if (selection is null)
            {
                error = "Selection is null.";
                return false;
            }

            if (selection.Owner != player.whoAmI)
            {
                error = $"Selection owner mismatch (expected {player.whoAmI}, got {selection.Owner}).";
                return false;
            }

            if (!selection.IsDefined)
            {
                error = "Selection is not defined after setup.";
                return false;
            }

            if (selection.Area != expectedBounds)
            {
                error =
                    $"Selection bounds mismatch (expected {expectedBounds.X},{expectedBounds.Y} " +
                    $"{expectedBounds.Width}x{expectedBounds.Height}, got {selection.Area}).";
                return false;
            }

            if (!ReferenceEquals(BlueprintSystem.GetTopmostSelection(), selection))
            {
                error = "Selection is not the topmost registered selection.";
                return false;
            }

            return true;
        }

        internal static bool VerifyProjection(Player player, Projection? projection, bool expectPayload, out string? error)
        {
            error = null;
            if (projection is null)
            {
                error = "Projection is null.";
                return false;
            }

            if (projection.Owner != player.whoAmI)
            {
                error = $"Projection owner mismatch (expected {player.whoAmI}, got {projection.Owner}).";
                return false;
            }

            if (!projection.IsDefined)
            {
                error = "Projection is not defined after setup.";
                return false;
            }

            if (expectPayload && !projection.HasPayload)
            {
                error = "Projection has no payload after setup.";
                return false;
            }

            if (!BlueprintSystem.GetProjections().Contains(projection))
            {
                error = "Projection is not registered in BlueprintSystem.";
                return false;
            }

            return true;
        }

        internal static bool VerifyImportedPayload(Projection projection, int width, int height, bool centerHasTile, out string? error)
        {
            error = null;
            if (projection.Payload.Width != width || projection.Payload.Height != height)
            {
                error = $"Payload size mismatch (expected {width}x{height}, got {projection.Payload.Width}x{projection.Payload.Height}).";
                return false;
            }

            if (!projection.Payload.TryGetCell(new Point(1, 1), out PayloadCell center) || center.HasTile() != centerHasTile)
            {
                error = $"Center cell HasTile expected {centerHasTile}.";
                return false;
            }

            return true;
        }

        internal static bool VerifyNoSelection(Player player, out string? error)
        {
            error = null;
            Selection? top = BlueprintSystem.GetTopmostSelection();
            if (top is not null && top.Owner == player.whoAmI && top.IsDefined)
            {
                error = "Player still has a defined selection after teardown.";
                return false;
            }

            return true;
        }

        internal static bool VerifyNoProjection(Player player, out string? error)
        {
            error = null;
            foreach (Projection projection in BlueprintSystem.GetProjections())
            {
                if (projection.Owner != player.whoAmI)
                    continue;
                error = "Player still has a registered projection after teardown.";
                return false;
            }

            return true;
        }

        internal static bool VerifyNoActivePaste(Player player, out string? error)
        {
            error = null;
            var pasting = ModContent.GetInstance<PastingSystem>();
            if (pasting.TryGetActivePasteForPlayer(player, out _))
            {
                error = "Player still has an active paste operation after teardown.";
                return false;
            }

            return true;
        }
    }
}
#endif
