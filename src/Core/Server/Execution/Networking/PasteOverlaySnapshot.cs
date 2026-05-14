using Microsoft.Xna.Framework;

namespace ArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Tile highlights for an in-progress paste (built on the server; drawn on the client).
    /// </summary>
    internal readonly struct PasteOverlaySnapshot
    {
        public int OverlayId { get; init; }
        public bool IsPlacePhase { get; init; }
        public Point[] Pending { get; init; }
        public Point[] Clear { get; init; }
        public Point[] Place { get; init; }
        public Point[] Done { get; init; }
    }
}
