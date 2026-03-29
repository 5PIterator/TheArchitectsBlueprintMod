using System;
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
            Blueprint blueprint = blueprintSystem.GetActiveBlueprint(player);
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
        }

        public void Update(Player player, Point cursorTile)
        {
            var state = GetState(player);
            if (!state.IsActive)
            {
                return;
            }

            state.Current = cursorTile;
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
                return;
            }

            Rectangle rect = Blueprint.FromPoints(state.Anchor, state.Current);

            switch (state.CurrentAction)
            {
                case ToolActionManager.ToolAction.ADD:
                    target.SelectTiles(rect);
                    break;

                case ToolActionManager.ToolAction.REMOVE:
                    target.DeselectTiles(rect);
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected edit action: {state.CurrentAction}");
            }

            state.IsActive = false;
        }

        public void Cancel(Player player)
        {
            var state = GetState(player);
            state.IsActive = false;
            state.Target = null;
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

    }
}
