using Terraria;
using Terraria.Map;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>
    /// Draws architect blueprint/projection overlays on the fullscreen map.
    /// </summary>
    public sealed class ArchitectOverlayMapLayer : ModMapLayer
    {
        public override void Draw(ref MapOverlayDrawContext context, ref string text)
        {
            if (Main.gameMenu || !ArchitectMapTransform.IsMapViewActive)
                return;

            ArchitectMapTransform.SetCurrent(in context);
            try
            {
                OverlaySystem.DrawMapOverlays(ref context);
            }
            finally
            {
                ArchitectMapTransform.ClearCurrent();
            }
        }
    }
}
