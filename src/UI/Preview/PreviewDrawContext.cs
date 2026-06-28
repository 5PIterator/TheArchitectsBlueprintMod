using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Shared state passed to each <see cref="PreviewLayer"/> while drawing a payload region.</summary>
    internal readonly struct PreviewDrawContext
    {
        public const int TilePixelSize = 16;

        public required SpriteBatch SpriteBatch { get; init; }
        public required Vector2 Position { get; init; }
        public required float Scale { get; init; }
        public required Point WorldOrigin { get; init; }
        public required Color Tint { get; init; }

        public int WorldX(int relX) => WorldOrigin.X + relX;
        public int WorldY(int relY) => WorldOrigin.Y + relY;
    }
}
