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
            /// <summary>Starts a fresh selection bounds (pin gesture).</summary>
            PIN = 0,
            /// <summary>Edits an existing selection's bounds, or falls back to PIN if undefined.</summary>
            SET = 1,
            /// <summary>Includes tiles in the current selection or projection via rectangle edit.</summary>
            INCLUDE = 2,
            /// <summary>Excludes tiles from the current selection or projection via rectangle edit.</summary>
            EXCLUDE = 3,
            /// <summary>Creates or moves a copy projection linked to the active selection.</summary>
            COPY = 4,
            /// <summary>Starts or cancels paste against the projection under the cursor.</summary>
            PASTE = 5,
            /// <summary>Removes either the selection or the projection under the cursor.</summary>
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

        private static SelectionControl SelectionControl => ModContent.GetInstance<SelectionControl>();
        private static SelectionSystem SelectionSystem => ModContent.GetInstance<SelectionSystem>();
        private static InclusionControl InclusionControl => ModContent.GetInstance<InclusionControl>();
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
                ToolAction.INCLUDE => "INC",
                ToolAction.EXCLUDE => "EXC",
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

                    case ToolAction.INCLUDE:
                    case ToolAction.EXCLUDE:
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
                            ApplyContextResetHitTest(player, action, cursorTile);
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

        /// <summary>Runs PIN selection gestures, treating right-click as cancel-only.</summary>
        private static void HandlePin(ToolActionKey key, Player player, Point cursorTile)
        {
            switch (key)
            {
                case ToolActionKey.LeftClick:
                    SelectionControl.BeginPin(player, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    SelectionControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    SelectionControl.EndGesture(player);
                    return;

                case ToolActionKey.RightClick:
                    SelectionControl.CancelGesture(player);
                    SelectionSystem.ClearSelection(player);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Runs SET when a selection exists, otherwise falls back to PIN behavior.</summary>
        private static void HandleSet(ToolActionKey key, Player player, Point cursorTile)
        {
            switch (key)
            {
                case ToolActionKey.LeftClick:
                    var selection = SelectionSystem.GetSelection(player);
                    if (!selection.IsDefined)
                    {
                        SelectionControl.BeginPin(player, cursorTile);
                    }
                    else
                    {
                        SelectionControl.BeginSet(player, cursorTile);
                    }
                    return;

                case ToolActionKey.LeftHold:
                    SelectionControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    SelectionControl.EndGesture(player);
                    return;

                case ToolActionKey.RightClick:
                    SelectionControl.CancelGesture(player);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Routes INCLUDE/EXCLUDE gestures to <see cref="InclusionControl"/>; the action determines which begin variant is called.</summary>
        private static void HandleEdit(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
        {
            switch (key)
            {
                case ToolActionKey.LeftClick:
                    if (action == ToolAction.INCLUDE)
                        InclusionControl.BeginInc(player, cursorTile);
                    else
                        InclusionControl.BeginExc(player, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    InclusionControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    InclusionControl.EndGesture(player, cursorTile);
                    return;

                case ToolActionKey.RightClick:
                    InclusionControl.CancelGesture(player);
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

        /// <summary>Starts or stops/cancels paste for the projection under the cursor (local player only). Network is handled in <see cref="PastingSystem"/>.</summary>
        private void HandlePaste(ToolActionKey key, Player player, Point cursorTile)
        {
            // --- Right-click stops an in-flight paste on the hit projection; left-click validates and starts or requests paste.

            ArgumentNullException.ThrowIfNull(player);
            if (player.whoAmI != Main.myPlayer)
                return;
            if (!ProjectionSystem.TryGetProjectionAt(player, cursorTile, out var projection) || projection is null || !projection.IsDefined)
                return;

            // check ToolActionKey first, then handle per Netmode
            switch (key)
            {
                // Left-click: validate and start paste locally or request from server.
                case ToolActionKey.LeftClick:
                    // Validate the paste.
                    var config = ModContent.GetInstance<PastingConfig>();

                    // Enforce configured maximum paste dimensions.
                    if (projection.Area.Width > config.MaxPasteWidth || projection.Area.Height > config.MaxPasteHeight)
                    {
                        if (Main.netMode != NetmodeID.Server)
                            Main.NewText("Paste blocked: projection exceeds area limits.", Color.OrangeRed);
                        return;
                    }

                    // Enforce configured tile volume cap.
                    if (projection.SourceTiles.Length > config.VolumeLimit)
                    {
                        if (Main.netMode != NetmodeID.Server)
                            Main.NewText("Paste blocked: projection exceeds tile volume limit.", Color.OrangeRed);
                        return;
                    }

                    if (!PastingSystem.StartPaste(projection, ClientPreferences.FromLocalConfigs(), out var startReject))
                    {
                        Main.NewText($"Paste rejected: {startReject}", Color.OrangeRed);
                    }
                    break;
                // Right-click: Run the RESET action.
                case ToolActionKey.RightClick:
                    PastingSystem.StopPaste(projection);
                    break;
                default:
                    return;
            }
        }

        /// <summary>Removes a projection at the cursor, or clears the selection when the cursor lies inside defined bounds.</summary>
        public static void ApplyContextResetHitTest(Player player, ToolAction action, Point cursorTile)
        {
            // --- For local player only: remove projection under cursor, else clear selection if cursor is inside bounds.

            ArgumentNullException.ThrowIfNull(player);
            if (player.whoAmI != Main.myPlayer)
            {
                return;
            }

            ProjectionSystem.TryGetProjectionAt(player, cursorTile, out var projection); // returns false if no projection, but we still want to check selection in that case, so ignore the bool result.

            switch (action)
            {
                // RESET: Reset based on priority (paste > projection > selection)
                case ToolAction.RESET:

                    if (projection != null && projection.IsDefined)
                    {
                        // If the projection is locked (Paste is in progress), Stop the paste.
                        if (projection.IsLocked)
                        {
                            PastingSystem.StopPaste(projection);
                        }
                        // Otherwise, remove the projection.
                        else
                        {
                            ProjectionSystem.RemoveProjectionAt(player, cursorTile);
                        }
                    }
                    else
                    {
                        // If the projection is not defined, clear the selection under the cursor.
                        if (SelectionSystem.GetSelection(player).IsDefined && SelectionSystem.GetSelection(player).Area.Contains(cursorTile))
                        {
                            SelectionSystem.ClearSelection(player);
                        }
                    }
                    break;
                // PASTE: Stop the paste.
                case ToolAction.PASTE:
                    if (projection != null && projection.IsDefined)
                    {
                        PastingSystem.StopPaste(projection);
                    }
                    break;
                // COPY: Remove the projection at the cursor.
                case ToolAction.COPY:
                    ProjectionSystem.RemoveProjectionAt(player, cursorTile);
                    break;
                // PIN/SET: Clear the selection.
                case ToolAction.PIN|ToolAction.SET:
                    if (SelectionSystem.GetSelection(player).IsDefined && SelectionSystem.GetSelection(player).Area.Contains(cursorTile))
                    {
                        SelectionSystem.ClearSelection(player);
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
