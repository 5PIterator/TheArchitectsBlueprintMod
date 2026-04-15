using System;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core.Server;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Core routing point for "tool action" input.
    /// Tools supply <see cref="ToolAction"/> and <see cref="ToolActionKey"/>; this system decides PIN vs SET,
    /// what <see cref="ToolActionKey.RightClick"/> means per action, and shared RESET hit-testing.
    /// </summary>
    public sealed class ToolActionManager : ModSystem
    {
        /// <summary>
        /// High-level actions that tools can request.
        /// </summary>
        public enum ToolAction
        {
            /// <summary>Starts a fresh blueprint bounds selection (pin gesture).</summary>
            PIN = 0,
            /// <summary>Edits an existing blueprint's bounds, or falls back to PIN if undefined.</summary>
            SET = 1,
            /// <summary>Adds tiles to the current blueprint or projection selection via rectangle edit.</summary>
            ADD = 2,
            /// <summary>Removes tiles from the current blueprint or projection selection via rectangle edit.</summary>
            REMOVE = 3,
            /// <summary>Creates or moves a copy projection linked to the active blueprint.</summary>
            COPY = 4,
            /// <summary>Starts or cancels paste against the projection under the cursor.</summary>
            PASTE = 5,
            /// <summary>Removes either the blueprint or the projection under the cursor.</summary>
            RESET = 6,
        }

        /// <summary>
        /// Input gestures/tools can report to the manager.
        /// A tool may map the same physical button to different keys depending on its design.
        /// </summary>
        public enum ToolActionKey
        {
            /// <summary>First frame of a left press.</summary>
            LeftClick,
            /// <summary>Left button held after the initial click.</summary>
            LeftHold,
            /// <summary>Left button released after being down.</summary>
            LeftRelease,
            /// <summary>First frame of a right press (or alt-use click).</summary>
            RightClick,
            /// <summary>Right button held after the initial click.</summary>
            RightHold,
            /// <summary>Right button released after being down.</summary>
            RightRelease
        }

        private static BlueprintControl BlueprintControl => ModContent.GetInstance<BlueprintControl>();
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        private static SelectionSystem SelectionSystem => ModContent.GetInstance<SelectionSystem>();
        private static ProjectionSystem ProjectionSystem => ModContent.GetInstance<ProjectionSystem>();

        private static ProjectionControl ProjectionControl => ModContent.GetInstance<ProjectionControl>();
        private static PastingSystem PastingSystem => ModContent.GetInstance<PastingSystem>();

        /// <summary>Returns the lowercase identifier string for a tool action.</summary>
        public static string GetActionId(ToolAction action) => action.ToString().ToLowerInvariant();

        /// <summary>Returns a short display label for the action (UI / wheel).</summary>
        public static string GetActionLabel(ToolAction action)
        {
            // --- Map each known action to a fixed label; unknown values throw.

            // Switch on action and return the uppercase label string used in the wheel.
            return action switch
            {
                ToolAction.PIN => "PIN",
                ToolAction.SET => "SET",
                ToolAction.ADD => "ADD",
                ToolAction.REMOVE => "RMV",
                ToolAction.COPY => "CPY",
                ToolAction.PASTE => "PST",
                ToolAction.RESET => "RST",
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
            };
        }

        /// <summary>Dispatches a tool gesture to the appropriate blueprint, projection, or paste handler.</summary>
        public void Handle(
            ToolAction action,
            ToolActionKey key,
            Player player,
            Point cursorTile)
        {
            // --- Route by action type; log and swallow unexpected failures so tools do not hard-crash the client.

            try
            {
                // Switch on high-level action and delegate to focused handlers or subsystems.
                switch (action)
                {
                    case ToolAction.PIN:
                        HandlePin(key, player, cursorTile);
                        return;

                    case ToolAction.SET:
                        HandleSet(key, player, cursorTile);
                        return;

                    case ToolAction.ADD:
                    case ToolAction.REMOVE:
                        HandleEdit(action, key, player, cursorTile);
                        return;

                    case ToolAction.COPY:
                        HandleCopy(key, player, cursorTile);
                        return;

                    case ToolAction.PASTE:
                        HandlePaste(key, player, cursorTile);
                        return;

                    case ToolAction.RESET:
                        // Apply hit-test reset only on discrete click keys (not hold/release noise).
                        if (key is ToolActionKey.LeftClick or ToolActionKey.RightClick)
                            ApplyContextResetHitTest(player, cursorTile);
                        return;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
                }
            }
            catch (Exception ex)
            {
                // Record context-rich error for diagnostics without propagating.
                ASMLog.Error($"Tool action failed: action={action}, key={key}, cursor=({cursorTile.X},{cursorTile.Y}), player={player?.name}", ex, "ToolAction");
            }
        }

        /// <summary>Runs PIN blueprint gestures, treating right-click as cancel-only.</summary>
        private static void HandlePin(ToolActionKey key, Player player, Point cursorTile)
        {
            switch (key)
            {
                case ToolActionKey.LeftClick:
                    BlueprintControl.BeginPin(player, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    BlueprintControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    BlueprintControl.EndGesture(player);
                    return;

                case ToolActionKey.RightClick:
                    BlueprintControl.CancelGesture(player);
                    BlueprintSystem.ClearBlueprint(player);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Runs SET when a blueprint exists, otherwise falls back to PIN behavior.</summary>
        private static void HandleSet(ToolActionKey key, Player player, Point cursorTile)
        {
            switch (key)
            {
                case ToolActionKey.LeftClick:
                    var blueprint = BlueprintSystem.GetBlueprint(player);
                    if (!blueprint.IsDefined)
                    {
                        BlueprintControl.BeginPin(player, cursorTile);
                    }
                    else
                    {
                        BlueprintControl.BeginSet(player, cursorTile);
                    }
                    return;

                case ToolActionKey.LeftHold:
                    BlueprintControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    BlueprintControl.EndGesture(player);
                    return;

                case ToolActionKey.RightClick:
                    BlueprintControl.CancelGesture(player);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Maps editable ADD/REMOVE rectangle gestures to the selection system.</summary>
        private static void HandleEdit(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
        {
            // --- Translate discrete click/hold/release into selection begin/update/apply; right-click cancels preview.

            switch (key)
            {
                case ToolActionKey.LeftClick:
                    SelectionSystem.Begin(player, action, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    SelectionSystem.Update(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    SelectionSystem.Apply(player, cursorTile);
                    return;

                case ToolActionKey.RightClick:
                    if (SelectionSystem.TryGetPreview(player, out _, out _, out _))
                    {
                        SelectionSystem.Cancel(player);
                    }
                    return;

                default:
                    return;
            }
        }

        /// <summary>Handles copy-projection drags and maps right-click to cancel or context reset.</summary>
        private static void HandleCopy(ToolActionKey key, Player player, Point cursorTile)
        {
            // --- Right-click stops an active drag when possible; else run the shared reset hit-test.

            switch (key)
            {
                case ToolActionKey.LeftClick:
                    ProjectionControl.BeginCopy(player, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    ProjectionControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    ProjectionControl.EndGesture(player);
                    return;

                case ToolActionKey.RightClick:
                    ProjectionControl.CancelGesture(player);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Starts or cancels paste for the projection under the cursor (local player only).</summary>
        private void HandlePaste(ToolActionKey key, Player player, Point cursorTile)
        {
            // --- Right-click stops an in-flight paste on the hit projection; left-click validates and starts or requests paste.

            ArgumentNullException.ThrowIfNull(player);

            if (player.whoAmI != Main.myPlayer)
            {
                return;
            }

            // Get the pasting system.
            if (
                !ProjectionSystem.TryGetProjectionAt(player, cursorTile, out var projection) ||
                projection is null ||
                !projection.IsDefined
                )
            {
                return;
            }

            // check ToolActionKey first, then handle per Netmode
            switch (key)
            {
                // Left-click: validate and start paste locally or request from server.
                case ToolActionKey.LeftClick:
                    // Validate the paste.
                    var config = ModContent.GetInstance<PastingConfig>();

                    // Enforce configured maximum paste dimensions.
                    if (projection.Bounds.Width > config.MaxPasteWidth || projection.Bounds.Height > config.MaxPasteHeight)
                    {
                        if (Main.netMode != NetmodeID.Server)
                            Main.NewText("Paste blocked: projection exceeds area limits.", Color.OrangeRed);
                        return;
                    }

                    // Enforce configured tile volume cap.
                    if (projection.SourceTiles.Count > config.VolumeLimit)
                    {
                        if (Main.netMode != NetmodeID.Server)
                            Main.NewText("Paste blocked: projection exceeds tile volume limit.", Color.OrangeRed);
                        return;
                    }

                    // Handle per Netmode.
                    switch (Main.netMode)
                    {
                        // Left-click: Single-player: start paste locally.
                        case NetmodeID.SinglePlayer:
                            if (!PastingSystem.StartPaste(projection, ClientPreferences.FromLocalConfigs(), out var startReject))
                            {
                                Main.NewText($"Paste rejected: {startReject}", Color.OrangeRed);
                            }
                            break;

                        case NetmodeID.MultiplayerClient:
                            // For multiplayer, send a paste request to the server.
                            if (!PasteNetworking.SendPasteRequest(Mod, player, projection, out var rejectReason))
                            {
                                Main.NewText($"Paste request failed: {rejectReason}", Color.OrangeRed);
                            }
                            break;
                    }
                    break;
                case ToolActionKey.RightClick:
                    switch (Main.netMode)
                    {
                        case NetmodeID.SinglePlayer:
                            projection.Id = 0;
                            projection.Unlock();
                            PastingSystem.StopPaste(projection);
                            break;

                        case NetmodeID.MultiplayerClient:
                            PasteNetworking.SendCancelIfPending(Mod, projection);
                            break;
                    }
                    break;
                default:
                    return;
            }
        }

        /// <summary>Removes a projection at the cursor, or clears the blueprint when the cursor lies inside defined bounds.</summary>
        public static void ApplyContextResetHitTest(Player player, Point cursorTile)
        {
            // --- For local player only: remove projection under cursor, else clear blueprint if cursor is inside bounds.

            ArgumentNullException.ThrowIfNull(player);
            if (player.whoAmI != Main.myPlayer)
            {
                return;
            }

            // Prefer removing a projection at the cursor when one exists.
            if (ProjectionSystem.RemoveProjectionAt(player, cursorTile))
            {
                return;
            }

            // Otherwise clear the blueprint selection when the cursor lies inside defined bounds.
            var blueprint = BlueprintSystem.GetBlueprint(player);
            if (blueprint.IsDefined && blueprint.Bounds.Contains(cursorTile))
            {
                BlueprintSystem.ClearBlueprint(player);
            }
        }
    }
}
