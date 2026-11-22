using System;
using System.Collections.Generic;
using TheArchitectsBlueprintMod.Core.Architect;
using TheArchitectsBlueprintMod.Core.Layers;
using TheArchitectsBlueprintMod.Core.Preview;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.UI;
using TheArchitectsBlueprintMod.UI.Style;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TheArchitectsBlueprintMod.Core
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
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug(
                        $"ProjectionSystem.NewProjection(cursor): no selection under cursor owner={player.whoAmI} tile=({cursorTile.X},{cursorTile.Y})",
                        "ProjectionSystem");
                return null;
            }

            if (!selection.IsDefined || !selection.Area.Contains(cursorTile))
            {
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug(
                        $"ProjectionSystem.NewProjection(cursor): selection invalid or cursor outside owner={player.whoAmI} " +
                        $"defined={selection.IsDefined} cursor=({cursorTile.X},{cursorTile.Y}) area={selection.Area}",
                        "ProjectionSystem");
                return null;
            }

            if (!BlueprintGuard.TryValidateIncludedTiles(player, selection, out _))
                return null;

            var projection = new Projection(player.whoAmI, selection, cutSource: false);

            // Load the payload from the world
            projection.LoadPayloadFromWorld();
            BlueprintSystem.RegisterBlueprint(projection);
            projection.UpdateOverlay();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"ProjectionSystem.NewProjection(cursor): created id={projection.Id} owner={player.whoAmI} " +
                    $"area=({projection.Area.X},{projection.Area.Y}) {projection.Area.Width}x{projection.Area.Height}",
                    "ProjectionSystem");
            return projection;
        }
        /// <summary>
        /// Creates a projection from a selection; returns null on failure.
        /// Adds the projection to the player's list and sets <see cref="ActiveProjection"/>.
        /// </summary>
        public Projection NewProjection(Player player, Selection selection, bool copyWorldPayload = true)
        {
            ArgumentNullException.ThrowIfNull(selection);

            if (copyWorldPayload && !BlueprintGuard.TryValidateIncludedTiles(player, selection, out _))
                return null;

            var projection = new Projection(player.whoAmI, selection, cutSource: false);

            if (copyWorldPayload)
                projection.LoadPayloadFromWorld();
            BlueprintSystem.RegisterBlueprint(projection);
            projection.UpdateOverlay();
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"ProjectionSystem.NewProjection(selection): created id={projection.Id} owner={player.whoAmI} " +
                    $"area=({projection.Area.X},{projection.Area.Y}) {projection.Area.Width}x{projection.Area.Height}",
                    "ProjectionSystem");
            return projection;
        }

        /// <summary>
        /// Removes a projection from the player's list and clears the overlay.
        /// </summary>
        public bool RemoveProjection(Projection projection)
        {
            if (projection == null)
            {
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug("ProjectionSystem.RemoveProjection: null projection", "ProjectionSystem");
                return false;
            }

            if (projection.IsLocked)
            {
                if (TABLog.IsDebugEnabled)
                    TABLog.Debug(
                        $"ProjectionSystem.RemoveProjection: refused (locked) id={projection.Id} owner={projection.Owner}",
                        "ProjectionSystem");
                return false;
            }

            PayloadPreview.InvalidateMapTexture(projection.OverlayInstanceKey);
            ProjectionPreviewCache.Invalidate(projection.OverlayInstanceKey);
            BlueprintSystem.UnregisterBlueprint(projection);
            ProjectionControl.InvalidateGestureIfDragging(projection);
            UpdateProjectionOverlay(Main.player[projection.Owner]);
            if (TABLog.IsDebugEnabled)
                TABLog.Debug(
                    $"ProjectionSystem.RemoveProjection: unregistered id={projection.Id} owner={projection.Owner}",
                    "ProjectionSystem");
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
                int invIndex = 0;
                foreach (Rectangle inventory in inventories)
                {
                    Color c = Palette.InventoryOutlineColor;
                    string invKey = $"c:{proj.OverlayInstanceKey}:{invIndex}";
                    if (locked)
                        OverlaySystem.RegTileBorder(invKey, inventory, c, 2, family);
                    else
                        OverlaySystem.RegTileBounds(invKey, inventory, c, 2, family);
                    invIndex++;
                }
            }
        }

        /// <summary>
        /// Rebuilds projection overlay entries for this player (client only).
        /// </summary>
        internal void UpdateProjectionOverlay(Player player)
        {
            // --- Dedicated server has no overlay catalog; paste reconstruction must not touch it.

            if (Main.dedServ)
                return;

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
                    string k = proj.OverlayInstanceKey.ToString();

                    // Sprites first so projection tint/borders (registered after) stay visible on top.
                    // Cached blit stays on while dragging; uncached huge payloads still skip during drag.
                    bool skipSpritePreview = ModContent.GetInstance<ProjectionControl>().IsActivelyDragging(player)
                        && proj.HasPayload
                        && proj.Payload.Width * proj.Payload.Height > 50_000
                        && !ProjectionPreviewCache.IsCachedBlitEnabled();
                    if (!proj.IsLocked && proj.HasPayload && !skipSpritePreview)
                    {
                        Projection projectionForPreview = proj;
                        OverlaySystem.RegScreen($"{k}:sprites", () => PayloadPreview.Draw(projectionForPreview), family);
                        OverlaySystem.RegMapPayloadPreview($"{k}:mapPixels", proj, family);
                    }

                    // CUT vs COPY: border color. Locked: outline only (transparent interior); unlocked: border + tinted fill.
                    Color c = proj.CutSource ? Palette.ProjectionCutColor : Palette.ProjectionCopyColor;
                    if (proj.IsLocked)
                        OverlaySystem.RegTileBorder($"{k}:b", proj.Area, c, 2, family);
                    else
                        OverlaySystem.RegTileBounds($"{k}:b", proj.Area, c, 2, family);

                    if (proj.CutSource)
                    {
                        if (proj.IsLocked)
                            OverlaySystem.RegTileBorder($"{k}:s", proj.SourceArea, Palette.ProjectionSourceCutColor, 2, family);
                        else
                            OverlaySystem.RegTileBounds($"{k}:s", proj.SourceArea, Palette.ProjectionSourceCutColor, 2, family);
                    }

                    BlueprintGuard.RegisterGuardMask(player, proj, $"{k}:guarded", family);
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
                        OverlaySystem.RegTileBorder("payloadIo", payloadIo.Area, Palette.PayloadIoHighlightColor, 3, family);
                        break;
                    }
                }
            }

            ModContent.GetInstance<ArchitectSystem>().Toolbox.RegisterToolbox(player);
            ModContent.GetInstance<InclusionControl>().UpdateExcludedTileOverlays(player);
            UpdateInventoryOverlay(player);
        }

        public override void OnWorldUnload()
        {
            int n = 0;
            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                BlueprintSystem.UnregisterBlueprint(proj);
                proj.Clear();
                n++;
            }

            PayloadPreview.ClearMapTextureCache();
            ProjectionPreviewCache.Clear();
            LayerSystem.ClearPipelineCache();

            if (TABLog.IsDebugEnabled && n > 0)
                TABLog.Debug($"ProjectionSystem.OnWorldUnload: cleared {n} projection(s)", "ProjectionSystem");

            ModContent.GetInstance<ProjectionControl>().ClearAllGestureSlots();
        }
    }
}
