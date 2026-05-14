using System;
using System.Collections.Generic;
using ArchitectsBlueprintMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Owns each player's projection list and <see cref="ActiveProjection"/>.
    /// Interactive COPY gestures (drag, cancel) are handled by <see cref="ProjectionControl"/>.
    /// </summary>
    public sealed class ProjectionSystem : ModSystem
    {

        private BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        private ProjectionControl ProjectionControl => ModContent.GetInstance<ProjectionControl>();

        /// <summary>
        /// Creates a projection from the player's selection when the cursor lies inside it; returns null on failure.
        /// Adds the projection to the player's list and sets <see cref="ActiveProjection"/>.
        /// </summary>
        public Projection NewProjection(Player player, Point cursorTile)
        {
            // Get the selection from the selection system
            if (!BlueprintSystem.GetTopmostSelectionAt(cursorTile, out Selection selection))
            {
                return null;
            }

            if (!selection.IsDefined || !selection.Area.Contains(cursorTile))
            {
                return null;
            }

            var projection = new Projection(player.whoAmI, selection, cutSource: false);

            // Load the payload from the world
            projection.LoadPayloadFromWorld();
            BlueprintSystem.RegisterBlueprint(projection);
            projection.UpdateOverlay();
            return projection;
        }

        public bool RemoveProjectionAt(Player player, Point cursorTile)
        {
            if (!BlueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection projection))
            {
                return false;
            }

            if (projection.IsLocked)
            {
                return false;
            }

            ProjectionControl.InvalidateGestureIfDragging(player, projection);
            UpdateProjectionOverlay(player);
            BlueprintSystem.UnregisterBlueprint(projection);
            return true;
        }

        public bool GetProjectionByCorrelationId(int correlationId, out Projection projection)
        {
            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                if (proj.Id == correlationId)
                {
                    projection = proj;
                    return true;
                }
            }
            projection = null;
            return false;
        }

        public int CountPendingOperations()
        {
            int count = 0;
            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                if (proj.Id > 0)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Toggles the cut source for the projection at the cursor tile. Returns true if the cut source was toggled, false otherwise.
        /// </summary>
        public bool ToggleCutSource(Point cursorTile)
        {
            if (!BlueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection projection))
            {
                return false;
            }
            return projection.ToggleCut();
        }

        /// <summary>
        /// Draws outlines for each rectangle in <see cref="Projection.Inventories"/>.
        /// </summary>
        private void UpdateInventoryOverlay(Player player)
        {
            int who = player.whoAmI;
            string family = $"inventory:{who}";
            OverlaySystem.UnregisterFamily(family);

            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                bool locked = proj.IsLocked;
                IEnumerable<Rectangle> inventories = proj.Inventories;
                foreach (Rectangle inventory in inventories)
                {
                    Color c = ArchitectVisualPalette.InventoryOutlineColor;
                    if (locked)
                        OverlaySystem.RegTileBorder($"c:{proj.Id}", inventory, c, 2, family);
                    else
                        OverlaySystem.RegTileBounds($"c:{proj.Id}", inventory, c, 2, family);
                }
            }
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
            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                // If projection is defined, draw the bounds
                if (!proj.Area.IsEmpty)
                {
                    // CUT vs COPY: border color. Locked: outline only (transparent interior); unlocked: border + tinted fill.
                    Color c = proj.CutSource ? ArchitectVisualPalette.ProjectionCutColor : ArchitectVisualPalette.ProjectionCopyColor;
                    if (proj.IsLocked)
                        OverlaySystem.RegTileBorder($"{proj.Id}:b", proj.Area, c, 2, family);
                    else
                        OverlaySystem.RegTileBounds($"{proj.Id}:b", proj.Area, c, 2, family);

                    if (proj.CutSource)
                    {
                        if (proj.IsLocked)
                            OverlaySystem.RegTileBorder($"{proj.Id}:s", proj.SourceArea, ArchitectVisualPalette.ProjectionSourceCutColor, 2, family);
                        else
                            OverlaySystem.RegTileBounds($"{proj.Id}:s", proj.SourceArea, ArchitectVisualPalette.ProjectionSourceCutColor, 2, family);
                    }
                }
            }

            if (PayloadSerialization.TryGetPayloadIoHighlight(player, out Projection? payloadIo) &&
                payloadIo is not null &&
                payloadIo.IsDefined &&
                !payloadIo.Area.IsEmpty)
            {
                foreach (Projection proj in BlueprintSystem.GetProjections())
                {
                    if (ReferenceEquals(proj, payloadIo))
                    {
                        OverlaySystem.RegTileBorder("payloadIo", payloadIo.Area, ArchitectVisualPalette.PayloadIoHighlightColor, 3, family);
                        break;
                    }
                }
            }

            ModContent.GetInstance<InclusionControl>().UpdateExcludedTileOverlays(player);
            UpdateInventoryOverlay(player);
        }

        public override void OnWorldUnload()
        {
            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                BlueprintSystem.UnregisterBlueprint(proj);
                proj.Clear();
            }

            ModContent.GetInstance<ProjectionControl>().ClearAllGestureSlots();
        }
    }
}
