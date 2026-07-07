using System;
using System.Linq;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Owns each player's active Selection for the world.
    /// This system handles only the selection bounds (two points A/B -> a rectangle).
    /// Tile-level editing of that selection (INCLUDE/EXCLUDE) lives in <see cref="InclusionControl"/>.
    /// </summary>
    public sealed class SelectionSystem : ModSystem
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        private sealed class PlayerSelectionState
        {
            public bool HasPointA;
            public bool HasPointB;
            public Point PointA;
            public Point PointB;
        }

        private readonly PlayerSelectionState[] _playerStates = new PlayerSelectionState[Main.maxPlayers];

        public Selection GetSelection()
        {
            return BlueprintSystem.GetTopmostSelection();
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
            int who = player.whoAmI;
            foreach (Selection existing in BlueprintSystem.GetSelections().ToList())
            {
                if (existing.Owner == who)
                    BlueprintSystem.UnregisterBlueprint(existing);
            }

            Selection selection = new Selection(who);
            var state = GetState(player);
            state.HasPointA = false;
            state.HasPointB = false;
            state.PointA = default;
            state.PointB = default;

            BlueprintSystem.RegisterBlueprint(selection);
            UpdateSelectionOverlay(player);
            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"SelectionSystem.NewSelection: owner={player.whoAmI}", "SelectionSystem");
        }

        /// <summary>
        /// Refreshes selection world overlay registration for this player (client only).
        /// </summary>
        internal void UpdateSelectionOverlay(Player player)
        {
            int who = player.whoAmI;
            string family = $"selection:{who}";

            Selection selection = BlueprintSystem.GetTopmostSelection();
            if (selection is null || !selection.IsDefined)
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
            Selection selection = BlueprintSystem.GetTopmostSelection();
            // Invariant: Selection.Area should always reflect whichever endpoints have been provided.
            if (state.HasPointA && state.HasPointB)
            {
                selection.Define(state.PointA, state.PointB);
                return;
            }

            if (state.HasPointA)
            {
                selection.Define(new Rectangle(state.PointA.X, state.PointA.Y, 1, 1));
                return;
            }

            if (state.HasPointB)
            {
                selection.Define(new Rectangle(state.PointB.X, state.PointB.Y, 1, 1));
                return;
            }

            selection.Clear();
        }

        /// <summary>
        /// Clears the bounds selection entirely for this player.
        /// </summary>
        public void ClearSelection(Player player)
        {
            Selection? selection = BlueprintSystem.GetTopmostSelection();
            if (selection is null || selection.Owner != player.whoAmI)
                return;
            var state = GetState(player);
            state.HasPointA = false;
            state.HasPointB = false;
            state.PointA = default;
            state.PointB = default;
            selection.Clear();
            BlueprintSystem.UnregisterBlueprint(selection);
            UpdateSelectionOverlay(player);
            if (TABLog.IsDebugEnabled)
                TABLog.Debug($"SelectionSystem.ClearSelection: owner={player.whoAmI}", "SelectionSystem");
        }

        /// <summary>
        /// Set/drag point A of the selection bounds (tile coordinates).
        /// </summary>
        public void SetPointA(Player player, Point tilePosition)
        {
            var state = GetState(player);
            if (state.HasPointA && state.PointA == tilePosition)
                return;

            state.PointA = tilePosition;
            state.HasPointA = true;
            UpdateSelection(state);
            UpdateSelectionOverlay(player);
            if (TABLog.IsDebugEnabled)
            {
                var sel = BlueprintSystem.GetTopmostSelection();
                TABLog.Debug(
                    $"SelectionSystem.SetPointA: owner={player.whoAmI} tile=({tilePosition.X},{tilePosition.Y}) " +
                    $"areaDefined={sel?.IsDefined == true}",
                    "SelectionSystem");
            }
        }

        /// <summary>
        /// Set/drag point B of the selection bounds (tile coordinates).
        /// </summary>
        public void SetPointB(Player player, Point tilePosition)
        {
            var state = GetState(player);
            if (state.HasPointB && state.PointB == tilePosition)
                return;

            state.PointB = tilePosition;
            state.HasPointB = true;
            UpdateSelection(state);
            UpdateSelectionOverlay(player);
            if (TABLog.IsDebugEnabled)
            {
                var sel = BlueprintSystem.GetTopmostSelection();
                TABLog.Debug(
                    $"SelectionSystem.SetPointB: owner={player.whoAmI} tile=({tilePosition.X},{tilePosition.Y}) " +
                    $"areaDefined={sel?.IsDefined == true}",
                    "SelectionSystem");
            }
        }

        /// <summary>
        /// Useful for interactive resize/move so we only rebuild once.
        /// </summary>
        public void SetPoints(Player player, Point pointA, Point pointB)
        {
            var state = GetState(player);
            if (state.HasPointA && state.HasPointB && state.PointA == pointA && state.PointB == pointB)
                return;

            state.PointA = pointA;
            state.PointB = pointB;
            state.HasPointA = true;
            state.HasPointB = true;
            UpdateSelection(state);
            UpdateSelectionOverlay(player);
            if (TABLog.IsDebugEnabled)
            {
                var sel = BlueprintSystem.GetTopmostSelection();
                TABLog.Debug(
                    $"SelectionSystem.SetPoints: owner={player.whoAmI} A=({pointA.X},{pointA.Y}) B=({pointB.X},{pointB.Y}) " +
                    $"area={(sel is { IsDefined: true } s ? $"({s.Area.X},{s.Area.Y}) {s.Area.Width}x{s.Area.Height}" : "empty")}",
                    "SelectionSystem");
            }
        }

        /// <summary>Moves the selection by a tile delta without reallocating the inclusion grid.</summary>
        public void TranslateSelection(Player player, Point delta)
        {
            Selection? selection = BlueprintSystem.GetTopmostSelection();
            if (selection is null || !selection.IsDefined)
                return;

            selection.Translate(delta);
            var state = GetState(player);
            state.PointA = new Point(selection.Area.Left, selection.Area.Top);
            state.PointB = new Point(selection.Area.Right - 1, selection.Area.Bottom - 1);
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
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"SelectionSystem.SetBounds: owner={player.whoAmI} bounds=({bounds.X},{bounds.Y}) {bounds.Width}x{bounds.Height}",
                    "SelectionSystem");
            SetPoints(player, a, b);
        }

        public override void OnWorldUnload()
        {
            int n = 0;
            foreach (Selection selection in BlueprintSystem.GetSelections())
            {
                BlueprintSystem.UnregisterBlueprint(selection);
                selection.Clear();
                n++;
            }

            if (TABLog.IsDebugEnabled && n > 0)
                TABLog.Debug($"SelectionSystem.OnWorldUnload: cleared {n} selection blueprint(s)", "SelectionSystem");

            for (int i = 0; i < _playerStates.Length; i++)
            {
                if (_playerStates[i] is not { } state)
                    continue;

                state.HasPointA = false;
                state.HasPointB = false;
                state.PointA = default;
                state.PointB = default;
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
                _playerStates[index] = state;
            }

            return state;
        }
    }
}
