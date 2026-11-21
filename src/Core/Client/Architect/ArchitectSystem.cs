using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics.Capture;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;
using TheArchitectsBlueprintMod.Content.Items;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.UI;
using TheArchitectsBlueprintMod.UI.TmodGUI;
using TheArchitectsBlueprintMod.UI.TmodGUI.Widgets;
using TheArchitectsBlueprintMod.Utils;
using static TheArchitectsBlueprintMod.Core.ToolActionManager;
using TheArchitectsBlueprintMod.UI.Style;

namespace TheArchitectsBlueprintMod.Core.Architect
{
    /// <summary>Client owner of Architect Mode, TmodGUI, overlays, toolbox, action wheel, and fullscreen-map interaction.</summary>
    [Autoload(Side = ModSide.Client)]
    public sealed class ArchitectSystem : ModSystem
    {
        private const int ToolbarButtonCount = 9;
        private const int ToolbarButtonSize = 42;
        private const int ToolbarButtonSpacing = 46;
        private const int ToolbarOriginX = 24;
        private const int ToolbarOriginY = 24;
        private const string IconsRoot = "Content/Icons";
        private const string EntryIconAssetPath = "Content/Icons/ArchitectMode";
        internal const string SearchIconPath = "Content/Icons/Button_Search";
        internal const string ManifestIconPath = "Content/Icons/Manifest";
        internal const string CutIconPath = "Content/Icons/Cut";

        private static readonly string[] ToolbarIconPaths =
        [
            SearchIconPath,
            ManifestIconPath,
            "Content/Icons/PIN",
            "Content/Icons/SET",
            "Content/Icons/Include",
            "Content/Icons/Copy",
            "Content/Icons/Paste",
            CutIconPath,
            "Content/Icons/EXIT",
        ];

        private static readonly string[] PreloadIconPaths =
        [
            EntryIconAssetPath,
            SearchIconPath,
            ManifestIconPath,
            "Content/Icons/PIN",
            "Content/Icons/SET",
            "Content/Icons/Include",
            "Content/Icons/Exclude",
            "Content/Icons/Copy",
            "Content/Icons/Paste",
            CutIconPath,
            "Content/Icons/EXIT",
        ];

        private Vector2 _mapPanLockPos;
        private bool _mapPanLockActive;

        private bool _entryHovered;
        private bool _uiPrevLeftDown;

        private int _toolbarHoveredIndex = -1;
        private bool _toolbarInclusionIsExclude;

        private readonly Dictionary<string, Asset<Texture2D>> _iconAssets = new(StringComparer.OrdinalIgnoreCase);

        private UserInterface? _ui;
        private GameTime? _lastUpdateUiGameTime;
        private bool _closeAllPending;

        /// <summary>World toolbox attached to the frontmost defined selection or projection.</summary>
        internal ProjectionToolboxControl Toolbox { get; } = new();

        internal bool PrevLeftDown;
        internal bool LeftDragSeenDown;
        internal bool PrevRightDown;
        internal bool RightDragSeenDown;

        /// <summary>True while the Architect Mode session is open.</summary>
        public bool Active { get; private set; }

        /// <summary>Toolbar-selected tool action for world/map gestures.</summary>
        public ToolAction SelectedAction { get; set; } = ToolAction.PIN;

        /// <summary>Suppresses the first click after opening, matching Camera Mode.</summary>
        public bool JustActivated { get; private set; }

        /// <summary>True when fullscreen map is open during an active Architect Mode session.</summary>
        public bool UsingMap => Active && Main.mapFullscreen;

        /// <summary>True when the pointer is over the top toolbar.</summary>
        public bool IsToolbarHovered => _toolbarHoveredIndex >= 0;

        /// <summary>True when the pointer is over the equip-page entry tab.</summary>
        public bool IsEntryHovered => _entryHovered;

        public override void Load()
        {
            // --- Preload toolbar icons, register chat tags, and create the TmodGUI UserInterface host.

            foreach (string path in PreloadIconPaths)
                TryLoadIconAsset(path);

            if (Main.dedServ)
                return;

            ChatManager.Register<ManifestPageTagHandler>(ManifestPrinter.ChatTagName);
            _ui = new UserInterface();
        }

        public override void Unload()
        {
            // --- Tear down widgets and release the UI host on mod unload.

            _closeAllPending = false;
            TmodGUI.CloseAll();
            _ui = null;
            _lastUpdateUiGameTime = null;
        }

        /// <summary>Sets the active TmodGUI widget state on the hosted UserInterface.</summary>
        internal void SetActiveWidget(TmodWidgetBase? widget)
        {
            // --- Forward widget state to the Terraria UserInterface.

            _ui?.SetState(widget);
        }

        /// <summary>Defer GUI teardown to <see cref="UpdateUI"/> so texture disposal stays on the main thread.</summary>
        private void RequestCloseAllOnMainThread() => _closeAllPending = true;

        /// <summary>
        /// Switches mouse zoom so <see cref="Main.mouseX"/> / <see cref="Main.MouseWorld"/> match
        /// screen hit-tests and tile picks for the current view (world vs map).
        /// </summary>
        public static void ApplyCursorZoom()
        {
            // --- Map overlays share UI mouse space; world tools need game zoom.

            if (ArchitectMapTransform.IsMapViewActive)
                PlayerInput.SetZoom_UI();
            else
                PlayerInput.SetZoom_World();
        }

        /// <summary>
        /// Resolves the tile under the cursor for tool routing.
        /// On the map, returns true only while Architect Mode is active.
        /// </summary>
        public bool TryGetCursorTile(Player player, out Point tile)
        {
            // --- Align mouse zoom, then map MouseWorld / map screen coords to a tile.

            ApplyCursorZoom();

            if (!ArchitectMapTransform.IsMapViewActive)
            {
                tile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
                return true;
            }

            // Map gestures are owned by Architect Mode; held items cannot interact on the map.
            if (!Active)
            {
                tile = default;
                return false;
            }

            return TryFullscreenMapMouseToTile(out tile);
        }

        /// <summary>
        /// Converts screen mouse position to a tile on the pannable fullscreen map.
        /// </summary>
        public static bool TryFullscreenMapMouseToTile(out Point tile)
        {
            // --- Reject when not in a map view, then convert mouse pixels to tile coords.

            if (!ArchitectMapTransform.IsMapViewActive)
            {
                tile = default;
                return false;
            }

            ApplyCursorZoom();
            return ArchitectMapTransform.TryScreenToTile(new Vector2(Main.mouseX, Main.mouseY), out tile);
        }

        /// <summary>Opens or closes Architect Mode when the player is allowed and Camera Mode is inactive.</summary>
        public void Toggle(bool on = true)
        {
            // --- Activate a Camera-like session, or tear it down and restore normal input.

            if (on)
            {
                if (Active || !CanOpen(Main.LocalPlayer))
                    return;

                Active = true;
                JustActivated = true;
                _uiPrevLeftDown = true;
                Main.playerInventory = false;
                SoundEngine.PlaySound(SoundID.MenuOpen);
                return;
            }

            if (!Active)
                return;

            Active = false;
            JustActivated = false;
            ResetSessionState();
            SoundEngine.PlaySound(SoundID.MenuClose);
        }

        /// <summary>Returns true when the local player may enter Architect Mode.</summary>
        public bool CanOpen(Player player)
        {
            // --- Require a living local player with GUI access and no Camera Mode conflict.

            if (player is not { active: true } || player.dead || Main.gameMenu)
                return false;

            if (CaptureManager.Instance.Active)
                return false;

            return player.GetModPlayer<ArchitectModePlayer>().CanAccessArchitectGui;
        }

        public override void UpdateUI(GameTime gameTime)
        {
            // --- Drive action wheel, Architect Mode, toolbox, then the active TmodGUI widget.

            _lastUpdateUiGameTime = gameTime;

            if (_closeAllPending)
            {
                _closeAllPending = false;
                TmodGUI.CloseAll();
            }

            ActionWheelRuntime.UpdateUI(gameTime);

            if (Main.dedServ || Main.gameMenu)
            {
                _uiPrevLeftDown = Main.mouseLeft;
                if (Main.gameMenu)
                    TmodGUI.CloseAll();
                return;
            }

            PlayerInput.SetZoom_UI();
            bool uiClicked = Main.mouseLeft && !_uiPrevLeftDown;

            UpdateEntryTab(uiClicked);

            if (Active)
            {
                Player player = Main.LocalPlayer;
                if (player is not { active: true } || player.dead)
                {
                    _uiPrevLeftDown = Main.mouseLeft;
                    Toggle(false);
                }
                else
                {
                    Main.blockMouse = true;

                    if (JustActivated && Main.mouseLeftRelease && !Main.mouseLeft)
                        JustActivated = false;

                    UpdateSession(uiClicked);

                    if (Main.mapFullscreen && IsMapGestureActive(player))
                        Main.mapFullscreenPos = _mapPanLockPos;
                }
            }

            Toolbox.UpdateUI(gameTime);

            if (_ui?.CurrentState != null)
                _ui.Update(gameTime);

            _uiPrevLeftDown = Main.mouseLeft;
        }

        public override void PostUpdateEverything()
        {
            // --- Snapshot map pan at gesture start and hold it while tools drag on the map.

            if (!Active || !Main.mapFullscreen)
            {
                _mapPanLockActive = false;
                return;
            }

            Player player = Main.LocalPlayer;
            if (IsMapGestureActive(player))
            {
                if (!_mapPanLockActive)
                {
                    _mapPanLockPos = Main.mapFullscreenPos;
                    _mapPanLockActive = true;
                }

                Main.mapFullscreenPos = _mapPanLockPos;
                return;
            }

            _mapPanLockActive = false;
        }

        public override void PostDrawFullscreenMap(ref string mouseText)
        {
            // --- Restore pan after vanilla DrawMap so drag gestures do not drift the map.

            if (!Active || !Main.mapFullscreen || !_mapPanLockActive)
                return;

            if (!IsMapGestureActive(Main.LocalPlayer))
                return;

            Main.mapFullscreenPos = _mapPanLockPos;
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            // --- Suppress HUD, then insert overlay, mode chrome, TmodGUI, and action wheel layers.

            ArchitectModeHud.ApplyLayerSuppression(layers, Active);

            int earlyIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Interface Logic 1"));
            if (earlyIndex == -1)
                earlyIndex = 0;

            layers.Insert(earlyIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprint: Overlay",
                () => { OverlaySystem.DrawOverlayEarly(); return true; },
                InterfaceScaleType.Game));

            int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
                mouseTextIndex = layers.Count;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprint: OverlayFills",
                () => { OverlaySystem.DrawOverlayLate(); return true; },
                InterfaceScaleType.Game));

            mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
                mouseTextIndex = layers.Count;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprintMod: Architect Mode",
                DrawInterface,
                InterfaceScaleType.UI));

            mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
                mouseTextIndex = layers.Count;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprintMod: TmodGUI",
                DrawTmodGuiInterface,
                InterfaceScaleType.UI));

            mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
                mouseTextIndex = layers.Count;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprintMod: ActionWheel",
                ActionWheelRuntime.DrawInterface,
                InterfaceScaleType.UI));
        }

        private bool DrawTmodGuiInterface()
        {
            // --- Draw the active TmodGUI widget when one is open.

            if (_ui?.CurrentState == null || _lastUpdateUiGameTime == null)
                return true;

            _ui.Draw(Main.spriteBatch, _lastUpdateUiGameTime);
            return true;
        }

        public override void OnWorldUnload()
        {
            // --- Close the session, widgets, overlays, and toolbox when leaving the world.

            Active = false;
            JustActivated = false;
            SelectedAction = ToolAction.PIN;
            ResetSessionState();
            RequestCloseAllOnMainThread();
            OverlaySystem.ClearCatalog();
            Toolbox.OnWorldUnload();
        }

        /// <summary>Opens the payload browser for the frontmost blueprint, spawning a projection from selection when needed.</summary>
        public static void OpenPayloadBrowser()
        {
            // --- Ensure a defined projection exists, then open the browser widget.

            if (PayloadBrowser.HasActive && TmodGUI.GetActiveWidget() is TmodPayloadBrowserWidget)
                return;

            Projection? projection = EnsureProjectionForToolbarAction();
            if (projection is null)
                return;

            SoundEngine.PlaySound(SoundID.MenuTick);
            TmodPayloadBrowserWidget.Open(projection);
        }

        /// <summary>Prints the paste manifest for the frontmost blueprint into chat.</summary>
        public static void PrintManifest()
        {
            // --- Resolve a projection, then print its material demand list.

            Projection? projection = EnsureProjectionForToolbarAction();
            if (projection is null)
                return;

            SoundEngine.PlaySound(SoundID.MenuTick);
            ManifestPrinter.Print(projection);
        }

        /// <summary>Toggles cut-from-source on the frontmost projection, capturing from a selection when needed.</summary>
        public static void ToggleCut()
        {
            // --- Resolve a projection, then flip CutSource when the payload allows it.

            // Capture a defined selection into a projection when the overlay is sitting on a selection.
            Projection? projection = EnsureProjectionForToolbarAction();
            if (projection is null)
                return;

            if (!projection.ToggleCut())
                return;

            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        /// <summary>True when the frontmost defined projection currently clears its source on paste.</summary>
        internal static bool IsFrontmostCutEnabled()
        {
            Blueprint? target = ModContent.GetInstance<BlueprintSystem>().GetTopmostDefinedSelectionOrProjection();
            return target is Projection { IsDefined: true, CutSource: true };
        }

        private void UpdateSession(bool uiClicked)
        {
            // --- Skip the opening click, then prefer toolbar hits over world/map gestures.

            if (JustActivated)
                return;

            // Toolbar hit-tests stay in UI zoom (set at the start of UpdateUI).
            if (UpdateToolbar(uiClicked))
                return;

            // World/map tools and SelectionControl screen hit-tests need matching mouse zoom.
            ApplyCursorZoom();
            UpdateWorldInput();
        }

        private bool DrawInterface()
        {
            // --- Draw in the layer's existing UIScaleMatrix (InterfaceScaleType.UI); do not rebind it.

            if (Active)
            {
                Main.mouseText = false;
                Main.instance.GUIBarsDraw();
                DrawToolbar();

                string title = Language.GetTextValue(
                    "Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Header",
                    GetActionLabel(SelectedAction));
                Terraria.Utils.DrawBorderStringBig(
                    Main.spriteBatch,
                    title,
                    new Vector2(Main.screenWidth / 2f, 4f),
                    Color.White,
                    1f,
                    0.5f,
                    0f,
                    -1);

                Main.instance.DrawMouseOver();
                Terraria.Utils.DrawCursorSingle(
                    Main.spriteBatch,
                    Main.cursorColor,
                    float.NaN,
                    Main.cursorScale,
                    Vector2.Zero,
                    0,
                    0);
            }
            else
            {
                DrawEntryTab();
            }

            return true;
        }

        private void UpdateEntryTab(bool uiClicked)
        {
            // --- Hit-test the equip-page tab and open Architect Mode on a fresh click.

            _entryHovered = false;

            if (Active || !ArchitectModeHud.ShouldShowEntryTab(Active))
                return;

            Player player = Main.LocalPlayer;
            var architectPlayer = player.GetModPlayer<ArchitectModePlayer>();
            if (!architectPlayer.CanAccessArchitectGui)
                return;

            if (Main.gameMenu || player.dead)
                return;

            Rectangle rect = ArchitectModeHud.GetEntryTabRect();
            Point mouse = new(Main.mouseX, Main.mouseY);
            if (!rect.Contains(mouse))
                return;

            _entryHovered = true;
            player.mouseInterface = true;
            Main.instance.MouseText(Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.EntryOpen"));

            if (CaptureManager.Instance.Active || !uiClicked)
                return;

            Toggle(true);
            Main.mouseLeftRelease = false;
        }

        private void DrawEntryTab()
        {
            // --- Draw a Camera-row page icon in UI space (layer already applies Main.UIScaleMatrix).

            if (!ArchitectModeHud.ShouldShowEntryTab(Active))
                return;

            Player player = Main.LocalPlayer;
            var architectPlayer = player.GetModPlayer<ArchitectModePlayer>();
            if (!architectPlayer.CanAccessArchitectGui)
                return;

            if (Main.gameMenu || player.dead)
                return;

            Texture2D? icon = TryGetEntryIconTexture();
            if (icon is null)
                return;

            Rectangle rect = ArchitectModeHud.GetEntryTabRect();
            bool captureActive = CaptureManager.Instance.Active;
            bool hovered = _entryHovered && !captureActive;

            // Fit the asset into the 32×32 page-icon cell, then nudge scale on hover like vanilla tabs.
            float fitScale = Math.Min(
                (float)rect.Width / Math.Max(1, icon.Width),
                (float)rect.Height / Math.Max(1, icon.Height));
            float scale = fitScale * (hovered ? 1.08f : 1f);
            Color tint = captureActive ? Color.Gray : hovered ? Color.White : Color.White * 0.92f;

            Vector2 origin = new(icon.Width * 0.5f, icon.Height * 0.5f);
            Vector2 center = new(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
            Main.spriteBatch.Draw(
                icon,
                center,
                null,
                tint,
                0f,
                origin,
                scale,
                SpriteEffects.None,
                0f);
        }

        private Texture2D? TryGetEntryIconTexture()
        {
            // --- Prefer the dedicated Architect Mode icon; fall back to the Blueprint item texture.

            Texture2D? icon = TryGetIconTexture(EntryIconAssetPath);
            if (icon is not null)
                return icon;

            int iconType = ModContent.ItemType<ArchitectsBlueprint>();
            if (TextureAssets.Item[iconType].IsLoaded)
                return TextureAssets.Item[iconType].Value;

            return null;
        }

        private Texture2D? TryGetToolbarIconTexture(int index)
        {
            // --- Resolve the slot icon, swapping Include/Exclude for the inclusion toggle.

            if (index < 0 || index >= ToolbarButtonCount)
                return null;

            if (index == 4)
            {
                return SelectedAction == ToolAction.EXCLUDE
                    ? TryGetIconTexture($"{IconsRoot}/Exclude")
                    : TryGetIconTexture($"{IconsRoot}/Include");
            }

            return TryGetIconTexture(ToolbarIconPaths[index]);
        }

        private void TryLoadIconAsset(string assetPath)
        {
            // --- Request each toolbar/entry icon once during mod load when the asset exists.

            if (_iconAssets.ContainsKey(assetPath))
                return;

            if (!Mod.RequestAssetIfExists<Texture2D>(assetPath, out Asset<Texture2D> asset))
                return;

            _iconAssets[assetPath] = Mod.Assets.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad);
        }

        internal Texture2D? TryGetIconTexture(string assetPath)
        {
            // --- Lazy-load missing icons and return the texture only when fully loaded.

            if (!_iconAssets.TryGetValue(assetPath, out Asset<Texture2D>? asset))
            {
                if (!Mod.RequestAssetIfExists<Texture2D>(assetPath, out asset))
                    return null;

                asset = Mod.Assets.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad);
                _iconAssets[assetPath] = asset;
            }

            return asset.IsLoaded ? asset.Value : null;
        }

        private bool UpdateToolbar(bool uiClicked)
        {
            // --- Hit-test toolbar slots; activate the hovered button on a fresh click.

            _toolbarHoveredIndex = -1;

            Point mouse = new(Main.mouseX, Main.mouseY);
            for (int i = 0; i < ToolbarButtonCount; i++)
            {
                if (!GetToolbarButtonRect(i).Contains(mouse))
                    continue;

                _toolbarHoveredIndex = i;
                Main.LocalPlayer.mouseInterface = true;
                Main.instance.MouseText(GetToolbarTooltip(i));

                if (uiClicked && IsToolbarButtonEnabled(i))
                {
                    ActivateToolbarButton(i);
                    Main.mouseLeftRelease = false;
                }

                return true;
            }

            return false;
        }

        private void DrawToolbar()
        {
            // --- Draw each toolbar slot with selection chrome and a disabled overlay when needed.

            for (int i = 0; i < ToolbarButtonCount; i++)
            {
                Rectangle rect = GetToolbarButtonRect(i);
                bool hovered = i == _toolbarHoveredIndex;
                bool enabled = IsToolbarButtonEnabled(i);
                bool selected = IsToolbarButtonSelected(i);

                Texture2D back = selected
                    ? TextureAssets.InventoryBack14.Value
                    : TextureAssets.InventoryBack2.Value;

                Color tint = enabled ? Color.White : Color.Gray;
                if (!enabled)
                    tint *= 0.55f;

                Main.spriteBatch.Draw(back, rect, tint);
                DrawToolbarButtonIcon(i, rect, hovered && enabled);

                if (!enabled)
                    Main.spriteBatch.Draw(TextureAssets.Cd.Value, rect, Color.White * 0.65f);
            }
        }

        private void ActivateToolbarButton(int index)
        {
            // --- Map stable toolbar indices to utilities, SelectedAction, or session exit.

            SoundEngine.PlaySound(SoundID.MenuTick);

            switch (index)
            {
                case 0:
                    OpenPayloadBrowser();
                    break;
                case 1:
                    PrintManifest();
                    break;
                case 2:
                    SelectedAction = ToolAction.PIN;
                    break;
                case 3:
                    SelectedAction = ToolAction.SET;
                    break;
                case 4:
                    SelectedAction = _toolbarInclusionIsExclude ? ToolAction.EXCLUDE : ToolAction.INCLUDE;
                    _toolbarInclusionIsExclude = !_toolbarInclusionIsExclude;
                    break;
                case 5:
                    SelectedAction = ToolAction.COPY;
                    break;
                case 6:
                    SelectedAction = ToolAction.PASTE;
                    break;
                case 7:
                    ToggleCut();
                    break;
                case 8:
                    Toggle(false);
                    Main.blockMouse = true;
                    Main.mouseLeftRelease = false;
                    break;
            }
        }

        private bool IsToolbarButtonEnabled(int index) =>
            !CaptureManager.Instance.Active;

        private bool IsToolbarButtonSelected(int index) => index switch
        {
            2 => SelectedAction == ToolAction.PIN,
            3 => SelectedAction == ToolAction.SET,
            4 => SelectedAction is ToolAction.INCLUDE or ToolAction.EXCLUDE,
            5 => SelectedAction == ToolAction.COPY,
            6 => SelectedAction == ToolAction.PASTE,
            7 => IsFrontmostCutEnabled(),
            _ => false
        };

        private string GetToolbarTooltip(int index) => index switch
        {
            0 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.OpenPayloadBrowser"),
            1 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.PrintManifest"),
            2 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Pin"),
            3 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Set"),
            4 => SelectedAction == ToolAction.EXCLUDE
                ? Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Exclude")
                : Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Include"),
            5 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Copy"),
            6 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Paste"),
            7 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Cut"),
            8 => Language.GetTextValue("Mods.TheArchitectsBlueprintMod.UI.ArchitectMode.Toolbar.Exit"),
            _ => string.Empty
        };

        private void DrawToolbarButtonIcon(int index, Rectangle rect, bool hovered)
        {
            // --- Inset the icon inside the slot and brighten it while hovered.

            const int inset = 8;
            Rectangle iconRect = new(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2);
            float alpha = hovered ? 1f : 0.85f;

            Texture2D? icon = TryGetToolbarIconTexture(index);
            if (icon is null)
                return;

            Color tint = index == 7 && IsFrontmostCutEnabled()
                ? Palette.ProjectionCutColor
                : Color.White;
            Main.spriteBatch.Draw(icon, iconRect, tint * alpha);
        }

        private void UpdateWorldInput()
        {
            // --- Edge-detect mouse buttons and dispatch SelectedAction through ToolActionManager.

            if (!CanInteractWithWorld())
            {
                PrevLeftDown = Main.mouseLeft;
                PrevRightDown = Main.mouseRight;
                return;
            }

            // Ensure MouseWorld / mouseX match world or map hit-test space before ToolActionManager.
            ApplyCursorZoom();

            Player player = Main.LocalPlayer;
            if (!TryGetCursorTile(player, out Point cursorTile))
            {
                PrevLeftDown = Main.mouseLeft;
                PrevRightDown = Main.mouseRight;
                return;
            }

            var actionManager = ModContent.GetInstance<ToolActionManager>();
            ToolAction action = SelectedAction;

            if (Main.mouseLeft && Main.mouseRight)
            {
                actionManager.Handle(action, ToolActionKey.RightClick, player, cursorTile);
                LeftDragSeenDown = false;
                RightDragSeenDown = false;
            }
            else
            {
                bool leftDown = Main.mouseLeft;
                bool leftClicked = leftDown && !PrevLeftDown;
                PrevLeftDown = leftDown;

                if (leftClicked)
                    actionManager.Handle(action, ToolActionKey.LeftClick, player, cursorTile);
                else if (leftDown && !Main.mouseLeftRelease)
                    actionManager.Handle(action, ToolActionKey.LeftHold, player, cursorTile);
                else if (LeftDragSeenDown && !leftDown)
                {
                    actionManager.Handle(action, ToolActionKey.LeftRelease, player, cursorTile);
                    LeftDragSeenDown = false;
                }

                if (leftDown)
                    LeftDragSeenDown = true;

                bool rightDown = Main.mouseRight;
                bool rightClicked = rightDown && !PrevRightDown;
                PrevRightDown = rightDown;

                if (rightClicked)
                    actionManager.Handle(action, ToolActionKey.RightClick, player, cursorTile);
                else if (rightDown && !Main.mouseRightRelease)
                    actionManager.Handle(action, ToolActionKey.RightHold, player, cursorTile);
                else if (RightDragSeenDown && !rightDown)
                    actionManager.Handle(action, ToolActionKey.RightRelease, player, cursorTile);

                if (rightDown)
                    RightDragSeenDown = true;
            }
        }

        private bool CanInteractWithWorld()
        {
            // --- Require an active session with no conflicting UI, Camera Mode, or hover chrome.

            if (Main.dedServ || Main.gameMenu || !Active)
                return false;

            Player player = Main.LocalPlayer;
            if (player is not { active: true } || player.dead)
                return false;

            if (CaptureManager.Instance.Active)
                return false;

            if (ActionWheel.IsActive || ActionWheel.IsPendingOpen)
                return false;

            if (IsToolbarHovered || IsEntryHovered)
                return false;

            if (TmodGUI.GetActiveWidget() is not null && player.mouseInterface)
                return false;

            return true;
        }

        private void ResetSessionState()
        {
            // --- Clear edge-detect and hover state when the session ends.

            PrevLeftDown = false;
            LeftDragSeenDown = false;
            PrevRightDown = false;
            RightDragSeenDown = false;
            _uiPrevLeftDown = false;
            _mapPanLockActive = false;
            _entryHovered = false;
            _toolbarHoveredIndex = -1;
            _toolbarInclusionIsExclude = false;
        }

        private static bool IsMapGestureActive(Player player) =>
            ModContent.GetInstance<SelectionControl>().IsActivelyGesturing(player)
            || ModContent.GetInstance<InclusionControl>().IsActivelyGesturing(player)
            || ModContent.GetInstance<ProjectionControl>().IsActivelyDragging(player);

        private static Projection? EnsureProjectionForToolbarAction()
        {
            // --- Reuse the frontmost projection, or capture a defined selection into a new one.

            Blueprint? target = ModContent.GetInstance<BlueprintSystem>().GetTopmostDefinedSelectionOrProjection();
            if (target is Projection projection && projection.IsDefined)
                return projection;

            if (target is not Selection selection || !selection.IsDefined)
                return null;

            Player player = Main.LocalPlayer;
            if (player is not { active: true } || player.dead)
                return null;

            BlueprintGuard.RefreshClientGuardMasks(player, skipWhenPasteActive: false);

            if (!BlueprintGuard.TryValidateIncludedTiles(player, selection, out var failure))
            {
                BlueprintGuard.NotifyCaptureBlocked(failure);
                return null;
            }

            return ModContent.GetInstance<ProjectionSystem>().NewProjection(player, selection);
        }

        private static Rectangle GetToolbarButtonRect(int index) =>
            new(ToolbarOriginX + ToolbarButtonSpacing * index, ToolbarOriginY, ToolbarButtonSize, ToolbarButtonSize);
    }

    /// <summary>Shared guards so held architect tools defer to Architect Mode / map-render-only rules.</summary>
    public static class ArchitectModeGuards
    {
        /// <summary>
        /// True when item UseItem/HoldItem should no-op: Architect Mode owns input, or the fullscreen map is
        /// render-only while mode is inactive.
        /// </summary>
        public static bool SuppressHeldToolInput(Player player) =>
            player.whoAmI == Main.myPlayer
            && (ModContent.GetInstance<ArchitectSystem>().Active
                || ArchitectMapTransform.IsMapViewActive);
    }
}
