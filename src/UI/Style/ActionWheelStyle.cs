using Microsoft.Xna.Framework;

namespace TheArchitectsBlueprintMod.UI.Style
{
    /// <summary>Layout and color options for the tool action wheel.</summary>
    public sealed class ActionWheelStyle
    {
        public float RadiusPx { get; set; } = 45f;
        public float InnerDeadzoneRadiusPx { get; set; } = 20f;
        public float MouseHoverRadiusPx { get; set; } = 19f;
        public float CenterHoverRadiusPx { get; set; } = 22f;
        public float GamepadCursorDistancePx { get; set; } = 40f;
        public float SlotScale { get; set; } = 1f;
        public float CenterSlotScale { get; set; } = 1f;
        public bool CloseOnClickOutside { get; set; } = true;

        public Color Background { get; set; } = Color.White;
        public Color HoverBackground { get; set; } = Color.White;
        public Color TextColor { get; set; } = Color.White;
        public Color SelectedTextColor { get; set; } = new Color(255, 230, 120, 255);
    }
}
