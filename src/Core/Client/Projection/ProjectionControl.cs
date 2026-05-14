using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// COPY-tool gestures for projections: create from selection, pick up and move, cancel restores start-of-drag position.
    /// Hit-testing uses <see cref="BlueprintSystem"/> (same topmost order as INCLUDE/EXCLUDE).
    /// Mirrors <see cref="SelectionControl"/> (gesture state here; list data in <see cref="ProjectionSystem"/>).
    /// Future: handles, resize, and on-projection buttons follow the same Control vs System split.
    /// </summary>
    public sealed class ProjectionControl : ModSystem
    {
        private enum Mode
        {
            None,
            Drag,
        }

        private enum DragKind
        {
            None,
            Move,
        }

        private sealed class PlayerState
        {
            public Mode Mode;
            public DragKind DragKind;
            public Projection DraggingProjection;
            public Point DragOffsetFromCursor;
            public Rectangle DragStartBounds;
        }

        private readonly PlayerState[] _states = new PlayerState[Main.maxPlayers];

        public static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();

        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();

        /// <summary>
        /// Begins a COPY gesture (left click): pick up the topmost <see cref="Projection"/> under the cursor, or
        /// create one from the topmost <see cref="Selection"/> when the cursor is on the selection and not covered by a projection.
        /// </summary>
        public void BeginCopy(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode != Mode.None)
            {
                return;
            }

            if (!BlueprintSystem.GetTopmostBlueprintAt(cursorTile, out Blueprint top))
            {
                return;
            }

            switch (top)
            {
                case Projection topmost:
                    if (topmost.IsLocked)
                    {
                        return;
                    }

                    BlueprintSystem.BringBlueprintToFront(topmost);
                    state.Mode = Mode.Drag;
                    state.DragKind = DragKind.Move;
                    state.DraggingProjection = topmost;
                    state.DragStartBounds = topmost.Area;
                    state.DragOffsetFromCursor = new Point(cursorTile.X - topmost.Area.X, cursorTile.Y - topmost.Area.Y);
                    break;

                case Selection:
                {
                        Projection projection = ProjectionSystem.NewProjection(player, cursorTile);
                    if (projection is null)
                    {
                        return;
                    }

                    state.Mode = Mode.Drag;
                    state.DragKind = DragKind.Move;
                    state.DraggingProjection = projection;
                    state.DragStartBounds = projection.Area;
                    state.DragOffsetFromCursor = new Point(cursorTile.X - projection.Area.X, cursorTile.Y - projection.Area.Y);
                    break;
                }

                default:
                    // COPY only interacts with live projections and the world selection, not other registered blueprints.
                    return;
            }

            if (state.DraggingProjection is not null && state.DraggingProjection.IsDefined)
            {
                Point origin = new(cursorTile.X - state.DragOffsetFromCursor.X, cursorTile.Y - state.DragOffsetFromCursor.Y);
                state.DraggingProjection.MoveTo(origin);
            }
        }

        /// <summary>Updates an in-progress COPY drag while the left button is held.</summary>
        public void UpdateGesture(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode != Mode.Drag || state.DraggingProjection is null || !state.DraggingProjection.IsDefined)
            {
                return;
            }

            Point origin = new(cursorTile.X - state.DragOffsetFromCursor.X, cursorTile.Y - state.DragOffsetFromCursor.Y);
            state.DraggingProjection.MoveTo(origin);
        }

        /// <summary>Ends the current left-button COPY gesture (release).</summary>
        public void EndGesture(Player player)
        {
            var state = GetState(player);
            state.Mode = Mode.None;
            state.DragKind = DragKind.None;
            state.DraggingProjection = null;
        }

        /// <summary>
        /// Aborts an in-progress COPY drag and restores the projection to its position when the drag began.
        /// </summary>
        public bool CancelGesture(Player player)
        {
            var state = GetState(player);
            if (state.Mode != Mode.Drag || state.DraggingProjection is null || !state.DraggingProjection.IsDefined)
            {
                return false;
            }

            state.DraggingProjection.MoveTo(state.DragStartBounds.Location);
            EndGesture(player);
            return true;
        }

        /// <summary>True while the local player is mid-COPY drag.</summary>
        public bool IsActivelyDragging(Player player)
        {
            if (player?.whoAmI is not int i || i < 0 || i >= _states.Length)
            {
                return false;
            }

            return _states[i] is { Mode: Mode.Drag, DraggingProjection: not null };
        }

        /// <summary>
        /// Called when a <see cref="Projection"/> is removed from the world list; clears COPY gesture if it referenced that instance.
        /// </summary>
        internal void InvalidateGestureIfDragging(Player player, Projection removed)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(removed);

            var state = GetState(player);
            if (state.Mode != Mode.Drag || !ReferenceEquals(state.DraggingProjection, removed))
            {
                return;
            }

            state.Mode = Mode.None;
            state.DragKind = DragKind.None;
            state.DraggingProjection = null;
        }

        /// <summary>Clears COPY gesture state for every player slot (world unload).</summary>
        internal void ClearAllGestureSlots()
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] is not { } gs)
                {
                    continue;
                }

                gs.Mode = Mode.None;
                gs.DragKind = DragKind.None;
                gs.DraggingProjection = null;
            }
        }

        private PlayerState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            if (player.whoAmI != Main.myPlayer || Main.gameMenu)
            {
                throw new InvalidOperationException("Player is not valid");
            }

            int index = player.whoAmI;
            if (index < 0 || index >= _states.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            return _states[index] ??= new PlayerState();
        }
    }
}
