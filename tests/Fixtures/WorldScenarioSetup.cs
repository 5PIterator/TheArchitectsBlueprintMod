#if DEBUG
#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Tests.Fixtures
{
    /// <summary>Places or inspects world tiles for scenario setup (not fixture registration).</summary>
    internal static class WorldScenarioSetup
    {
        internal static void PlacePayload(BlueprintFixtureChain chain, Rectangle tileArea, Payload payload)
        {
            chain.ProtectWorldRegion(tileArea);
            for (int y = 0; y < payload.Height; y++)
            {
                for (int x = 0; x < payload.Width; x++)
                {
                    int wx = tileArea.X + x;
                    int wy = tileArea.Y + y;
                    if (!WorldGen.InWorld(wx, wy))
                        continue;

                    SuppressTileDrop.KillTile(wx, wy);
                    WorldGen.KillWall(wx, wy, fail: false);

                    if (!payload.TryGetCell(new Point(x, y), out PayloadCell cell) || cell.IsEmpty)
                        continue;

                    if (cell.Wall.Type != WallID.None)
                        WorldGen.PlaceWall(wx, wy, cell.Wall.Type, mute: true);

                    if (cell.HasTile())
                    {
                        int style = 0;
                        int alternate = 0;
                        cell.GetTileInfo(ref style, ref alternate);
                        WorldGen.PlaceTile(wx, wy, cell.Block.Type, mute: true, forced: true, plr: -1, style: style);
                    }
                }
            }
        }
    }
}
#endif
