using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Owns each player's Projections (copy/cut ghosts).
    /// </summary>
    public sealed class ProjectionSystem : ModSystem
    {
        private sealed class PlayerProjectionState
        {
            public readonly List<Projection> Projections = [];
            public Projection ActiveProjection;

            public bool IsDragging;
            public Projection DraggingProjection;
            public Point DragOffsetFromCursor;
        }

        private readonly PlayerProjectionState[] _playerStates = new PlayerProjectionState[Main.maxPlayers];

        public Projection GetActiveProjection(Player player)
        {
            return GetState(player).ActiveProjection;
        }

        public IReadOnlyList<Projection> GetProjections(Player player)
        {
            return GetState(player).Projections;
        }

        public void ClearProjection(Player player)
        {
            var state = GetState(player);
            if (state.ActiveProjection is null)
            {
                return;
            }

            var toRemove = state.ActiveProjection;
            state.Projections.Remove(state.ActiveProjection);
            state.ActiveProjection = null;
            if (ReferenceEquals(state.DraggingProjection, toRemove))
            {
                state.IsDragging = false;
                state.DraggingProjection = null;
            }
        }

        public bool ClearActiveProjection(Player player, ProjectionKind kind)
        {
            var state = GetState(player);
            if (state.ActiveProjection is null || state.ActiveProjection.Kind != kind)
            {
                return false;
            }

            ClearProjection(player);
            return true;
        }

        private static int FindTopmostIndexAt(PlayerProjectionState state, Point cursorTile)
        {
            for (int i = state.Projections.Count - 1; i >= 0; i--)
            {
                Projection p = state.Projections[i];
                if (p.IsDefined && p.Bounds.Contains(cursorTile))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindTopmostIndexAt(PlayerProjectionState state, Point cursorTile, ProjectionKind kind)
        {
            for (int i = state.Projections.Count - 1; i >= 0; i--)
            {
                Projection p = state.Projections[i];
                if (p.IsDefined && p.Kind == kind && p.Bounds.Contains(cursorTile))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RemoveAtIndex(PlayerProjectionState state, int index)
        {
            Projection p = state.Projections[index];

            if (ReferenceEquals(state.ActiveProjection, p))
            {
                state.ActiveProjection = null;
            }

            if (ReferenceEquals(state.DraggingProjection, p))
            {
                state.IsDragging = false;
                state.DraggingProjection = null;
            }

            state.Projections.RemoveAt(index);
        }

        public bool RemoveProjectionAt(Player player, Point cursorTile)
        {
            var state = GetState(player);
            int index = FindTopmostIndexAt(state, cursorTile);
            if (index < 0)
            {
                return false;
            }

            if (state.Projections[index].IsLocked)
            {
                return false;
            }

            RemoveAtIndex(state, index);
            return true;
        }

        public bool RemoveProjectionAt(Player player, Point cursorTile, ProjectionKind kind)
        {
            var state = GetState(player);
            int index = FindTopmostIndexAt(state, cursorTile, kind);
            if (index < 0)
            {
                return false;
            }

            if (state.Projections[index].IsLocked)
            {
                return false;
            }

            RemoveAtIndex(state, index);
            return true;
        }

        public bool TryGetProjectionAt(Player player, Point cursorTile, out Projection projection)
        {
            var state = GetState(player);
            int index = FindTopmostIndexAt(state, cursorTile);
            if (index >= 0)
            {
                projection = state.Projections[index];
                return true;
            }

            projection = null;
            return false;
        }

        public bool TryGetProjectionAt(Player player, Point cursorTile, ProjectionKind kind, out Projection projection)
        {
            var state = GetState(player);
            int index = FindTopmostIndexAt(state, cursorTile, kind);
            if (index >= 0)
            {
                projection = state.Projections[index];
                return true;
            }

            projection = null;
            return false;
        }

        public void DragOrCreate(Player player, Point cursorTile, ProjectionKind kind)
        {
            var state = GetState(player);

            // First tick of a drag: decide what we are grabbing.
            if (!state.IsDragging)
            {
                if (TryGetProjectionAt(player, cursorTile, kind, out var topmostOfKind))
                {
                    if (topmostOfKind.IsLocked)
                    {
                        return;
                    }

                    state.ActiveProjection = topmostOfKind;
                    state.DraggingProjection = topmostOfKind;
                    state.IsDragging = true;
                    state.DragOffsetFromCursor = new Point(cursorTile.X - topmostOfKind.Bounds.X, cursorTile.Y - topmostOfKind.Bounds.Y);

                    // Bring to front for hit-testing/draw order.
                    state.Projections.Remove(topmostOfKind);
                    state.Projections.Add(topmostOfKind);
                }
                else if (TryGetProjectionAt(player, cursorTile, out _))
                {
                    // Cursor is over a different projection kind; ignore input (don't create on top).
                    return;
                }
                else
                {
                    // Only create a projection if the cursor is inside the player's blueprint.
                    var blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
                    Blueprint blueprint = blueprintSystem.GetActiveBlueprint(player);
                    if (!blueprint.IsDefined || !blueprint.Bounds.Contains(cursorTile))
                    {
                        return;
                    }

                    // Preserve an offset: grab point inside the blueprint stays under cursor while dragging.
                    Point rel = new(cursorTile.X - blueprint.Bounds.X, cursorTile.Y - blueprint.Bounds.Y);
                    Point origin = new(cursorTile.X - rel.X, cursorTile.Y - rel.Y);

                    var projection = new Projection(kind);
                    projection.OwnerPlayerIndex = player.whoAmI;
                    projection.Define(new Rectangle(origin.X, origin.Y, blueprint.Bounds.Width, blueprint.Bounds.Height));

                    // Copy selection layout from blueprint into projection (mask).
                    for (int i = 0; i < blueprint.Tiles.Count; i++)
                    {
                        var t = blueprint.Tiles[i];
                        if (projection.TryGetTileByRelative(t.Relative, out var pTile))
                        {
                            pTile.Selected = t.Selected;
                        }
                    }

                    // Capture the world tiles within the blueprint bounds as the projection source payload.
                    projection.CaptureSourceFromWorld(blueprint.Bounds);
                    var snapshot = Blueprint.SnapshotSelectedTiles(blueprint, rel =>
                        projection.TryGetSourceTile(rel, out var t) ? t : (Tile?)null);
                    projection.SetSourceTiles(snapshot);
                    projection.RefreshTargetTiles();

                    state.Projections.Add(projection);
                    state.ActiveProjection = projection;
                    state.DraggingProjection = projection;
                    state.IsDragging = true;
                    state.DragOffsetFromCursor = rel;
                }
            }

            // Drag update.
            if (state.DraggingProjection is not null && state.DraggingProjection.IsDefined)
            {
                Point origin = new(cursorTile.X - state.DragOffsetFromCursor.X, cursorTile.Y - state.DragOffsetFromCursor.Y);
                state.DraggingProjection.MoveTo(origin);
            }
        }

        public void EndDrag(Player player)
        {
            var state = GetState(player);
            state.IsDragging = false;
            state.DraggingProjection = null;
        }

        public override void OnWorldUnload()
        {
            for (int i = 0; i < _playerStates.Length; i++)
            {
                if (_playerStates[i] is not { } state)
                    continue;

                state.Projections.Clear();
                state.ActiveProjection = null;
                state.IsDragging = false;
                state.DraggingProjection = null;
                state.DragOffsetFromCursor = default;
            }
        }

        private PlayerProjectionState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            int index = player.whoAmI;
            if (index < 0 || index >= _playerStates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            return _playerStates[index] ??= new PlayerProjectionState();
        }
    }
}
