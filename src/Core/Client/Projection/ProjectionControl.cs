using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// COPY-tool gestures for projections: create from blueprint, pick up and move, cancel restores start-of-drag position.
    /// Mirrors <see cref="BlueprintControl"/> (gesture state here; list data in <see cref="ProjectionSystem"/>).
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

        /// <summary>Begins a COPY gesture (left click): pick up an existing projection or spawn one from the blueprint.</summary>
        public void BeginCopy(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode != Mode.None)
            {
                return;
            }

            var sys = ProjectionSystem;
            if (sys.TryGetProjectionAt(player, cursorTile, out var topmost))
            {
                if (topmost.IsLocked)
                {
                    return;
                }

                sys.SetActiveProjection(player, topmost);
                sys.BringProjectionToFront(player, topmost);
                state.Mode = Mode.Drag;
                state.DragKind = DragKind.Move;
                state.DraggingProjection = topmost;
                state.DragStartBounds = topmost.Bounds;
                state.DragOffsetFromCursor = new Point(cursorTile.X - topmost.Bounds.X, cursorTile.Y - topmost.Bounds.Y);
            }
            else
            {
                var projection = sys.NewProjection(player, cursorTile);
                if (projection is null)
                {
                    return;
                }

                state.Mode = Mode.Drag;
                state.DragKind = DragKind.Move;
                state.DraggingProjection = projection;
                state.DragStartBounds = projection.Bounds;
                state.DragOffsetFromCursor = new Point(cursorTile.X - projection.Bounds.X, cursorTile.Y - projection.Bounds.Y);
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

            int i = player.whoAmI;
            if ((uint)i >= (uint)_states.Length)
            {
                return;
            }

            if (_states[i] is not { } gs)
            {
                return;
            }

            if (gs.Mode != Mode.Drag || !ReferenceEquals(gs.DraggingProjection, removed))
            {
                return;
            }

            gs.Mode = Mode.None;
            gs.DragKind = DragKind.None;
            gs.DraggingProjection = null;
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
