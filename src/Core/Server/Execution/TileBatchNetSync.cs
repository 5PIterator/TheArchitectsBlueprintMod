using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Sends one MP tile square covering a paste <see cref="TileBatch"/> AABB (or a single cell fallback).</summary>
    internal static class TileBatchNetSync
    {
        /// <summary>Broadcasts the stamped batch square, or a 1×1 around <paramref name="fallbackWorld"/> when empty.</summary>
        public static void Send(Rectangle syncSquare, Point fallbackWorld)
        {
            // --- Server-only: prefer the batch AABB; otherwise sync the mutation cell.
            // The (x,y,width,height) overload uses (x,y) as the top-left corner, not the center.

            if (Main.netMode != NetmodeID.Server)
                return;

            int x;
            int y;
            int width;
            int height;
            if (syncSquare.Width > 0 && syncSquare.Height > 0)
            {
                // Send the exact batch AABB from its top-left corner.
                x = syncSquare.X;
                y = syncSquare.Y;
                width = syncSquare.Width;
                height = syncSquare.Height;
            }
            else
            {
                // Unbatched mutation: sync only the written cell.
                x = fallbackWorld.X;
                y = fallbackWorld.Y;
                width = 1;
                height = 1;
            }

            NetMessage.SendTileSquare(-1, x, y, width, height);
        }
    }
}
