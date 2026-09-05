using System;
using System.Linq;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.Server;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.UI;
using TheArchitectsBlueprintMod.Core.ArchitectMode;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Core routing point for "tool action" input.
    /// Tools supply <see cref="ToolAction"/> and <see cref="ToolActionKey"/>; this system decides PIN vs SET,
    /// what <see cref="ToolActionKey.RightClick"/> means per action, and shared RESET hit-testing.
    /// </summary>
    public sealed class ToolActionManager : ModSystem
    {
        private static BlueprintSystem BlueprintSystem => ModContent.GetInstance<BlueprintSystem>();
        /// <summary>
        /// High-level actions that tools can request.
        /// </summary>
        public enum ToolAction
        {
            /// <summary>Starts a fresh selection bounds (pin gesture).</summary>
            PIN = 0,
            /// <summary>Edits an existing selection, or moves a projection when the selection is not under the cursor.</summary>
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
                // Ignore world clicks that hit the on-blueprint toolbox so PIN cannot wipe the selection.
                if (key is ToolActionKey.LeftClick
                    && ModContent.GetInstance<ProjectionToolboxControl>().IsHovering)
                    return;

                var architectMode = ModContent.GetInstance<ArchitectModeSystem>();
                if (architectMode.Active && architectMode.IsToolbarHovered)
                    return;

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
                TABLog.Error($"Tool action failed: action={action}, key={key}, cursor=({cursorTile.X},{cursorTile.Y}), player={player?.name}", ex, "ToolAction");
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

        /// <summary>Runs SET on a hit selection, else moves a projection, else falls back to PIN when nothing is defined.</summary>
        private static void HandleSet(ToolActionKey key, Player player, Point cursorTile)
        {
            switch (key)
            {
                case ToolActionKey.LeftClick:
                    // Prefer the selection whenever the cursor hits it, even if a projection overlaps the same tile.
                    if (SelectionControl.BeginSet(player, cursorTile))
                        return;

                    // Selection was not hit; move an existing projection under the cursor.
                    if (ProjectionControl.BeginMove(player, cursorTile))
                        return;

                    // No selection or projection target; start a new PIN when nothing is defined to edit.
                    if (BlueprintSystem.GetSelection() is not { IsDefined: true })
                        SelectionControl.BeginPin(player, cursorTile);
                    return;

                case ToolActionKey.LeftHold:
                    if (ProjectionControl.IsActivelyDragging(player))
                        ProjectionControl.UpdateGesture(player, cursorTile);
                    else
                        SelectionControl.UpdateGesture(player, cursorTile);
                    return;

                case ToolActionKey.LeftRelease:
                    if (ProjectionControl.IsActivelyDragging(player))
                        ProjectionControl.EndGesture(player);
                    else
                        SelectionControl.EndGesture(player);
                    return;

                case ToolActionKey.RightClick:
                    if (ProjectionControl.IsActivelyDragging(player))
                        ProjectionControl.CancelGesture(player);
                    else
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
        private static void HandlePaste(ToolActionKey key, Player player, Point cursorTile)
        {
            // --- Right-click stops an in-flight paste on the hit projection; left-click validates and starts or requests paste.

            ArgumentNullException.ThrowIfNull(player);
            if (player.whoAmI != Main.myPlayer)
                return;
            if (!BlueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection projection))
                return;

            // check ToolActionKey first, then handle per Netmode
            switch (key)
            {
                // Left-click: validate and start paste locally or request from server.
                case ToolActionKey.LeftClick:
                    // Validate the paste.
                    var config = ModContent.GetInstance<PastingConfig>();

                    if (PasteLimits.ExceedsArea(projection, config))
                    {
                        if (Main.netMode != NetmodeID.Server)
                        {
                            Main.NewText(
                                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Paste.BlockedArea"),
                                Color.OrangeRed);
                        }
                        return;
                    }

                    if (PasteLimits.ExceedsVolume(projection, config))
                    {
                        if (Main.netMode != NetmodeID.Server)
                        {
                            Main.NewText(
                                Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.Paste.BlockedVolume"),
                                Color.OrangeRed);
                        }
                        return;
                    }

                    BlueprintGuard.RefreshClientGuardMasks(player, skipWhenPasteActive: false);

                    if (!BlueprintGuard.TryValidateIncludedTiles(player, projection, out BlueprintGuardFailure guardFailure))
                    {
                        if (Main.netMode != NetmodeID.Server)
                        {
                            Main.NewText(
                                Language.GetTextValue(
                                    "Mods.TheArchitectsBlueprintMod.UI.Paste.RejectedPrefix",
                                    PasteNetworking.FormatPasteRejectReason(
                                        BlueprintGuard.ToPasteRejectReason(guardFailure))),
                                Color.OrangeRed);
                        }
                        return;
                    }

                    if (!PastingSystem.StartPaste(projection, out var startReject))
                    {
                        Main.NewText(
                            Language.GetTextValue(
                                "Mods.TheArchitectsBlueprintMod.UI.Paste.RejectedPrefix",
                                PasteNetworking.FormatPasteRejectReason(startReject)),
                            Color.OrangeRed);
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

            _ = BlueprintSystem.GetTopmostProjectionAt(cursorTile, out Projection projection);

            switch (action)
            {
                // RESET: Reset based on priority (paste > projection > selection)
                case ToolAction.RESET:

                    if (projection is { IsDefined: true })
                    {
                        // If the projection is locked (Paste is in progress), Stop the paste.
                        if (projection.IsLocked)
                        {
                            PastingSystem.StopPaste(projection);
                        }
                        // Otherwise, remove the projection.
                        else
                        {
                            ProjectionSystem.RemoveProjection(projection);
                        }
                    }
                    else if (BlueprintSystem.GetTopmostSelectionAt(cursorTile, out Selection selection) && selection.IsDefined)
                    {
                        SelectionSystem.ClearSelection(player);
                    }
                    break;
                // PASTE: Stop the paste.
                case ToolAction.PASTE:
                    if (projection is { IsDefined: true })
                    {
                        PastingSystem.StopPaste(projection);
                    }
                    break;
                // COPY: Remove the projection at the cursor.
                case ToolAction.COPY:
                    if (projection is { IsDefined: true })
                    {
                        ProjectionSystem.RemoveProjection(projection);
                    }
                    break;
                // PIN/SET: Clear the selection.
                case ToolAction.PIN:
                case ToolAction.SET:
                    if (BlueprintSystem.GetTopmostSelectionAt(cursorTile, out Selection sel) && sel.IsDefined)
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
