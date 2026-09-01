using Terraria;
using Terraria.Map;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.UI
{
    /// <summary>
    /// Draws architect blueprint/projection overlays on the fullscreen map.
    /// </summary>
    public sealed class ArchitectOverlayMapLayer : ModMapLayer
    {
        public override void Draw(ref MapOverlayDrawContext context, ref string text)
        {
            if (Main.gameMenu || !Main.mapFullscreen)
                return;

            OverlaySystem.DrawMapOverlays(ref context);
        }
    }
}
