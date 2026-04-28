using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Config;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Core.Server;
using ArchitectsStencilMod.Debug;
using ArchitectsStencilMod.UIKit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace ArchitectsStencilMod.UI;

/// <summary>
/// Game-scaled world/tool interface: layered <see cref="UIView"/> roots, drawing, mouse sync, and tool gesture dispatch.
/// </summary>
public sealed class InterfaceSystem : ModSystem
{
    /// <summary>Computed at draw/update time (skip when map or main menu).</summary>
    public readonly struct WorldInterfaceContext
    {
        public bool IsGameMenu { get; init; }
        public bool IsMapFullscreen { get; init; }
        public bool ShouldDrawWorldInterface => !IsGameMenu && !IsMapFullscreen;
    }

    private sealed class InterfaceLayerSlot : UIView
    {
        public InterfaceLayerSlot()
        {
            OverridesMouse = false;
        }

        protected override float GetWidth() => Main.screenWidth;

        protected override float GetHeight() => Main.screenHeight;
    }

    private sealed class WorldInterfaceRoot : UIView
    {
        public WorldInterfaceRoot()
        {
            OverridesMouse = false;
        }

        protected override float GetWidth() => Main.screenWidth;

        protected override float GetHeight() => Main.screenHeight;
    }

    /// <summary>
    /// <see cref="ModContent.GetInstance{T}"/> is not reliable during this type’s own <see cref="ModSystem.Load"/>; use this for static entry points.
    /// </summary>
    private static InterfaceSystem RequireInstance()
    {
        InterfaceSystem? sys = ModContent.GetInstance<InterfaceSystem>();
        if (sys is null)
            throw new InvalidOperationException("ArchitectsStencilMod.InterfaceSystem is not registered yet.");
        return sys;
    }

    private readonly WorldInterfaceRoot _root = new();
    private readonly InterfaceLayerSlot[] _slots = new InterfaceLayerSlot[Enum.GetValues<InterfaceLayer>().Length];
    private readonly UIView?[][] _perPlayerHosts;
    private UIProjectionPayloadBar _projectionPayloadBar = new();
    private MouseState _previousMouseState;

    public InterfaceSystem()
    {
        _perPlayerHosts = new UIView[_slots.Length][];
        for (int i = 0; i < _perPlayerHosts.Length; i++)
            _perPlayerHosts[i] = new UIView[Main.maxPlayers];

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new InterfaceLayerSlot();
            _root.AddChild(_slots[i]);
        }

        _slots[(int)InterfaceLayer.ProjectionHud].AddChild(_projectionPayloadBar);
    }

    public static WorldInterfaceContext CurrentContext => new()
    {
        IsGameMenu = Main.gameMenu,
        IsMapFullscreen = Main.mapFullscreen
    };

    public override void OnWorldUnload()
    {
        for (int i = 0; i < _slots.Length; i++)
            _slots[i].RemoveAllChildren();

        for (int li = 0; li < _perPlayerHosts.Length; li++)
        {
            for (int p = 0; p < _perPlayerHosts[li].Length; p++)
                _perPlayerHosts[li][p] = null;
        }

        _projectionPayloadBar = new UIProjectionPayloadBar();
        _slots[(int)InterfaceLayer.ProjectionHud].AddChild(_projectionPayloadBar);
    }

    /// <summary>Layer container for adding world-overlay <see cref="UIView"/> children.</summary>
    internal static UIView GetLayer(InterfaceLayer layer) => RequireInstance()._slots[(int)layer];

    /// <summary>Removes all views from a layer (caller re-adds content).</summary>
    public static void ClearLayer(InterfaceLayer layer)
    {
        InterfaceSystem sys = RequireInstance();
        int li = (int)layer;
        UIView slot = sys._slots[li];
        slot.RemoveAllChildren();
        for (int p = 0; p < sys._perPlayerHosts[li].Length; p++)
            sys._perPlayerHosts[li][p] = null;

        if (layer == InterfaceLayer.ProjectionHud)
            slot.AddChild(sys._projectionPayloadBar);
    }

    /// <summary>Fullscreen pass-through host for one player’s content in a layer (z-order: lower index draws under higher).</summary>
    internal static UIView GetPerPlayerHost(InterfaceLayer layer, int whoAmI)
    {
        InterfaceSystem sys = RequireInstance();
        int li = (int)layer;
        UIView?[] row = sys._perPlayerHosts[li];
        if (row[whoAmI] == null)
        {
            var host = new InterfaceLayerSlot();
            row[whoAmI] = host;
            sys._slots[li].AddChild(host);
        }

        return row[whoAmI]!;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (Main.dedServ)
            return;

        var ctx = CurrentContext;
        if (!ctx.ShouldDrawWorldInterface)
            return;

        SyncMouseToUiKit();
        _root.Update();
    }

    private void SyncMouseToUiKit()
    {
        MouseState mouseState = Mouse.GetState();
        UIView.MouseLeftButton = mouseState.LeftButton == ButtonState.Pressed;
        UIView.MousePrevLeftButton = _previousMouseState.LeftButton == ButtonState.Pressed;
        UIView.MouseRightButton = mouseState.RightButton == ButtonState.Pressed;
        UIView.MousePrevRightButton = _previousMouseState.RightButton == ButtonState.Pressed;
        UIView.ScrollAmount = PlayerInput.ScrollWheelDeltaForUI;
        _previousMouseState = mouseState;
        UIView.HoverText = "";
        UIView.GameMouseOverwritten = false;
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int insertIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Interface Logic 1"));
        if (insertIndex == -1)
            insertIndex = 0;

        layers.Insert(insertIndex, new LegacyGameInterfaceLayer(
            "ArchitectsStencil: Interface",
            () =>
            {
                DrawWorldInterface();
                return true;
            },
            InterfaceScaleType.Game)
        );
    }

    private static void DrawWorldInterface()
    {
        var ctx = CurrentContext;
        if (!ctx.ShouldDrawWorldInterface)
            return;

        RequireInstance()._root.Draw(Main.spriteBatch);
    }

    /// <summary>Routes tool gestures from items; replaces the former <c>ToolActionManager.Handle</c> pipeline.</summary>
    public static void DispatchToolInput(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
    {
        try
        {
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
                    if (key is ToolActionKey.LeftClick or ToolActionKey.RightClick)
                        ApplyContextResetHitTest(player, action, cursorTile);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
            }
        }
        catch (Exception ex)
        {
            ASMLog.Error($"Tool action failed: action={action}, key={key}, cursor=({cursorTile.X},{cursorTile.Y}), player={player?.name}", ex, "ToolAction");
        }
    }

    private static bool TopmostBlueprintIsProjection(Player player, Point cursorTile)
    {
        return ModContent.GetInstance<BlueprintSystem>().TryGetTopmostBlueprintAt(player, cursorTile, out Blueprint? top)
            && top is Projection;
    }

    private static void HandlePin(ToolActionKey key, Player player, Point cursorTile)
    {
        var selectionControl = ModContent.GetInstance<SelectionControl>();
        var selectionSystem = ModContent.GetInstance<SelectionSystem>();

        switch (key)
        {
            case ToolActionKey.LeftClick:
                if (TopmostBlueprintIsProjection(player, cursorTile))
                    return;
                selectionControl.BeginPin(player, cursorTile);
                return;

            case ToolActionKey.LeftHold:
                selectionControl.UpdateGesture(player, cursorTile);
                return;

            case ToolActionKey.LeftRelease:
                selectionControl.EndGesture(player);
                return;

            case ToolActionKey.RightClick:
                selectionControl.CancelGesture(player);
                selectionSystem.ClearSelection(player);
                return;

            default:
                return;
        }
    }

    private static void HandleSet(ToolActionKey key, Player player, Point cursorTile)
    {
        var selectionControl = ModContent.GetInstance<SelectionControl>();
        var selectionSystem = ModContent.GetInstance<SelectionSystem>();

        switch (key)
        {
            case ToolActionKey.LeftClick:
                if (TopmostBlueprintIsProjection(player, cursorTile))
                    return;

                Selection selection = selectionSystem.GetSelection(player);
                if (!selection.IsDefined)
                {
                    selectionControl.BeginPin(player, cursorTile);
                }
                else
                {
                    selectionControl.BeginSet(player, cursorTile);
                }
                return;

            case ToolActionKey.LeftHold:
                selectionControl.UpdateGesture(player, cursorTile);
                return;

            case ToolActionKey.LeftRelease:
                selectionControl.EndGesture(player);
                return;

            case ToolActionKey.RightClick:
                selectionControl.CancelGesture(player);
                return;

            default:
                return;
        }
    }

    private static void HandleEdit(ToolAction action, ToolActionKey key, Player player, Point cursorTile)
    {
        var inclusionControl = ModContent.GetInstance<InclusionControl>();

        switch (key)
        {
            case ToolActionKey.LeftClick:
                if (action == ToolAction.INCLUDE)
                    inclusionControl.BeginInc(player, cursorTile);
                else
                    inclusionControl.BeginExc(player, cursorTile);
                return;

            case ToolActionKey.LeftHold:
                inclusionControl.UpdateGesture(player, cursorTile);
                return;

            case ToolActionKey.LeftRelease:
                inclusionControl.EndGesture(player, cursorTile);
                return;

            case ToolActionKey.RightClick:
                inclusionControl.CancelGesture(player);
                return;

            default:
                return;
        }
    }

    private static void HandleCopy(ToolActionKey key, Player player, Point cursorTile)
    {
        var projectionControl = ModContent.GetInstance<ProjectionControl>();

        switch (key)
        {
            case ToolActionKey.LeftClick:
                projectionControl.BeginCopy(player, cursorTile);
                return;

            case ToolActionKey.LeftHold:
                projectionControl.UpdateGesture(player, cursorTile);
                return;

            case ToolActionKey.LeftRelease:
                projectionControl.EndGesture(player);
                return;

            case ToolActionKey.RightClick:
                projectionControl.CancelGesture(player);
                return;

            default:
                return;
        }
    }

    private static void HandlePaste(ToolActionKey key, Player player, Point cursorTile)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.whoAmI != Main.myPlayer)
            return;

        var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
        if (!projectionSystem.TryGetProjectionAt(player, cursorTile, out var projection) || projection is null || !projection.IsDefined)
            return;

        switch (key)
        {
            case ToolActionKey.LeftClick:
                var config = ModContent.GetInstance<PastingConfig>();

                if (projection.Area.Width > config.MaxPasteWidth || projection.Area.Height > config.MaxPasteHeight)
                {
                    if (Main.netMode != NetmodeID.Server)
                        Main.NewText("Paste blocked: projection exceeds area limits.", Color.OrangeRed);
                    return;
                }

                if (projection.SourceTiles.Length > config.VolumeLimit)
                {
                    if (Main.netMode != NetmodeID.Server)
                        Main.NewText("Paste blocked: projection exceeds tile volume limit.", Color.OrangeRed);
                    return;
                }

                if (!ModContent.GetInstance<PastingSystem>().StartPaste(projection, ClientPreferences.FromLocalConfigs(), out var startReject))
                {
                    Main.NewText($"Paste rejected: {startReject}", Color.OrangeRed);
                }
                break;

            case ToolActionKey.RightClick:
                ModContent.GetInstance<PastingSystem>().StopPaste(projection);
                break;

            default:
                return;
        }
    }

    internal static void ApplyContextResetHitTest(Player player, ToolAction action, Point cursorTile)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.whoAmI != Main.myPlayer)
            return;

        var projectionSystem = ModContent.GetInstance<ProjectionSystem>();
        projectionSystem.TryGetProjectionAt(player, cursorTile, out var projection);

        switch (action)
        {
            case ToolAction.RESET:
                if (projection != null && projection.IsDefined)
                {
                    if (projection.IsLocked)
                    {
                        ModContent.GetInstance<PastingSystem>().StopPaste(projection);
                    }
                    else
                    {
                        projectionSystem.RemoveProjectionAt(player, cursorTile);
                    }
                }
                else
                {
                    var selection = ModContent.GetInstance<SelectionSystem>().GetSelection(player);
                    if (selection.IsDefined && selection.Area.Contains(cursorTile))
                    {
                        ModContent.GetInstance<SelectionSystem>().ClearSelection(player);
                    }
                }
                break;

            case ToolAction.PASTE:
                if (projection != null && projection.IsDefined)
                    ModContent.GetInstance<PastingSystem>().StopPaste(projection);
                break;

            case ToolAction.COPY:
                projectionSystem.RemoveProjectionAt(player, cursorTile);
                break;

            case ToolAction.PIN:
            case ToolAction.SET:
                var sel = ModContent.GetInstance<SelectionSystem>().GetSelection(player);
                if (sel.IsDefined && sel.Area.Contains(cursorTile))
                    ModContent.GetInstance<SelectionSystem>().ClearSelection(player);
                break;

            default:
                break;
        }
    }
}
