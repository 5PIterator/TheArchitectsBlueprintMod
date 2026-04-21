using System;
using ArchitectsStencilMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Owns each player's active Blueprint selection for the world.
    /// This system handles only the selection bounds (two points A/B -> a rectangle).
    /// Tile-level editing of that selection (ADD/REMOVE) lives in <see cref="SelectionSystem"/>.
    /// </summary>
    public sealed class BlueprintSystem : ModSystem
    {
        private sealed class PlayerBlueprintState
        {
            public Blueprint Blueprint = new();
            public bool HasPointA;
            public bool HasPointB;
            public Point PointA;
            public Point PointB;
        }

        private readonly PlayerBlueprintState[] _playerStates = new PlayerBlueprintState[Main.maxPlayers];

        public Blueprint GetBlueprint(Player player)
        {
            return GetState(player).Blueprint;
        }

        public bool TryGetPointA(Player player, out Point pointA)
        {
            var state = GetState(player);
            if (!state.HasPointA)
            {
                pointA = default;
                return false;
            }

            pointA = state.PointA;
            return true;
        }

        public bool TryGetPointB(Player player, out Point pointB)
        {
            var state = GetState(player);
            if (!state.HasPointB)
            {
                pointB = default;
                return false;
            }

            pointB = state.PointB;
            return true;
        }

        public bool TryGetPoints(Player player, out Point pointA, out Point pointB)
        {
            var state = GetState(player);
            if (!state.HasPointA || !state.HasPointB)
            {
                pointA = default;
                pointB = default;
                return false;
            }

            pointA = state.PointA;
            pointB = state.PointB;
            return true;
        }

        /// <summary>
        /// Replaces the current blueprint instance for this player.
        /// Intended for "crude" selector tools that always start fresh.
        /// </summary>
        public void NewBlueprint(Player player)
        {
            var state = GetState(player);
            state.Blueprint = new Blueprint();
            state.HasPointA = false;
            state.HasPointB = false;
            state.PointA = default;
            state.PointB = default;
            UpdateBlueprintOverlay(player);
        }

        /// <summary>
        /// Refreshes blueprint world overlay registration for this player (client only).
        /// </summary>
        internal void UpdateBlueprintOverlay(Player player)
        {
            int who = player.whoAmI;
            string family = $"blueprint:{who}";

            Blueprint blueprint = GetBlueprint(player);
            if (!blueprint.IsDefined)
            {
                OverlaySystem.UnregisterFamily(family);
                return;
            }

            // Draw the bounds of the blueprint
            OverlaySystem.RegTileBounds("bounds", blueprint.Bounds, Color.White, 2, family);
            // Draw the unselected tiles of the blueprint
            Point[] unsel = blueprint.BuildUnselectedTilePoints();
            if (unsel.Length > 0)
                OverlaySystem.RegTileFill("unsel", unsel, Color.Black * 0.35f, family);
            // Draw the points of the blueprint
            if (TryGetPoints(player, out var a, out var b))
            {
                OverlaySystem.RegPoints("a", [a], Color.Cyan, family);
                OverlaySystem.RegPoints("b", [b], Color.Orange, family);
            }
        }

        private static void UpdateBlueprint(PlayerBlueprintState state)
        {
            // Invariant: Blueprint.Bounds should always reflect whichever endpoints have been provided.
            if (state.HasPointA && state.HasPointB)
            {
                state.Blueprint.Define(Blueprint.FromPoints(state.PointA, state.PointB));
                return;
            }

            if (state.HasPointA)
            {
                state.Blueprint.Define(new Rectangle(state.PointA.X, state.PointA.Y, 1, 1));
                return;
            }

            if (state.HasPointB)
            {
                state.Blueprint.Define(new Rectangle(state.PointB.X, state.PointB.Y, 1, 1));
                return;
            }

            state.Blueprint.Clear();
        }

        /// <summary>
        /// Clears the bounds selection entirely for this player.
        /// </summary>
        public void ClearBlueprint(Player player)
        {
            var state = GetState(player);
            state.HasPointA = false;
            state.HasPointB = false;
            state.PointA = default;
            state.PointB = default;
            state.Blueprint.Clear();
            UpdateBlueprintOverlay(player);
        }

        /// <summary>
        /// Set/drag point A of the blueprint bounds (tile coordinates).
        /// </summary>
        public void SetPointA(Player player, Point tilePosition)
        {
            var state = GetState(player);
            state.PointA = tilePosition;
            state.HasPointA = true;
            UpdateBlueprint(state);
            UpdateBlueprintOverlay(player);
        }

        /// <summary>
        /// Set/drag point B of the blueprint bounds (tile coordinates).
        /// </summary>
        public void SetPointB(Player player, Point tilePosition)
        {
            var state = GetState(player);
            state.PointB = tilePosition;
            state.HasPointB = true;
            UpdateBlueprint(state);
            UpdateBlueprintOverlay(player);
        }

        /// <summary>
        /// Sets both points A and B in one call (tile coordinates).
        /// Useful for interactive resize/move so we only rebuild once.
        /// </summary>
        public void SetPoints(Player player, Point pointA, Point pointB)
        {
            var state = GetState(player);
            state.PointA = pointA;
            state.PointB = pointB;
            state.HasPointA = true;
            state.HasPointB = true;
            UpdateBlueprint(state);
            UpdateBlueprintOverlay(player);
        }

        /// <summary>
        /// Defines the blueprint bounds directly (tile coordinates, inclusive sizing like <see cref="Blueprint.Bounds"/>).
        /// </summary>
        public void SetBounds(Player player, Rectangle bounds)
        {
            if (bounds.IsEmpty)
            {
                ClearBlueprint(player);
                return;
            }

            var a = new Point(bounds.Left, bounds.Top);
            var b = new Point(bounds.Right - 1, bounds.Bottom - 1);
            SetPoints(player, a, b);
        }

        public override void OnWorldUnload()
        {
            for (int i = 0; i < _playerStates.Length; i++)
            {
                if (_playerStates[i] is not { } state)
                    continue;

                state.HasPointA = false;
                state.HasPointB = false;
                state.PointA = default;
                state.PointB = default;
                state.Blueprint.Clear();
            }
        }

        private PlayerBlueprintState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            int index = player.whoAmI;
            if (index < 0 || index >= _playerStates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            return _playerStates[index] ??= new PlayerBlueprintState();
        }

    }
}
