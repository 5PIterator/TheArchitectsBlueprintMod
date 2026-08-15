using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    internal sealed class FluidPreviewLayer : PreviewLayer
    {
        public static FluidPreviewLayer Instance { get; } = new();
        public override string Id => "Fluid";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Skip near-empty cells, then draw body with a rim only on exposed surfaces.

            if (cell.Liquid.Amount <= 14)
                return;

            bool drawRim = !NeighborHasDrawableLiquid(in ctx, relX, relY - 1);
            LiquidPreviewResolver.Draw(in ctx, cell.Liquid.Type, cell.Liquid.Amount, relX, relY, drawRim);
        }

        /// <summary>Returns whether the relative neighbor cell has enough liquid to draw in preview.</summary>
        private static bool NeighborHasDrawableLiquid(in PreviewDrawContext ctx, int relX, int relY)
        {
            // --- Look up the payload neighbor and apply the same amount threshold as this layer.

            if (ctx.CellLookup is null)
                return false;

            if (!ctx.CellLookup.TryGetValue(new Point(relX, relY), out PayloadCell neighbor))
                return false;

            return neighbor.Liquid.Amount > 14;
        }
    }
}
