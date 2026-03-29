using System;
using System.Collections;
using Microsoft.Xna.Framework;
using ArchitectsStencilMod.Debug;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Core routing point for "tool action" input.
    /// Tools provide:
    /// - a <see cref="ToolAction"/> (what they intend to do)
    /// - a <see cref="ToolActionKey"/> (what input gesture is occurring)
    /// - the cursor tile coordinate and player context
    ///
    /// This system does not (and should not) inspect the calling tool/item type.
    /// Tool behavior differences should be expressed entirely through the (action, key, modifiers) tuple.
    /// </summary>
    public sealed class ToolActionManager : ModSystem
    {
        /// <summary>
        /// High-level actions that tools can request.
        /// </summary>
        public enum ToolAction
        {
            /// Replaces the current Blueprint instance for the player.
            NEW = 0,
            /// PIN action: always starts a new Blueprint bounds selection.
            PIN = 1,
            /// SET action: interactively edits an existing Blueprint bounds selection (falls back to PIN if undefined).
            SET = 2,
            /// An action responsible for adding tiles to the selection of either Blueprint or Projection.
            ADD = 3,
            /// An action responsible for removing tiles from the selection of either Blueprint or Projection.
            REMOVE = 4,
            /// An action responsible for creating a Copy Projection of Blueprint and its position..
            COPY = 5,
            /// An action responsible for creating a Cut Projection of Blueprint and its position.
            CUT = 6,
            /// An action responsible for starting a pasting process.
            /// Pasting clears use <see cref="ArchitectsStencilMod.Utils.SuppressTileDrop"/> for no-item tile kills (multi-tile safe).
            PASTE = 7,
        }

        public static string GetActionId(ToolAction action)
        {
            // Stable identifier for UI entries / save data.
            return action.ToString().ToLowerInvariant();
        }

        public static string GetActionLabel(ToolAction action)
        {
            // Display label for UI. Kept simple for now; can be replaced with localization later.
            return action switch
            {
                ToolAction.NEW => "NEW",
                ToolAction.PIN => "PIN",
                ToolAction.SET => "SET",
                ToolAction.ADD => "ADD",
                ToolAction.REMOVE => "REMOVE",
                ToolAction.COPY => "COPY",
                ToolAction.CUT => "CUT",
                ToolAction.PASTE => "PASTE",
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
            };
        }

        /// <summary>
        /// Input gestures/tools can report to the manager.
        /// A tool may map the same physical button to different keys depending on its design.
        /// </summary>
        public enum ToolActionKey
        {
            LeftClick,
            LeftHold,
            LeftRelease,
            RightClick,
            RightHold,
            RightRelease
        }

        [Flags]
        public enum ToolActionModifier
        {
            None = 0,
            Reset = 1 << 0
        }

        // Paste behavior is implemented by PastingSystem.

        /// <summary>
        /// Execute a tool action for the given input.
        /// This method is intentionally thin: it routes input to the appropriate subsystem
        /// and owns the policy for which action maps to which subsystem calls.
        /// </summary>
        public void Handle(
            ToolAction action,
            ToolActionKey key,
            Player player,
            Point cursorTile,
            ToolActionModifier modifiers = ToolActionModifier.None)
        {
            try
            {
                if ((modifiers & ToolActionModifier.Reset) != 0)
                {
                    HandleResetModifiedAction(action, key, player, cursorTile);
                    return;
                }

                switch (action)
                {
                    case ToolAction.NEW:
                        ModContent.GetInstance<BlueprintSystem>().NewBlueprint(player);
                        return;

                    case ToolAction.PIN:
                        ModContent.GetInstance<BlueprintControl>().HandlePin(key, player, cursorTile);
                        return;

                    case ToolAction.SET:
                        ModContent.GetInstance<BlueprintControl>().HandleSet(key, player, cursorTile);
                        return;

                    case ToolAction.ADD:
                        HandleSelectionEdit(action, key, player, cursorTile);
                        return;

                    case ToolAction.REMOVE:
                        HandleSelectionEdit(action, key, player, cursorTile);
                        return;

                    case ToolAction.COPY:
                        HandleProjectionEdit(action, key, player, cursorTile);
                        return;

                    case ToolAction.CUT:
                        HandleProjectionEdit(action, key, player, cursorTile);
                        return;

                    case ToolAction.PASTE:
                        HandlePaste(key, player, cursorTile);
                        return;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
                }
            }
            catch (Exception ex)
            {
                // ToolActionManager is high-level routing logic: log and continue.
                // Lower-level systems are free to throw on invalid state; we decide whether to surface it.
                ASMLog.Error($"Tool action failed: action={action}, key={key}, cursor=({cursorTile.X},{cursorTile.Y}), player={player?.name}", ex, "ToolAction");
            }
        }

        private void HandleResetModifiedAction(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
        {
            if (key != ToolActionKey.RightClick && key != ToolActionKey.LeftClick)
            {
                return;
            }

            switch (action)
            {
                case ToolAction.PIN:
                case ToolAction.SET:
                    ModContent.GetInstance<BlueprintSystem>().ClearSelection(player);
                    return;

                case ToolAction.ADD:
                case ToolAction.REMOVE:
                    HandleResetStencilMask(action, player, cursorTile);
                    return;

                case ToolAction.COPY:
                    ModContent.GetInstance<ProjectionSystem>().RemoveProjectionAt(player, cursorTile, ProjectionKind.Copy);
                    return;

                case ToolAction.CUT:
                    ModContent.GetInstance<ProjectionSystem>().RemoveProjectionAt(player, cursorTile, ProjectionKind.Cut);
                    return;

                case ToolAction.PASTE:
                    if (ModContent.GetInstance<ProjectionSystem>().TryGetProjectionAt(player, cursorTile, out var stopProjection) && stopProjection is not null)
                    {
                        ModContent.GetInstance<PastingSystem>().StopPaste(stopProjection);
                    }
                    return;

                default:
                    // Ignore unsupported combinations; tools define which actions can be paired with Reset.
                    return;
            }
        }

        private static void HandleResetStencilMask(ToolAction action, Player player, Point cursorTile)
        {
            var blueprintSystem = ModContent.GetInstance<BlueprintSystem>();
            Blueprint target = blueprintSystem.GetActiveBlueprint(player);
            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            if (projectionSystem.TryGetProjectionAt(player, cursorTile, out var projection))
            {
                target = projection;
            }

            if (target is null || !target.IsDefined)
            {
                return;
            }

            switch (action)
            {
                case ToolAction.ADD:
                    target.SelectTiles(target.Bounds);
                    return;
                case ToolAction.REMOVE:
                    target.DeselectTiles(target.Bounds);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Expected ADD or REMOVE.");
            }
        }

        private void HandlePaste(ToolActionKey key, Player player, Point cursorTile)
        {
            if (key != ToolActionKey.LeftClick)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(player);

            if (player.whoAmI != Main.myPlayer)
            {
                return;
            }

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                throw new InvalidOperationException("Pasting is not implemented for multiplayer clients yet (server-side paste required).");
            }

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
            if (!projectionSystem.TryGetProjectionAt(player, cursorTile, out var projection) || projection is null || !projection.IsDefined)
            {
                return;
            }

            var pastingSystem = ModContent.GetInstance<PastingSystem>();
            bool hadOperation = pastingSystem.HasActiveOperationFor(projection);
            pastingSystem.StartPaste(projection);

            if (DebugStepper.IsHoldingDebugStick(player) && !hadOperation)
            {
                IEnumerator PasteStepEnumerator()
                {
                    while (pastingSystem.TryStepOperation(projection))
                        yield return null;
                }
                void OnCancel() => pastingSystem.StopPaste(projection);
                DebugStepper.StartSession(player, PasteStepEnumerator(), onComplete: null, onCancel: OnCancel, log: msg => { });
            }
        }

        private static void HandleSelectionEdit(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
        {
            var selectionSystem = ModContent.GetInstance<SelectionSystem>();

            switch (key)
            {
                case ToolActionKey.LeftClick:
                    // Start a rectangle gesture; the selection isn't applied until LeftRelease.
                    selectionSystem.Begin(player, action, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    selectionSystem.Update(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    selectionSystem.Apply(player, cursorTile);
                    return;

                case ToolActionKey.RightClick:
                    // Cancellation is explicitly separate from "clear selection" (selector tool right click).
                    selectionSystem.Cancel(player);
                    return;

                default:
                    return;
            }
        }

        private static void HandleProjectionEdit(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
        {
            ProjectionKind kind = action == ToolAction.COPY ? ProjectionKind.Copy : ProjectionKind.Cut;
            ModContent.GetInstance<ProjectionControl>().Handle(key, player, cursorTile, kind);
        }
    }

}