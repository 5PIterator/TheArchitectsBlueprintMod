using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Owns each player's projection list and <see cref="ActiveProjection"/>.
    /// Interactive COPY gestures (drag, cancel) are handled by <see cref="ProjectionControl"/>.
    /// </summary>
    public sealed class ProjectionSystem : ModSystem
    {
        private sealed class PlayerProjectionState
        {
            public readonly List<Projection> Projections = [];
            public Projection ActiveProjection;
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

        /// <summary>
        /// Sets which projection is considered active for paste and toggles (topmost under cursor is typical).
        /// </summary>
        public void SetActiveProjection(Player player, Projection projection)
        {
            GetState(player).ActiveProjection = projection;
        }

        /// <summary>
        /// Moves <paramref name="projection"/> to the end of the player's list (topmost for hit-test and draw).
        /// </summary>
        public void BringProjectionToFront(Player player, Projection projection)
        {
            var list = GetState(player).Projections;
            if (!list.Remove(projection))
            {
                return;
            }

            list.Add(projection);
            projection.UpdateOverlay();
        }

        /// <summary>
        /// Creates a projection from the player's blueprint when the cursor lies inside it; returns null on failure.
        /// Adds the projection to the player's list and sets <see cref="ActiveProjection"/>.
        /// </summary>
        public Projection NewProjection(Player player, Point cursorTile)
        {

            // Get the blueprint from the blueprint system
            var blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            Blueprint blueprint = blueprintSystem.GetBlueprint(player);

            // Validate the blueprint
            if (!blueprint.IsDefined || !blueprint.Bounds.Contains(cursorTile))
            {
                return null;
            }
            var config = ModContent.GetInstance<PastingConfig>();
            if (blueprint.Bounds.Width > config.MaxPasteWidth || blueprint.Bounds.Height > config.MaxPasteHeight)
            {
                return null;
            }

            // Gather Blueprint rel, origin and source tiles.
            Point rel = new(cursorTile.X - blueprint.Bounds.X, cursorTile.Y - blueprint.Bounds.Y);
            Point origin = new(cursorTile.X - rel.X, cursorTile.Y - rel.Y);
            IReadOnlyList<Blueprint.BoundedTile> sourceTiles = blueprint.SnapshotTiles(rel =>
            {
                Point world = blueprint.RelativeToWorld(rel);
                if (!WorldGen.InWorld(world.X, world.Y))
                {
                    return null;
                }

                return Main.tile[world.X, world.Y];
            });

            // Create the projection
            var projection = new Projection(
                cutSource: false,
                sourceArea: blueprint.Bounds,
                sourceTiles: sourceTiles)
            {
                OwnerPlayerIndex = player.whoAmI,
            };
            projection.Define(new Rectangle(origin.X, origin.Y, blueprint.Bounds.Width, blueprint.Bounds.Height));

            // Synchronize the selected tiles with the blueprint
            for (int i = 0; i < blueprint.Tiles.Count; i++)
            {
                var t = blueprint.Tiles[i];
                if (projection.TryGetTileByRelative(t.Relative, out var pTile))
                {
                    pTile.Selected = t.Selected;
                }
            }

            // Load the payload from the world
            projection.LoadPayloadFromWorld();

            // Add the projection to the player's list and set the active projection
            var state = GetState(player);
            state.Projections.Add(projection);
            state.ActiveProjection = projection;
            projection.UpdateOverlay();
            return projection;
        }

        /// <summary>
        /// Rebuilds projection overlay entries for this player (client only).
        /// </summary>
        internal void UpdateProjectionOverlay(Player player)
        {
            int who = player.whoAmI;
            string family = $"projection:{who}";

            // Clear the previous overlay
            OverlaySystem.UnregisterFamily(family);

            // For each projection, draw the bounds and the unselected tiles
            IReadOnlyList<Projection> list = GetProjections(player);
            for (int i = 0; i < list.Count; i++)
            {
                // If projection is defined, draw the bounds
                Projection proj = list[i];
                if (!proj.Bounds.IsEmpty)
                {
                    // CUT vs COPY: border color. Locked: outline only (transparent interior); unlocked: border + tinted fill.
                    Color c = proj.CutSource ? Color.Red : Color.LimeGreen;
                    if (proj.IsLocked)
                        OverlaySystem.RegTileBorder($"{i}:b", proj.Bounds, c, 2, family);
                    else
                        OverlaySystem.RegTileBounds($"{i}:b", proj.Bounds, c, 2, family);

                    if (proj.CutSource)
                    {
                        if (proj.IsLocked)
                            OverlaySystem.RegTileBorder($"{i}:s", proj.SourceArea, Color.Gray, 2, family);
                        else
                            OverlaySystem.RegTileBounds($"{i}:s", proj.SourceArea, Color.Gray, 2, family);
                    }
                }
            }

            ModContent.GetInstance<SelectionSystem>().UpdateDeselectionOverlays(player);
        }

        public void ClearActiveProjection(Player player)
        {
            var state = GetState(player);
            if (state.ActiveProjection is null)
            {
                return;
            }

            var toRemove = state.ActiveProjection;
            state.Projections.Remove(state.ActiveProjection);
            state.ActiveProjection = null;
            ModContent.GetInstance<ProjectionControl>().InvalidateGestureIfDragging(player, toRemove);
            UpdateProjectionOverlay(player);
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

        private static void RemoveProjectionAtIndex(Player player, PlayerProjectionState state, int index)
        {
            Projection p = state.Projections[index];

            if (ReferenceEquals(state.ActiveProjection, p))
            {
                state.ActiveProjection = null;
            }

            state.Projections.RemoveAt(index);
            ModContent.GetInstance<ProjectionControl>().InvalidateGestureIfDragging(player, p);
            ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(player);
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

            RemoveProjectionAtIndex(player, state, index);
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

        public bool TryGetProjectionByCorrelationId(Player player, int correlationId, out Projection projection)
        {
            var state = GetState(player);
            for (int i = state.Projections.Count - 1; i >= 0; i--)
            {
                var p = state.Projections[i];
                if (p.Id == correlationId)
                {
                    projection = p;
                    return true;
                }
            }

            projection = null;
            return false;
        }

        public int CountPendingOperations(Player player)
        {
            var state = GetState(player);
            int count = 0;
            for (int i = 0; i < state.Projections.Count; i++)
            {
                if (state.Projections[i].Id > 0)
                {
                    count++;
                }
            }
            return count;
        }

        public bool ToggleCutSource(Player player, Point cursorTile, out Projection projection)
        {
            var state = GetState(player);
            int index = FindTopmostIndexAt(state, cursorTile);
            if (index >= 0)
            {
                projection = state.Projections[index];
                if (projection.IsLocked)
                {
                    return false;
                }

                projection.ToggleCut();
                state.ActiveProjection = projection;
                return true;
            }

            projection = null;
            return false;
        }

        public override void OnWorldUnload()
        {
            for (int i = 0; i < _playerStates.Length; i++)
            {
                if (_playerStates[i] is not { } state)
                    continue;

                state.Projections.Clear();
                state.ActiveProjection = null;
            }

            ModContent.GetInstance<ProjectionControl>().ClearAllGestureSlots();
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
