using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview.Layers
{
    /// <summary>Wire, actuator, and other wiring overlays for payload tile previews.</summary>
    internal sealed class WiringPreviewLayer : PreviewLayer
    {
        public static WiringPreviewLayer Instance { get; } = new();
        public override string Id => "Wiring";

        public override void Draw(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            // --- Respect builder toggles, then draw each wire color and actuators with neighbor connectivity.

            PayloadCell.WireData wire = cell.Wire;
            if (!wire.RedWire && !wire.BlueWire && !wire.GreenWire && !wire.YellowWire && !wire.HasActuator)
                return;

            if (BuilderToggle.HideAllWires.CurrentState != 0)
                return;

            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize,
                relY * PreviewDrawContext.TilePixelSize);

            if (wire.RedWire && BuilderToggle.RedWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire.Value, w => w.RedWire);

            if (wire.BlueWire && BuilderToggle.BlueWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire2.Value, w => w.BlueWire);

            if (wire.GreenWire && BuilderToggle.GreenWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire3.Value, w => w.GreenWire);

            if (wire.YellowWire && BuilderToggle.YellowWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire4.Value, w => w.YellowWire);

            if (wire.HasActuator && BuilderToggle.ActuatorsVisibility.CurrentState != 0)
                DrawActuator(ctx.SpriteBatch, pos, ctx.Scale, ctx.Tint);
        }

        /// <summary>Draws one wire color at a cell using vanilla junction sprite frames.</summary>
        private static void DrawWireColor(
            in PreviewDrawContext ctx,
            int relX,
            int relY,
            Vector2 pos,
            Texture2D texture,
            Func<PayloadCell.WireData, bool> hasWire)
        {
            // --- Resolve four-neighbor connectivity, pick the matching atlas rect, and draw.

            bool up = ctx.NeighborHasWire(relX, relY, 0, -1, hasWire);
            bool down = ctx.NeighborHasWire(relX, relY, 0, 1, hasWire);
            bool left = ctx.NeighborHasWire(relX, relY, -1, 0, hasWire);
            bool right = ctx.NeighborHasWire(relX, relY, 1, 0, hasWire);
            Rectangle source = GetWireSourceRect(up, down, left, right);
            ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, SpriteEffects.None, 0f);
        }

        /// <summary>Draws the actuator overlay sprite for a payload cell.</summary>
        private static void DrawActuator(SpriteBatch sb, Vector2 pos, float scale, Color tint)
        {
            // --- Draw the full actuator texture centered on the tile.

            Texture2D texture = TextureAssets.Actuator.Value;
            var source = new Rectangle(0, 0, texture.Width, texture.Height);
            sb.Draw(texture, pos * scale, source, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>Maps four-neighbor wire connectivity to the vanilla wire atlas rectangle.</summary>
        private static Rectangle GetWireSourceRect(bool up, bool down, bool left, bool right)
        {
            // --- Mirror Main.DrawWires junction lookup for a single wire color.

            if (up)
            {
                if (down)
                {
                    if (left)
                        return right ? new Rectangle(18, 18, 16, 16) : new Rectangle(54, 0, 16, 16);
                    return right ? new Rectangle(36, 0, 16, 16) : new Rectangle(0, 0, 16, 16);
                }

                if (left)
                    return right ? new Rectangle(0, 18, 16, 16) : new Rectangle(54, 18, 16, 16);
                return right ? new Rectangle(36, 18, 16, 16) : new Rectangle(36, 36, 16, 16);
            }

            if (down)
            {
                if (left)
                    return right ? new Rectangle(72, 0, 16, 16) : new Rectangle(72, 18, 16, 16);
                return right ? new Rectangle(0, 36, 16, 16) : new Rectangle(18, 36, 16, 16);
            }

            if (left)
                return right ? new Rectangle(18, 0, 16, 16) : new Rectangle(54, 36, 16, 16);
            return right ? new Rectangle(72, 36, 16, 16) : new Rectangle(0, 54, 16, 16);
        }
    }
}
