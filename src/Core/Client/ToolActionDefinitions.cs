using System;

namespace ArchitectsStencilMod.Core;

/// <summary>
/// High-level actions that tools can request. Used as a key for routing input in <see cref="ArchitectsStencilMod.UI.InterfaceSystem"/>.
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
/// Input gestures tools report to <see cref="UI.InterfaceSystem.DispatchToolInput"/>.
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

public static class ToolActionDefinitions
{
    public static string GetActionId(ToolAction action) => action.ToString().ToLowerInvariant();

    public static string GetActionLabel(ToolAction action)
    {
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
}
