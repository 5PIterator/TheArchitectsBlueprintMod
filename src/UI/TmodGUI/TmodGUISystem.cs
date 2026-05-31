using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using TheArchitectsBlueprintMod.UI.TmodGUI.Widgets;

namespace TheArchitectsBlueprintMod.UI.TmodGUI
{
    [Autoload(Side = ModSide.Client)]
    public sealed class TmodGUISystem : ModSystem
    {
        private UserInterface? _ui;
        private GameTime? _lastUpdateUiGameTime;
        private bool _closeAllPending;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _ui = new UserInterface();
        }

        public override void Unload()
        {
            _closeAllPending = false;
            TmodGUI.CloseAll();
            _ui = null;
            _lastUpdateUiGameTime = null;
        }

        public override void OnWorldUnload()
        {
            RequestCloseAllOnMainThread();
        }

        /// <summary>Defer GUI teardown to <see cref="UpdateUI"/> so texture disposal stays on the main thread.</summary>
        private void RequestCloseAllOnMainThread() => _closeAllPending = true;

        internal void SetActiveWidget(TmodWidgetBase? widget)
        {
            _ui?.SetState(widget);
        }

        public override void UpdateUI(GameTime gameTime)
        {
            _lastUpdateUiGameTime = gameTime;

            if (_closeAllPending)
            {
                _closeAllPending = false;
                TmodGUI.CloseAll();
            }

            if (Main.gameMenu)
            {
                TmodGUI.CloseAll();
                return;
            }

            if (_ui?.CurrentState == null)
                return;

            _ui.Update(gameTime);
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int insertIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (insertIndex == -1)
                insertIndex = layers.Count;

            layers.Insert(insertIndex, new LegacyGameInterfaceLayer(
                "TheArchitectsBlueprintMod: TmodGUI",
                DrawInterface,
                InterfaceScaleType.UI));
        }

        private bool DrawInterface()
        {
            if (_ui?.CurrentState == null || _lastUpdateUiGameTime == null)
                return true;

            _ui.Draw(Main.spriteBatch, _lastUpdateUiGameTime);
            return true;
        }
    }
}
