using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Shared payload-tile materialization and source-rect resolution for preview layers.</summary>
    internal static class PreviewTileDraw
    {
        /// <summary>Sanitized tile plus draw metadata prepared from a payload cell.</summary>
        internal readonly struct PreparedTile
        {
            public required Tile Tile { get; init; }
            public required ushort TileType { get; init; }
            public required TileDrawing Renderer { get; init; }
        }

        /// <summary>Resolved sprite batch inputs for a single preview tile draw.</summary>
        internal readonly struct DrawSprite
        {
            public required Texture2D Texture { get; init; }
            public required Rectangle Source { get; init; }
            public required SpriteEffects Effects { get; init; }
            public required int TileWidth { get; init; }
            public required int TileHeight { get; init; }
            public required int TileTop { get; init; }
            public required int HalfBrickHeight { get; init; }
        }

        /// <summary>Materializes a payload cell as a wall-scrubbed tile ready for preview drawing.</summary>
        public static bool TryPrepare(PayloadCell cell, out PreparedTile prepared)
        {
            // --- Build a visible tile snapshot and strip wall frame/type state before any draw API use.

            prepared = default;
            if (!cell.Detail.HasTile)
                return false;

            Tile tile = cell.ToTile();
            if (!TileDrawing.IsVisible(tile))
                return false;

            ushort tileType = cell.Block.Type;
            Main.instance.LoadTiles(tileType);

            // Preview tile draw data must not depend on wall animation or wall type state.
            TileWallWireStateData state = tile.Get<TileWallWireStateData>();
            int tileFrameNumber = state.TileFrameNumber;
            state.WallFrameX = 0;
            state.WallFrameY = 0;
            state.WallFrameNumber = 0;
            state.TileFrameNumber = tileFrameNumber;
            tile.Get<TileWallWireStateData>() = state;
            tile.Get<WallTypeData>() = default;

            prepared = new PreparedTile
            {
                Tile = tile,
                TileType = tileType,
                Renderer = Main.instance.TilesRenderer,
            };
            return true;
        }

        /// <summary>Resolves the minecart-track source rectangle without generic tile animation offsets.</summary>
        public static bool TryResolveMinecartTrack(in PreparedTile prepared, out DrawSprite sprite)
        {
            // --- Use Minecart.GetSourceRect with the stored frame ID and track-only animation frame.

            sprite = default;
            if (prepared.TileType != TileID.MinecartTrack)
                return false;

            // Track junction/style is encoded in FrameX as a frame ID, not a pixel offset.
            int frameId = prepared.Tile.TileFrameX;
            int animationFrame = Main.tileFrame[TileID.MinecartTrack];
            Rectangle source = Minecart.GetSourceRect(frameId, animationFrame);
            if (source.Width <= 0 || source.Height <= 0)
                return false;

            Texture2D texture = prepared.Renderer.GetTileDrawTexture(prepared.Tile, 0, 0)
                ?? TextureAssets.Tile[TileID.MinecartTrack].Value;

            sprite = new DrawSprite
            {
                Texture = texture,
                Source = source,
                Effects = SpriteEffects.None,
                TileWidth = PreviewDrawContext.TilePixelSize,
                TileHeight = PreviewDrawContext.TilePixelSize,
                TileTop = 0,
                HalfBrickHeight = 0,
            };
            return true;
        }

        /// <summary>Resolves generic tile draw data for non-track tiles via Terraria's tile renderer.</summary>
        public static bool TryResolveGeneric(in PreparedTile prepared, out DrawSprite sprite)
        {
            // --- Query GetTileDrawData with isolated coordinates, then build the source rect from its outputs.

            sprite = default;

            // Prefer the dedicated track path; generic animation offsets are wrong for FrameX frame IDs.
            if (prepared.TileType == TileID.MinecartTrack)
                return false;

            // Use origin-local coordinates so preview drawing does not sample projection world tiles.
            const int payloadX = 0;
            const int payloadY = 0;

            short frameX = prepared.Tile.TileFrameX;
            short frameY = prepared.Tile.TileFrameY;
            prepared.Renderer.GetTileDrawData(
                payloadX,
                payloadY,
                prepared.Tile,
                prepared.TileType,
                ref frameX,
                ref frameY,
                out int tileWidth,
                out int tileHeight,
                out int tileTop,
                out int halfBrickHeight,
                out int addFrX,
                out int addFrY,
                out SpriteEffects effects,
                out Texture2D glowTexture,
                out Rectangle glowSourceRect,
                out Color glowColor);

            Texture2D texture = prepared.Renderer.GetTileDrawTexture(prepared.Tile, payloadX, payloadY);
            if (texture is null)
                return false;

            sprite = new DrawSprite
            {
                Texture = texture,
                Source = new Rectangle(frameX + addFrX, frameY + addFrY, tileWidth, tileHeight - halfBrickHeight),
                Effects = effects,
                TileWidth = tileWidth,
                TileHeight = tileHeight,
                TileTop = tileTop,
                HalfBrickHeight = halfBrickHeight,
            };
            return true;
        }

        /// <summary>Chooses the track-specific or generic source rectangle for a prepared payload tile.</summary>
        public static bool TryResolve(in PreparedTile prepared, out DrawSprite sprite)
        {
            // --- Prefer minecart track framing, otherwise fall back to generic tile draw data.

            if (TryResolveMinecartTrack(in prepared, out sprite))
                return true;

            return TryResolveGeneric(in prepared, out sprite);
        }

        /// <summary>Computes the unscaled draw origin for a resolved tile sprite within the preview grid.</summary>
        public static Vector2 GetDrawPosition(in PreviewDrawContext ctx, int relX, int relY, in DrawSprite sprite)
        {
            // --- Offset by grid cell, centering wider sprites and applying tileTop / half-brick shifts.

            return ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - (sprite.TileWidth - PreviewDrawContext.TilePixelSize) / 2f,
                relY * PreviewDrawContext.TilePixelSize + sprite.TileTop + sprite.HalfBrickHeight);
        }
    }
}
