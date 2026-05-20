using TheArchitectsBlueprintMod.UI.TmodGUI.Widgets;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.UI.TmodGUI
{
    /// <summary>Static entry point for showing and closing TmodGUI widgets.</summary>
    public static class TmodGUI
    {
        private static TmodWidgetBase? _activeWidget;
        public static bool IsOpen => _activeWidget is not null;
        public static bool IsBlockingInput => false;

        public static void Show(TmodWidgetBase widget)
        {
            if (Main.dedServ || widget == null)
                return;

            TmodGUISystem system = ModContent.GetInstance<TmodGUISystem>();
            if (system == null)
                return;

            Close();
            _activeWidget = widget;
            widget.Activate();
            system.SetActiveWidget(widget);
        }

        public static void Close()
        {
            if (_activeWidget is null)
                return;

            TmodWidgetBase closed = _activeWidget;
            _activeWidget = null;
            closed.OnClosed?.Invoke();

            TmodGUISystem system = ModContent.GetInstance<TmodGUISystem>();
            if (system == null)
                return;

            system.SetActiveWidget(null);
            Main.blockInput = false;
        }

        public static void CloseAll()
        {
            Close();
            Main.blockInput = false;
        }

        internal static TmodWidgetBase? GetActiveWidget() => _activeWidget;
    }
}
