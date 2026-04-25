using System;
using ArchitectsStencilMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Owns each player's active Selection for the world.
    /// This system handles only the selection bounds (two points A/B -> a rectangle).
    /// Tile-level editing of that selection (INCLUDE/EXCLUDE) lives in <see cref="InclusionControl"/>.
    /// </summary>
    public sealed class SelectionSystem : ModSystem
    {
        private sealed class PlayerSelectionState
        {
            public Selection Selection = null!;
            public bool HasPointA;
            public bool HasPointB;
            public Point PointA;
            public Point PointB;
        }

        private readonly PlayerSelectionState[] _playerStates = new PlayerSelectionState[Main.maxPlayers];

        public Selection GetSelection(Player player)
        {
            return GetState(player).Selection;
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
        /// Replaces the current selection instance for this player.
        /// Intended for "crude" selector tools that always start fresh.
        /// </summary>
        public void NewSelection(Player player)
        {
            var state = GetState(player);
            state.Selection = new Selection(player.whoAmI);
            state.HasPointA = false;
            state.HasPointB = false;
            state.PointA = default;
            state.PointB = default;
            UpdateSelectionOverlay(player);
        }

        /// <summary>
        /// Refreshes selection world overlay registration for this player (client only).
        /// </summary>
        internal void UpdateSelectionOverlay(Player player)
        {
            int who = player.whoAmI;
            string family = $"selection:{who}";

            Selection selection = GetSelection(player);
            if (!selection.IsDefined)
            {
                OverlaySystem.UnregisterFamily(family);
                return;
            }

            // Draw the bounds of the selection
            OverlaySystem.RegTileBounds("bounds", selection.Area, ArchitectVisualPalette.SelectionBoundsColor, 2, family);
            // Clear previous exclusion fill: RegTileFill does not run when the mask is all included, and the
            // catalog would otherwise keep the last non-empty "excluded" registration.
            OverlaySystem.Unregister("excluded", family);
            Point[] excluded = selection.BuildExcludedTilePoints();
            if (excluded.Length > 0)
                OverlaySystem.RegTileFill("excluded", excluded, Color.Black * 0.35f, family);
            // Draw the pin points of the selection
            if (TryGetPoints(player, out var a, out var b))
            {
                OverlaySystem.RegPoints("a", [a], ArchitectVisualPalette.PinAColor, family);
                OverlaySystem.RegPoints("b", [b], ArchitectVisualPalette.PinBColor, family);
            }
        }

        private static void UpdateSelection(PlayerSelectionState state)
        {
            // Invariant: Selection.Area should always reflect whichever endpoints have been provided.
            if (state.HasPointA && state.HasPointB)
            {
                state.Selection.Define(new Rectangle(state.PointA.X, state.PointA.Y, state.PointB.X - state.PointA.X + 1, state.PointB.Y - state.PointA.Y + 1));
                return;
            }

            if (state.HasPointA)
            {
                state.Selection.Define(new Rectangle(state.PointA.X, state.PointA.Y, 1, 1));
                return;
            }

            if (state.HasPointB)
            {
                state.Selection.Define(new Rectangle(state.PointB.X, state.PointB.Y, 1, 1));
                return;
            }

            state.Selection.Clear();
        }

        /// <summary>
        /// Clears the bounds selection entirely for this player.
        /// </summary>
        public void ClearSelection(Player player)
        {
            var state = GetState(player);
            state.HasPointA = false;
            state.HasPointB = false;
            state.PointA = default;
            state.PointB = default;
            state.Selection.Clear();
            UpdateSelectionOverlay(player);
        }

        /// <summary>
        /// Set/drag point A of the selection bounds (tile coordinates).
        /// </summary>
        public void SetPointA(Player player, Point tilePosition)
        {
            var state = GetState(player);
            state.PointA = tilePosition;
            state.HasPointA = true;
            UpdateSelection(state);
            UpdateSelectionOverlay(player);
        }

        /// <summary>
        /// Set/drag point B of the selection bounds (tile coordinates).
        /// </summary>
        public void SetPointB(Player player, Point tilePosition)
        {
            var state = GetState(player);
            state.PointB = tilePosition;
            state.HasPointB = true;
            UpdateSelection(state);
            UpdateSelectionOverlay(player);
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
            UpdateSelection(state);
            UpdateSelectionOverlay(player);
        }

        /// <summary>
        /// Defines the selection bounds directly (tile coordinates, inclusive sizing like <see cref="Blueprint.Area"/>).
        /// </summary>
        public void SetBounds(Player player, Rectangle bounds)
        {
            if (bounds.IsEmpty)
            {
                ClearSelection(player);
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
                state.Selection.Clear();
            }
        }

        private PlayerSelectionState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            int index = player.whoAmI;
            if (index < 0 || index >= _playerStates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            if (_playerStates[index] is not { } state)
            {
                state = new PlayerSelectionState();
                state.Selection = new Selection(player.whoAmI);
                _playerStates[index] = state;
            }

            return state;
        }
    }
}
