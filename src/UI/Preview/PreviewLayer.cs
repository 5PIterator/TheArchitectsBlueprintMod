using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>One visual pass in payload preview rendering (wall, fluid, block, wiring, etc.).</summary>
    internal abstract class PreviewLayer
    {
        public abstract string Id { get; }

        public abstract void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY);
    }
}
