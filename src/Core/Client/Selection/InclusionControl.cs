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
    /// Gesture handler for INCLUDE/EXCLUDE rectangle edits on a player's Selection or Projection.
    /// Driven by <see cref="ToolActionManager"/> via <see cref="BeginInc"/>/<see cref="BeginExc"/>;
    /// hold/release/cancel routing matches <see cref="SelectionControl"/> and <see cref="ProjectionControl"/>.
    /// <para/>
    /// In-progress gestures can be canceled via <see cref="CancelGesture"/> (e.g. tool right-click).
    /// </summary>
    public sealed class InclusionControl : ModSystem
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        private enum Mode { None, Include, Exclude }

        private sealed class PlayerState
        {
            public Mode Mode;
            public Point Anchor;
            public Point Current;
            public Blueprint Target;
        }

        private readonly PlayerState[] _states = new PlayerState[Main.maxPlayers];

        /// <summary>Begins an INCLUDE rectangle gesture (left click).</summary>
        public void BeginInc(Player player, Point cursorTile) => BeginGesture(player, Mode.Include, cursorTile);

        /// <summary>Begins an EXCLUDE rectangle gesture (left click).</summary>
        public void BeginExc(Player player, Point cursorTile) => BeginGesture(player, Mode.Exclude, cursorTile);

        private void BeginGesture(Player player, Mode mode, Point cursorTile)
        {
            if (!BlueprintSystem.TryGetTopmostBlueprintAt(player, cursorTile, out Blueprint? target))
                return;

            var state = GetState(player);
            state.Mode = mode;
            state.Target = target;
            // Anchor/Current stored in tile coordinates; converted to a rectangle on demand.
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
                ClearGestureOverlay(player);
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);

            if (state.Mode == Mode.Include)
                ApplyIncludeWithVolumeLimit(target, rect);
            else
                target.ExcludeTiles(rect);

            state.Mode = Mode.None;
            state.Target = null;
            ClearGestureOverlay(player);
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
            ClearGestureOverlay(player);
        }

        public override void OnWorldUnload()
        {
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] is not { } state)
                    continue;

                state.Mode = Mode.None;
                state.Anchor = default;
                state.Current = default;
                state.Target = null;
            }
        }

        /// <summary>
        /// Rebuilds semi-transparent black fills for excluded projection tiles (separate overlay family from projection bounds).
        /// </summary>
        internal void UpdateExcludedTileOverlays(Player player)
        {
            int who = player.whoAmI;
            string family = $"projection:deselect:{who}";
            OverlaySystem.UnregisterFamily(family);

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            IReadOnlyList<Projection> list = projectionSystem.GetProjections(player);
            for (int i = 0; i < list.Count; i++)
            {
                Point[] excluded = list[i].BuildExcludedTilePoints();
                if (excluded.Length > 0)
                    OverlaySystem.RegTileFill($"{i}:u", excluded, Color.Black * 0.25f, family);
            }
        }

        private PlayerState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            int index = player.whoAmI;
            if (index < 0 || index >= _states.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            return _states[index] ??= new PlayerState();
        }

        private void RefreshGestureOverlay(Player player)
        {
            var state = GetState(player);
            if (state.Mode == Mode.None || state.Target is null || !state.Target.IsDefined)
            {
                ClearGestureOverlay(player);
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);
            Rectangle targetBounds = state.Target.Area;
            Color c = state.Mode == Mode.Include
                ? ArchitectVisualPalette.IncludePreviewColor
                : ArchitectVisualPalette.ExcludePreviewColor;
            Rectangle clamped = Rectangle.Intersect(targetBounds, rect);
            int who = player.whoAmI;
            OverlaySystem.RegTileBounds("preview", clamped, c, 2, $"inclusion:{who}");
        }

        private static void ClearGestureOverlay(Player player)
        {
            OverlaySystem.UnregisterFamily($"inclusion:{player.whoAmI}");
        }

        private static void UpdateTargetOverlay(Player player, Blueprint target)
        {
            var selectionSystem = ModContent.GetInstance<SelectionSystem>();
            if (ReferenceEquals(target, selectionSystem.GetSelection(player)))
                selectionSystem.UpdateSelectionOverlay(player);
            else
                ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(player);
        }

        private static void ApplyIncludeWithVolumeLimit(Blueprint target, Rectangle tileRect)
        {
            var config = ModContent.GetInstance<PastingConfig>();
            int volumeLimit = config.VolumeLimit;
            if (volumeLimit <= 0 || target.Area.IsEmpty)
                return;

            int count = CountIncludedTiles(target);
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

        private static int CountIncludedTiles(Blueprint target)
        {
            int count = 0;
            foreach (var t in target.Tiles)
            {
                if (t.Included)
                    count++;
            }
            return count;
        }
    }
}
