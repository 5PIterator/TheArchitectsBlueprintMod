using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Core.Phases;

namespace TheArchitectsBlueprintMod.Core
{
    internal enum PasteProjectileMode : byte
    {
        Place = 0,
        Clear = 1,
    }

    internal enum TileMutationKind : byte
    {
        PlaceTile = 0,
        PlaceObject = 1,
        PlaceWall = 2,
        ClearTile = 3,
        ClearWall = 4,
        ClearWireRed = 5,
        ClearWireBlue = 6,
        ClearWireGreen = 7,
        ClearWireYellow = 8,
        ClearActuator = 9,
        VoidKillTile = 10,
        Sweep = 11,
        PlaceWireRed = 12,
        PlaceWireBlue = 13,
        PlaceWireGreen = 14,
        PlaceWireYellow = 15,
        PlaceActuator = 16,
        ApplyDetailState = 17,
        ApplyPaintState = 18,
        SetLiquid = 19,
        ApplyTileFrame = 20,
        ApplyPayloadBlock = 21,
    }

    /// <summary>One liquid write inside a pool-level <see cref="TileMutationKind.SetLiquid"/> mutation.</summary>
    internal readonly record struct LiquidPoolCell(Point World, int LiquidType, byte Amount);

    /// <summary>
    /// Tile and wall paint colors plus coating flags carried by an <see cref="TileMutationKind.ApplyPaintState"/> mutation.
    /// </summary>
    internal readonly record struct PaintMutationState(
        byte TileColor = 0,
        byte WallColor = 0,
        bool TileInvisible = false,
        bool WallInvisible = false,
        bool TileFullbright = false,
        bool WallFullbright = false)
    {
        /// <summary>True when wall color or wall coatings should be written.</summary>
        public bool WantsWallAppearance =>
            WallColor != 0 || WallInvisible || WallFullbright;

        /// <summary>True when any paint color or coating facet is set.</summary>
        public bool HasAny =>
            TileColor != 0
            || WallColor != 0
            || TileInvisible
            || WallInvisible
            || TileFullbright
            || WallFullbright;
    }

    /// <summary>Queued world mutation executed by <see cref="TileExecutorSystem"/>.</summary>
    internal readonly record struct TileMutation(
        int MutationId,
        int OperationId,
        TileMutationKind Kind,
        Point World,
        ushort Type = 0,
        int Style = 0,
        int Alternate = 0,
        int Direction = -1,
        bool Forced = false,
        bool ConsumeItem = false,
        ClearItemDropStyle ItemDropStyle = ClearItemDropStyle.Drop,
        PasteProjectileMode? ProjectileMode = null,
        int ItemType = 0,
        int ItemStack = 1,
        PayloadCell Payload = default,
        int TargetInventoryIndex = -1,
        short PostPlaceFrameX = -1,
        short PostPlaceFrameY = -1,
        PaintMutationState Paint = default,
        Rectangle SyncSquare = default)
    {
        public const short NoPostPlaceFrame = -1;

        public bool HasPostPlaceFrame => PostPlaceFrameX != NoPostPlaceFrame;

        public static TileMutation New(
            int mutationId,
            int operationId,
            TileMutationKind kind,
            Point world,
            ushort type = 0,
            int style = 0,
            int alternate = 0,
            int direction = -1,
            bool forced = false,
            bool consumeItem = false,
            ClearItemDropStyle itemDropStyle = ClearItemDropStyle.Drop,
            PasteProjectileMode? projectileMode = null,
            int itemType = 0,
            int itemStack = 1,
            PayloadCell payload = default,
            int targetInventoryIndex = -1,
            short postPlaceFrameX = NoPostPlaceFrame,
            short postPlaceFrameY = NoPostPlaceFrame,
            PaintMutationState paint = default,
            Rectangle syncSquare = default) =>
            new(
                MutationId: mutationId,
                OperationId: operationId,
                Kind: kind,
                World: world,
                Type: type,
                Style: style,
                Alternate: alternate,
                Direction: direction,
                Forced: forced,
                ConsumeItem: consumeItem,
                ItemDropStyle: itemDropStyle,
                ProjectileMode: projectileMode,
                ItemType: itemType,
                ItemStack: itemStack,
                Payload: payload,
                TargetInventoryIndex: targetInventoryIndex,
                PostPlaceFrameX: postPlaceFrameX,
                PostPlaceFrameY: postPlaceFrameY,
                Paint: paint,
                SyncSquare: syncSquare);
    }
}
