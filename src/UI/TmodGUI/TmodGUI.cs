using TheArchitectsBlueprintMod.Core.Architect;
using TheArchitectsBlueprintMod.UI.TmodGUI.Elements;
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
        public static bool IsBlockingInput => IsOpen && TmodWindowPanel.IsCapturingTextInput;

        public static void Show(TmodWidgetBase widget)
        {
            if (Main.dedServ || widget == null)
                return;

            ArchitectSystem system = ModContent.GetInstance<ArchitectSystem>();
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

            ArchitectSystem system = ModContent.GetInstance<ArchitectSystem>();
            if (system == null)
                return;

            system.SetActiveWidget(null);
            TmodWindowPanel.ResetInputState();
            Main.blockInput = false;
        }

        public static void CloseAll()
        {
            Close();
            TmodWindowPanel.ResetInputState();
            Main.blockInput = false;
        }

        internal static TmodWidgetBase? GetActiveWidget() => _activeWidget;
    }
}
