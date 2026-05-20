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

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _ui = new UserInterface();
        }

        public override void Unload()
        {
            TmodGUI.CloseAll();
            _ui = null;
            _lastUpdateUiGameTime = null;
        }

        public override void OnWorldUnload()
        {
            TmodGUI.CloseAll();
        }

        internal void SetActiveWidget(TmodWidgetBase? widget)
        {
            _ui?.SetState(widget);
        }

        public override void UpdateUI(GameTime gameTime)
        {
            _lastUpdateUiGameTime = gameTime;

            if (Main.gameMenu)
            {
                TmodGUI.CloseAll();
                return;
            }

            if (_ui?.CurrentState == null)
                return;

            _ui.Update(gameTime);

            if (_ui.CurrentState is TmodWidgetBase widget &&
                widget.WantsMouseCapture &&
                Main.LocalPlayer != null)
            {
                Main.LocalPlayer.mouseInterface = true;
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex == -1)
                mouseTextIndex = layers.Count;

            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
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
