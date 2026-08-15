using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using Terraria.GameContent.Liquid;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Resolves liquid preview colors from textures and draws a two-tone fill.</summary>
    internal static class LiquidPreviewResolver
    {
        private static readonly Color NeutralBody = new(120, 140, 160, 180);
        private static readonly Color NeutralRim = new(200, 210, 220, 220);
        private static readonly Dictionary<int, (Color Body, Color Rim)> ColorCache = [];

        /// <summary>Draws one liquid cell as a body fill, optionally with a surface rim.</summary>
        public static void Draw(
            in PreviewDrawContext ctx,
            int liquidType,
            byte amount,
            int relX,
            int relY,
            bool drawRim)
        {
            // --- Convert amount to height, resolve colors, then draw body and rim only when exposed.

            int waterSize = (amount + 1) / 16;
            if (waterSize <= 0)
                return;

            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize,
                relY * PreviewDrawContext.TilePixelSize + (PreviewDrawContext.TilePixelSize - waterSize));

            ResolveColors(liquidType, out Color body, out Color rim);
            DrawTwoToneFill(in ctx, body, rim, waterSize, pos, drawRim);
        }

        /// <summary>Resolves body and rim colors from the liquid texture, with a cached fallback.</summary>
        public static void ResolveColors(int liquidType, out Color body, out Color rim)
        {
            // --- Reuse a cached sample when possible; otherwise read opaque pixels from the liquid sheet.

            if (ColorCache.TryGetValue(liquidType, out (Color Body, Color Rim) cached))
            {
                body = cached.Body;
                rim = cached.Rim;
                return;
            }

            if (TryGetTexture(liquidType, out Texture2D texture)
                && TrySampleColors(texture, out body, out rim))
            {
                ColorCache[liquidType] = (body, rim);
                return;
            }

            // Shimmer has no sheet in LiquidRenderer; approximate from the vanilla shimmer base color.
            if (liquidType == LiquidID.Shimmer)
            {
                Vector4 shimmer = LiquidRenderer.GetShimmerBaseColor(0f, 0f);
                body = new Color(shimmer) * 0.85f;
                body.A = 200;
                rim = Color.Lerp(new Color(shimmer), Color.White, 0.45f);
                rim.A = 230;
                ColorCache[liquidType] = (body, rim);
                return;
            }

            body = NeutralBody;
            rim = NeutralRim;
            ColorCache[liquidType] = (body, rim);
        }

        /// <summary>Resolves a liquid texture for vanilla water styles or ModLiquidLib assets.</summary>
        public static bool TryGetTexture(int liquidType, out Texture2D texture)
        {
            // --- Map vanilla liquids to water-style slots, then ask ModLiquidLib for registered assets.

            texture = null!;

            if (TryGetVanillaWaterStyleIndex(liquidType, out int styleIndex)
                && TryGetWaterStyleTexture(styleIndex, out texture))
            {
                return true;
            }

            Texture2D? modTexture = ModLiquidLibBridge.TryGetLiquidTexture(liquidType);
            if (modTexture is null)
                return false;

            texture = modTexture;
            return true;
        }

        /// <summary>Maps vanilla liquid IDs onto LiquidRenderer water-style texture indices.</summary>
        private static bool TryGetVanillaWaterStyleIndex(int liquidType, out int styleIndex)
        {
            // --- Encode the fixed vanilla liquid→style table used by the classic liquid sheets.

            switch (liquidType)
            {
                case LiquidID.Water:
                    styleIndex = 0;
                    return true;
                case LiquidID.Lava:
                    styleIndex = 1;
                    return true;
                case LiquidID.Honey:
                    styleIndex = 11;
                    return true;
                default:
                    styleIndex = -1;
                    return false;
            }
        }

        /// <summary>Loads a LiquidRenderer water-style texture by style index.</summary>
        private static bool TryGetWaterStyleTexture(int styleIndex, out Texture2D texture)
        {
            // --- Bounds-check the renderer sheet array and unwrap the Asset value.

            texture = null!;
            Asset<Texture2D>[] textures = LiquidRenderer.Instance._liquidTextures;
            if (styleIndex < 0 || styleIndex >= textures.Length || textures[styleIndex] is null)
                return false;

            texture = textures[styleIndex].Value;
            return texture is not null;
        }

        /// <summary>Samples body (average) and rim (brightest) colors from a liquid texture sheet.</summary>
        private static bool TrySampleColors(Texture2D texture, out Color body, out Color rim)
        {
            // --- Read one liquid cell frame and derive two tones from its opaque pixels.

            body = NeutralBody;
            rim = NeutralRim;

            Rectangle sample = texture.Width >= 32 && texture.Height >= 64
                ? new Rectangle(16, 48, 16, 16)
                : new Rectangle(
                    0,
                    0,
                    Math.Min(PreviewDrawContext.TilePixelSize, texture.Width),
                    Math.Min(PreviewDrawContext.TilePixelSize, texture.Height));

            if (sample.Width <= 0 || sample.Height <= 0)
                return false;

            var data = new Color[sample.Width * sample.Height];
            try
            {
                texture.GetData(0, sample, data, 0, data.Length);
            }
            catch
            {
                return false;
            }

            long sumR = 0;
            long sumG = 0;
            long sumB = 0;
            long sumA = 0;
            int count = 0;
            float bestLuma = -1f;
            Color brightest = NeutralRim;

            for (int i = 0; i < data.Length; i++)
            {
                Color pixel = data[i];
                if (pixel.A < 16)
                    continue;

                sumR += pixel.R;
                sumG += pixel.G;
                sumB += pixel.B;
                sumA += pixel.A;
                count++;

                float luma = pixel.R * 0.3f + pixel.G * 0.59f + pixel.B * 0.11f;
                if (luma > bestLuma)
                {
                    bestLuma = luma;
                    brightest = pixel;
                }
            }

            if (count <= 0)
                return false;

            body = new Color(
                (byte)(sumR / count),
                (byte)(sumG / count),
                (byte)(sumB / count),
                (byte)Math.Min(200, sumA / count));
            rim = brightest;
            rim.A = Math.Max(body.A, (byte)220);
            return true;
        }

        /// <summary>Draws a solid body rectangle and, when exposed, a multi-pixel surface rim.</summary>
        private static void DrawTwoToneFill(
            in PreviewDrawContext ctx,
            Color bodyColor,
            Color rimColor,
            int waterSize,
            Vector2 pos,
            bool drawRim)
        {
            // --- Fill the liquid body, then overlay a brighter top band only on free surfaces.

            const int RimHeight = 3;

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            var src = new Rectangle(0, 0, 1, 1);
            float tintAlpha = ctx.Tint.A / 255f;

            Color body = bodyColor * tintAlpha;
            body.A = (byte)(bodyColor.A * tintAlpha);
            ctx.SpriteBatch.Draw(
                pixel,
                pos * ctx.Scale,
                src,
                body,
                0f,
                Vector2.Zero,
                new Vector2(PreviewDrawContext.TilePixelSize, waterSize) * ctx.Scale,
                SpriteEffects.None,
                0f);

            if (!drawRim || waterSize <= 1)
                return;

            int rimHeight = Math.Min(RimHeight, waterSize);
            Color rim = rimColor * tintAlpha;
            rim.A = (byte)(rimColor.A * tintAlpha);
            ctx.SpriteBatch.Draw(
                pixel,
                pos * ctx.Scale,
                src,
                rim,
                0f,
                Vector2.Zero,
                new Vector2(PreviewDrawContext.TilePixelSize, rimHeight) * ctx.Scale,
                SpriteEffects.None,
                0f);
        }
    }
}
