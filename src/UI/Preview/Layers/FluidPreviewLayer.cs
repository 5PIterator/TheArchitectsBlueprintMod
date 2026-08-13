using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    internal sealed class FluidPreviewLayer : PreviewLayer
    {
        public static FluidPreviewLayer Instance { get; } = new();
        public override string Id => "Fluid";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Skip near-empty cells, then draw through the shared liquid texture/fill path.

            if (cell.Liquid.Amount <= 14)
                return;

            LiquidPreviewResolver.Draw(in ctx, cell.Liquid.Type, cell.Liquid.Amount, relX, relY);
        }
    }
}
