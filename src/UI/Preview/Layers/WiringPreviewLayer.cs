using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    /// <summary>Reserved for wire, actuator, and other wiring overlays.</summary>
    internal sealed class WiringPreviewLayer : PreviewLayer
    {
        public static WiringPreviewLayer Instance { get; } = new();
        public override string Id => "Wiring";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
        }
    }
}
