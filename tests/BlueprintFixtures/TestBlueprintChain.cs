#if DEBUG
#nullable enable

using System;
using TheArchitectsBlueprintMod.Core;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>
    /// Fluent chain for test setup: player context to tile bounds, then selection and/or projection.
    /// Compose steps in order; each step returns a new chain instance (immutable).
    /// </summary>
    internal readonly struct TestBlueprintChain
    {
        internal Player Player { get; }
        internal Rectangle WorldTileBounds { get; }

        private TestBlueprintChain(Player player, Rectangle worldTileBounds)
        {
            Player = player;
            WorldTileBounds = worldTileBounds;
        }

        /// <summary>Start a chain for the given local test player.</summary>
        internal static TestBlueprintChain Begin(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);
            return new TestBlueprintChain(player, Rectangle.Empty);
        }

        /// <summary>Set world tile bounds from the player’s tile position (optional vertical offset in tiles).</summary>
        internal TestBlueprintChain WithTileBoundsFromPlayerCenter(int width, int height, int offsetYTiles = 0)
        {
            Rectangle r = SelectionFixture.TileRectFromPlayerCenter(Player, width, height, offsetYTiles);
            return new TestBlueprintChain(Player, r);
        }

        /// <summary>Set explicit world tile bounds (top-left + size).</summary>
        internal TestBlueprintChain WithWorldTileBounds(Rectangle worldTileRectangle) =>
            new(Player, worldTileRectangle);

        /// <summary>Set bounds to match a payload’s width and height anchored at the player’s tile.</summary>
        internal TestBlueprintChain WithTileBoundsFromPlayerForPayload(Payload payload, int offsetYTiles = 0)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (!payload.IsDefined)
                throw new ArgumentException("Payload is not defined.", nameof(payload));
            return WithTileBoundsFromPlayerCenter(payload.Width, payload.Height, offsetYTiles);
        }

        internal bool HasBounds => !WorldTileBounds.IsEmpty;

        internal bool TryValidateInWorld(out string? error) =>
            WorldBoundsFixture.TryEnsureRectangleInWorld(WorldTileBounds, out error);

        internal bool TryBuildSelection(out Selection selection, out string? error) =>
            SelectionFixture.TryCreate(Player, WorldTileBounds, out selection, out error);

        internal bool TryBuildProjection(bool cutSource, out Projection projection, out string? error)
        {
            if (!TryBuildSelection(out Selection sel, out error))
            {
                projection = null!;
                return false;
            }

            return ProjectionFixture.TryCreate(sel, cutSource, out projection, out error);
        }

        internal bool TryBuildProjectionWithImportedPayload(Payload payload, bool cutSource, out Projection projection, out string? error)
        {
            projection = null!;
            if (!HasBounds)
            {
                error = "Chain has no tile bounds; use WithTileBounds* before building a projection.";
                return false;
            }

            if (!TryBuildSelection(out Selection sel, out error))
                return false;

            return ProjectionFixture.TryCreateWithImportedPayload(sel, payload, cutSource, out projection, out error);
        }
    }
}
#endif
