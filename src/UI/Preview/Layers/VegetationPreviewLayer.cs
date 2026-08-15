using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    /// <summary>Tree crowns, leafy branches, and other foliage not covered by block sprites.</summary>
    internal sealed class VegetationPreviewLayer : PreviewLayer
    {
        public static VegetationPreviewLayer Instance { get; } = new();
        public override string Id => "Vegetation";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Overlay tree tops and branches after trunk tiles have already been drawn.

            TreeFoliagePreviewDraw.Draw(in ctx, cell, relX, relY);
        }
    }
}
