using System;
using System.Collections.Generic;
using ArchitectsStencilMod.UI;
using ArchitectsStencilMod.UIKit;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ObjectData;

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

        /// <summary>Returns the front-most defined projection for a player, if one exists.</summary>
        public bool TryGetFrontmostProjection(Player player, out Projection projection)
        {
            // --- Walk the projection list from topmost to backmost and return the first defined projection.

            var state = GetState(player);
            for (int i = state.Projections.Count - 1; i >= 0; i--)
            {
                Projection candidate = state.Projections[i];
                if (candidate.IsDefined)
                {
                    projection = candidate;
                    return true;
                }
            }

            projection = null;
            return false;
        }

        /// <summary>Topmost projection whose bounds contain <paramref name="cursorTile"/>, if any.</summary>
        public bool TryGetProjectionAt(Player player, Point cursorTile, out Projection? projection)
        {
            var state = GetState(player);
            int index = FindTopmostIndexAt(state, cursorTile);
            if (index < 0)
            {
                projection = null;
                return false;
            }

            projection = state.Projections[index];
            return true;
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
        /// Creates a projection from the player's selection when the cursor lies inside it; returns null on failure.
        /// Adds the projection to the player's list and sets <see cref="ActiveProjection"/>.
        /// </summary>
        public Projection NewProjection(Player player, Point cursorTile)
        {
            // Get the selection from the selection system
            var selectionSystem = ModContent.GetInstance<SelectionSystem>();
            Selection selection = selectionSystem.GetSelection(player);

            if (!selection.IsDefined || !selection.Area.Contains(cursorTile))
            {
                return null;
            }

            var projection = new Projection(selection, cutSource: false);

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
            UIView host = InterfaceSystem.GetPerPlayerHost(InterfaceLayer.ProjectionWorld, who);
            host.RemoveAllChildren();

            IReadOnlyList<Projection> list = GetProjections(player);
            for (int i = 0; i < list.Count; i++)
            {
                Projection proj = list[i];
                if (!proj.Area.IsEmpty)
                {
                    // CUT vs COPY: border color. Locked: outline only (transparent interior); unlocked: border + tinted fill.
                    Color c = proj.CutSource ? ArchitectVisualPalette.ProjectionCutColor : ArchitectVisualPalette.ProjectionCopyColor;
                    if (proj.IsLocked)
                        host.AddChild(new UIBorderTile(proj.Area, c, 2));
                    else
                        host.AddChild(new UIWorldRectFill(proj.Area, c * 0.3f));

                    if (proj.CutSource)
                    {
                        if (proj.IsLocked)
                            host.AddChild(new UIBorderTile(proj.SourceArea, ArchitectVisualPalette.ProjectionSourceCutColor, 2));
                        else
                            host.AddChild(new UIBoundsTile(proj.SourceArea, ArchitectVisualPalette.ProjectionSourceCutColor, 2));
                    }
                }
            }

            ModContent.GetInstance<InclusionControl>().UpdateExcludedTileOverlays(player);
            UpdateInventoryOverlay(player);
        }

        /// <summary>
        /// Draws outlines for each rectangle in <see cref="Projection.Inventories"/>.
        /// </summary>
        private void UpdateInventoryOverlay(Player player)
        {
            int who = player.whoAmI;
            UIView host = InterfaceSystem.GetPerPlayerHost(InterfaceLayer.InventoryOutline, who);
            host.RemoveAllChildren();

            IReadOnlyList<Projection> list = GetProjections(player);
            for (int i = 0; i < list.Count; i++)
            {
                bool locked = list[i].IsLocked;
                IReadOnlyList<Rectangle> inventories = list[i].Inventories;
                Color c = ArchitectVisualPalette.InventoryOutlineColor;
                for (int j = 0; j < inventories.Count; j++)
                {
                    if (locked)
                        host.AddChild(new UIBorderTile(inventories[j], c, 2));
                    else
                        host.AddChild(new UIBoundsTile(inventories[j], c, 2));
                }
            }
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
                if (p.IsDefined && p.Area.Contains(cursorTile))
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
