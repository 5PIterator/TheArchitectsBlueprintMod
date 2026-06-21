using System;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Gesture handler for INCLUDE/EXCLUDE rectangle edits on the client <see cref="Selection"/> or <see cref="Projection"/>.
    /// Driven by <see cref="ToolActionManager"/> via <see cref="BeginInc"/>/<see cref="BeginExc"/>;
    /// hold/release/cancel routing matches <see cref="SelectionControl"/> and <see cref="ProjectionControl"/>.
    /// <para/>
    /// In-progress gestures can be canceled via <see cref="CancelGesture"/> (e.g. tool right-click).
    /// </summary>
    public sealed class InclusionControl : ModSystem
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        private enum Mode { None, Include, Exclude }

        private const string InclusionGestureFamily = "inclusion:gesture";
        private const string ProjectionExcludedTilesFamily = "projection:excluded-tiles";

        private sealed class GestureState
        {
            public Mode Mode;
            public Point Anchor;
            public Point Current;
            public Blueprint Target;
        }

        private readonly GestureState _gesture = new();

        /// <summary>Begins an INCLUDE rectangle gesture (left click).</summary>
        public void BeginInc(Player player, Point cursorTile) => BeginGesture(player, Mode.Include, cursorTile);

        /// <summary>Begins an EXCLUDE rectangle gesture (left click).</summary>
        public void BeginExc(Player player, Point cursorTile) => BeginGesture(player, Mode.Exclude, cursorTile);

        private void BeginGesture(Player player, Mode mode, Point cursorTile)
        {
            if (!BlueprintSystem.GetTopmostBlueprintAt(cursorTile, out Blueprint target))
                return;

            var state = GetState(player);
            state.Mode = mode;
            state.Target = target;
            state.Anchor = cursorTile;
            state.Current = cursorTile;
            RefreshGestureOverlay(player);
        }

        /// <summary>Updates the preview rectangle while the left button is held.</summary>
        public void UpdateGesture(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode == Mode.None)
                return;

            state.Current = cursorTile;
            RefreshGestureOverlay(player);
        }

        /// <summary>Applies the gesture and clears the preview (left release).</summary>
        public void EndGesture(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (state.Mode == Mode.None)
                return;

            state.Current = cursorTile;

            Blueprint target = state.Target;
            if (target is null || !target.IsDefined)
            {
                state.Mode = Mode.None;
                state.Target = null;
                ClearGestureOverlay();
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);

            if (state.Mode == Mode.Include)
                ApplyIncludeWithVolumeLimit(target, rect);
            else
                target.ExcludeTiles(rect);

            state.Mode = Mode.None;
            state.Target = null;
            ClearGestureOverlay();
            UpdateTargetOverlay(player, target);
        }

        /// <summary>Cancels an in-progress gesture without applying (right-click cancel or tool switch).</summary>
        public void CancelGesture(Player player)
        {
            var state = GetState(player);
            if (state.Mode == Mode.None)
                return;

            state.Mode = Mode.None;
            state.Target = null;
            ClearGestureOverlay();
        }

        public override void OnWorldUnload()
        {
            _gesture.Mode = Mode.None;
            _gesture.Anchor = default;
            _gesture.Current = default;
            _gesture.Target = null;
        }

        /// <summary>
        /// Rebuilds semi-transparent black fills for excluded projection tiles (separate overlay family from projection bounds).
        /// </summary>
        internal void UpdateExcludedTileOverlays(Player player)
        {
            _ = player;
            OverlaySystem.UnregisterFamily(ProjectionExcludedTilesFamily);

            foreach (Projection proj in BlueprintSystem.GetProjections())
            {
                Point[] excluded = proj.BuildExcludedTilePoints();
                if (excluded.Length > 0)
                    OverlaySystem.RegTileFill($"{proj.OverlayInstanceKey}:u", excluded, Color.Black * 0.25f, ProjectionExcludedTilesFamily);
            }
        }

        private GestureState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            if (player.whoAmI != Main.myPlayer || Main.gameMenu)
            {
                throw new InvalidOperationException("Player is not valid");
            }

            return _gesture;
        }

        private void RefreshGestureOverlay(Player player)
        {
            var state = GetState(player);
            if (state.Mode == Mode.None || state.Target is null || !state.Target.IsDefined)
            {
                ClearGestureOverlay();
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);
            Rectangle targetBounds = state.Target.Area;
            Color c = state.Mode == Mode.Include
                ? ArchitectVisualPalette.IncludePreviewColor
                : ArchitectVisualPalette.ExcludePreviewColor;
            Rectangle clamped = Rectangle.Intersect(targetBounds, rect);
            OverlaySystem.RegTileBounds("preview", clamped, c, 2, InclusionGestureFamily);
        }

        private static void ClearGestureOverlay()
        {
            OverlaySystem.UnregisterFamily(InclusionGestureFamily);
        }

        private static void UpdateTargetOverlay(Player player, Blueprint target)
        {
            var selectionSystem = ModContent.GetInstance<SelectionSystem>();
            if (ReferenceEquals(target, BlueprintSystem.GetTopmostSelection()))
                selectionSystem.UpdateSelectionOverlay(player);
            else
                ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(player);
        }

        private static void ApplyIncludeWithVolumeLimit(Blueprint target, Rectangle tileRect)
        {
            var config = ModContent.GetInstance<PastingConfig>();
            int volumeLimit = config.Limits.VolumeLimit;
            if (volumeLimit <= 0 || target.Area.IsEmpty)
                return;

            int count = PasteLimits.CountIncludedVolume(target);
            if (count >= volumeLimit)
                return;

            Rectangle area = Rectangle.Intersect(target.Area, tileRect);
            if (area.IsEmpty)
                return;

            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    if (!target.TryGetTile(new Point(x, y), out var boundedTile) || boundedTile.Included)
                        continue;

                    boundedTile.Included = true;
                    count++;
                    if (count >= volumeLimit)
                        return;
                }
            }
        }

        private static int CountIncludedTiles(Blueprint target) => PasteLimits.CountIncludedVolume(target);
    }
}
