using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using System.Collections.Generic;
using ReLogic.Graphics;
using Terraria.GameContent;
using ArchitectsStencilMod.Utils;
using Terraria.ID;
namespace ArchitectsStencilMod.Debug;

public class CursorCoordinates : ModSystem
{
    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
        if (mouseTextIndex != -1)
        {
            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "CursorCoordinates: Display",
                delegate
                {
                    DrawCursorCoordinates();
                    return true;
                },
                InterfaceScaleType.UI)
            );
        }

        /*if (mouseTextIndex != -1)
        {
            layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                "CursorRectangle: Display",
                delegate
                {
                    DrawCursorRectangle();
                    return true;
                },
                InterfaceScaleType.UI)
            );
        }*/
    }

    public static void DrawCursorCoordinates()
    {
        Vector2 cursorWorldPosition = Main.MouseWorld;
        int tileX = Terraria.Utils.ToTileCoordinates(cursorWorldPosition).X;
        int tileY = Terraria.Utils.ToTileCoordinates(cursorWorldPosition).Y;
        string coordinatesText = $"({tileX}, {tileY})";

        Vector2 cursorScreenPosition = Main.MouseScreen;
        Vector2 textPosition = cursorScreenPosition - new Vector2(0, 20);

        Main.spriteBatch.DrawString(
            FontAssets.MouseText.Value,
            coordinatesText,
            textPosition,
            Color.White
        );
    }

    // (Removed extra debug instrumentation helpers)
}
