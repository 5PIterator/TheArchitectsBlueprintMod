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
    /// Manages tile-selection edits (ADD/REMOVE) within a player's Blueprint (and later, Projections).
    /// Gesture model: LeftClick begins (sets point A), LeftHold updates preview rectangle,
    /// LeftRelease applies, RightClick cancels.
    /// </summary>
    public sealed class SelectionSystem : ModSystem
    {
        private sealed class PlayerEditState
        {
            public bool IsActive;
            public Point Anchor;
            public Point Current;
            public ToolActionManager.ToolAction CurrentAction;
            public Blueprint Target;
        }

        private readonly PlayerEditState[] _editStates = new PlayerEditState[Main.maxPlayers];

        public void Begin(Player player, ToolActionManager.ToolAction action, Point cursorTile)
        {
            if (action != ToolActionManager.ToolAction.ADD && action != ToolActionManager.ToolAction.REMOVE)
            {
                throw new ArgumentOutOfRangeException(nameof(action), action, "SelectionSystem only supports Add/Remove actions.");
            }

            var blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            Blueprint blueprint = blueprintSystem.GetBlueprint(player);
            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            Blueprint target = blueprint;
            if (projectionSystem.TryGetProjectionAt(player, cursorTile, out var projection))
            {
                target = projection;
            }

            if (!target.IsDefined)
            {
                return;
            }

            var state = GetState(player);
            state.IsActive = true;
            state.CurrentAction = action;
            state.Target = target;
            // Anchor/Current are stored in tile coordinates; we convert to a rectangle on demand.
            state.Anchor = cursorTile;
            state.Current = cursorTile;
            UpdateGestureOverlay(player);
        }

        public void Update(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (!state.IsActive)
            {
                return;
            }

            state.Current = cursorTile;
            UpdateGestureOverlay(player);
        }

        public void Apply(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (!state.IsActive)
            {
                return;
            }

            state.Current = cursorTile;

            Blueprint target = state.Target;
            if (target is null || !target.IsDefined)
            {
                state.IsActive = false;
                ClearGestureOverlay(player);
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);

            switch (state.CurrentAction)
            {
                case ToolActionManager.ToolAction.ADD:
                    ApplyAddWithVolumeLimit(target, rect);
                    break;

                case ToolActionManager.ToolAction.REMOVE:
                    target.DeselectTiles(rect);
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected edit action: {state.CurrentAction}");
            }

            state.IsActive = false;
            ClearGestureOverlay(player);
            UpdateTargetOverlay(player, target);
        }

        public void Cancel(Player player)
        {
            var state = GetState(player);
            state.IsActive = false;
            state.Target = null;
            ClearGestureOverlay(player);
        }

        public bool TryGetPreview(Player player, out ToolActionManager.ToolAction action, out Rectangle rect, out Rectangle targetBounds)
        {
            var state = GetState(player);
            if (!state.IsActive)
            {
                action = default;
                rect = Rectangle.Empty;
                targetBounds = Rectangle.Empty;
                return false;
            }

            action = state.CurrentAction;
            rect = Blueprint.FromPoints(state.Anchor, state.Current);
            targetBounds = state.Target?.Bounds ?? Rectangle.Empty;
            return true;
        }

        public override void OnWorldUnload()
        {
            for (int i = 0; i < _editStates.Length; i++)
            {
                if (_editStates[i] is not { } state)
                    continue;

                state.IsActive = false;
                state.Anchor = default;
                state.Current = default;
                state.CurrentAction = default;
                state.Target = null;
            }
        }

        /// <summary>
        /// Rebuilds semi-transparent black fills for deselected projection tiles (separate overlay family from projection bounds).
        /// </summary>
        internal void UpdateDeselectionOverlays(Player player)
        {
            int who = player.whoAmI;
            string family = $"projection:deselect:{who}";
            OverlaySystem.UnregisterFamily(family);

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            IReadOnlyList<Projection> list = projectionSystem.GetProjections(player);
            for (int i = 0; i < list.Count; i++)
            {
                Point[] unsel = list[i].BuildUnselectedTilePoints();
                if (unsel.Length > 0)
                    OverlaySystem.RegTileFill($"{i}:u", unsel, Color.Black * 0.25f, family);
            }
        }

        private PlayerEditState GetState(Player player)
        {
            ArgumentNullException.ThrowIfNull(player);

            int index = player.whoAmI;
            if (index < 0 || index >= _editStates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
            }

            return _editStates[index] ??= new PlayerEditState();
        }

        private void UpdateGestureOverlay(Player player)
        {
            var state = GetState(player);
            if (!state.IsActive || state.Target is null || !state.Target.IsDefined)
            {
                ClearGestureOverlay(player);
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);
            Rectangle targetBounds = state.Target.Bounds;
            Color c = state.CurrentAction == ToolActionManager.ToolAction.ADD ? Color.LimeGreen : Color.Red;
            Rectangle clamped = Rectangle.Intersect(targetBounds, rect);
            int who = player.whoAmI;
            OverlaySystem.RegTileBounds("preview", clamped, c, 2, $"selection:{who}");
        }

        private static void ClearGestureOverlay(Player player)
        {
            OverlaySystem.UnregisterFamily($"selection:{player.whoAmI}");
        }

        private static void UpdateTargetOverlay(Player player, Blueprint target)
        {
            var blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            if (ReferenceEquals(target, blueprintSystem.GetBlueprint(player)))
                blueprintSystem.UpdateBlueprintOverlay(player);
            else
                ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(player);
        }

        private static void ApplyAddWithVolumeLimit(Blueprint target, Rectangle tileRect)
        {
            var config = ModContent.GetInstance<PastingConfig>();
            int volumeLimit = config.VolumeLimit;
            if (volumeLimit <= 0 || target.Bounds.IsEmpty)
            {
                return;
            }

            int selected = CountSelectedTiles(target);
            if (selected >= volumeLimit)
            {
                return;
            }

            Rectangle area = Rectangle.Intersect(target.Bounds, tileRect);
            if (area.IsEmpty)
            {
                return;
            }

            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    if (!target.TryGetTile(new Point(x, y), out var boundedTile) || boundedTile.Selected)
                    {
                        continue;
                    }

                    boundedTile.Selected = true;
                    selected++;
                    if (selected >= volumeLimit)
                    {
                        return;
                    }
                }
            }
        }

        private static int CountSelectedTiles(Blueprint target)
        {
            int selected = 0;
            for (int i = 0; i < target.Tiles.Count; i++)
            {
                if (target.Tiles[i].Selected)
                {
                    selected++;
                }
            }
            return selected;
        }

    }
}
