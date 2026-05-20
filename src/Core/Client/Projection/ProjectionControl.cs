using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// COPY-tool gestures for projections: create from selection, pick up and move, cancel restores start-of-drag position.
    /// Hit-testing uses <see cref="BlueprintSystem"/> (same topmost order as INCLUDE/EXCLUDE).
    /// Mirrors <see cref="SelectionControl"/> (client-only gesture state here; list data in <see cref="ProjectionSystem"/>).
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

        private sealed class GestureState
        {
            public Mode Mode;
            public DragKind DragKind;
            public Projection DraggingProjection;
            public Point DragOffsetFromCursor;
            public Rectangle DragStartBounds;
        }

        private readonly GestureState _gesture = new();

        public static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();

        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();

        /// <summary>
        /// Begins a COPY gesture (left click): pick up the topmost <see cref="Projection"/> under the cursor, or
        /// create one from the topmost <see cref="Selection"/> when the cursor is on the selection and not covered by a projection.
        /// </summary>
        public void BeginCopy(Player player, Point cursorTile)
        {
            GestureState state = GetGesture(player);
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

                case Selection topmost:
                {
                    Projection projection = ProjectionSystem.NewProjection(player, topmost);
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
            GestureState state = GetGesture(player);
            if (state.Mode != Mode.Drag || state.DraggingProjection is null || !state.DraggingProjection.IsDefined)
            {
                return;
            }

            Point origin = new(cursorTile.X - state.DragOffsetFromCursor.X, cursorTile.Y - state.DragOffsetFromCursor.Y);
            state.DraggingProjection.MoveTo(origin, isDragFrame: true);
        }

        /// <summary>Ends the current left-button COPY gesture (release).</summary>
        public void EndGesture(Player player)
        {
            GestureState state = GetGesture(player);
            Projection? dragged = state.DraggingProjection;
            dragged?.CommitDragRefresh();
            state.Mode = Mode.None;
            state.DragKind = DragKind.None;
            state.DraggingProjection = null;
        }

        /// <summary>
        /// Aborts an in-progress COPY drag and restores the projection to its position when the drag began.
        /// </summary>
        public bool CancelGesture(Player player)
        {
            GestureState state = GetGesture(player);
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
            if (player is null || player.whoAmI != Main.myPlayer || Main.gameMenu)
            {
                return false;
            }

            return _gesture is { Mode: Mode.Drag, DraggingProjection: not null };
        }

        /// <summary>
        /// Called when a <see cref="Projection"/> is removed from the world list; clears COPY gesture if it referenced that instance.
        /// </summary>
        internal void InvalidateGestureIfDragging(Projection removed)
        {
            if (_gesture.Mode != Mode.Drag || !ReferenceEquals(_gesture.DraggingProjection, removed))
            {
                return;
            }

            _gesture.Mode = Mode.None;
            _gesture.DragKind = DragKind.None;
            _gesture.DraggingProjection = null;
        }

        /// <summary>Clears COPY gesture state (world unload).</summary>
        internal void ClearAllGestureSlots()
        {
            _gesture.Mode = Mode.None;
            _gesture.DragKind = DragKind.None;
            _gesture.DraggingProjection = null;
        }

        private GestureState GetGesture(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            if (player.whoAmI != Main.myPlayer || Main.gameMenu)
            {
                throw new InvalidOperationException("Player is not valid");
            }

            return _gesture;
        }
    }
}
