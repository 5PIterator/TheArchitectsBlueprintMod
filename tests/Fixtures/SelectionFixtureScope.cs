#if DEBUG
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Registers a fresh <see cref="Selection"/> via <see cref="SelectionSystem"/> and tears it down on dispose.</summary>
    internal sealed class SelectionFixtureScope : IDisposable
    {
        private readonly Player _player;
        private readonly Rectangle _expectedBounds;
        private bool _entered;

        public Selection? Selection { get; private set; }

        private SelectionFixtureScope(Player player, Rectangle expectedBounds)
        {
            _player = player;
            _expectedBounds = expectedBounds;
        }

        /// <summary>Creates a selection centered on the cursor tile with the given size.</summary>
        internal static bool TryEnter(
            Player player,
            int width,
            int height,
            out SelectionFixtureScope? scope,
            out string? error)
        {
            scope = null;
            Rectangle bounds = SelectionFixture.TileRectAtCursor(width, height);
            return TryEnterWithBounds(player, bounds, out scope, out error);
        }

        internal static bool TryEnterWithBounds(
            Player player,
            Rectangle worldTileBounds,
            out SelectionFixtureScope? scope,
            out string? error)
        {
            scope = null;
            ArgumentNullException.ThrowIfNull(player);
            if (!WorldBoundsFixture.TryEnsureRectangleInWorld(worldTileBounds, out error))
                return false;

            var candidate = new SelectionFixtureScope(player, worldTileBounds);
            if (!candidate.Enter(out error))
                return false;

            scope = candidate;
            return true;
        }

        private bool Enter(out string? error)
        {
            error = null;
            var selectionSystem = ModContent.GetInstance<SelectionSystem>();
            selectionSystem.NewSelection(_player);
            selectionSystem.SetBounds(_player, _expectedBounds);
            Selection = ModContent.GetInstance<BlueprintSystem>().GetTopmostSelection();
            _entered = true;
            return VerifyAfterSetup(out error);
        }

        internal bool VerifyAfterSetup(out string? error) =>
            BlueprintTestAssertions.VerifySelection(_player, _expectedBounds, Selection, out error);

        internal bool VerifyAfterTeardown(out string? error) =>
            BlueprintTestAssertions.VerifyNoSelection(_player, out error);

        public void Dispose()
        {
            if (!_entered)
                return;

            ModContent.GetInstance<SelectionSystem>().ClearSelection(_player);
            Selection = null;
            _entered = false;
        }
    }
}
#endif
