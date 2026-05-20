using System;
using Terraria.UI;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Widgets
{
    /// <summary>Base class for all TmodGUI screens.</summary>
    public abstract class TmodWidgetBase : UIState
    {
        public Action? OnClosed { get; set; }
        internal virtual bool WantsMouseCapture => false;

        public sealed override void OnInitialize()
        {
            Build();
            Recalculate();
        }

        protected abstract void Build();

        internal void RequestClose() => TmodGUI.Close();
    }
}
