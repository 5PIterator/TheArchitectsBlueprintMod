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

    /// <summary>Queued world mutation executed by <see cref="TileExecutorSystem"/>.</summary>
    internal readonly record struct TileMutation(
        int MutationId,
        int OperationId,
        TileMutationKind Kind,
        Point World,
        ushort Type = 0,
        int Style = 0,
        int Alternate = 0,
        bool Forced = false,
        bool ConsumeItem = false,
        ClearItemDropStyle ItemDropStyle = ClearItemDropStyle.Drop,
        PasteProjectileMode? ProjectileMode = null,
        int ItemType = 0,
        int ItemStack = 1,
        PayloadCell Payload = default,
        int TargetInventoryIndex = -1)
    {
        public static TileMutation New(
            int mutationId,
            int operationId,
            TileMutationKind kind,
            Point world,
            ushort type = 0,
            int style = 0,
            int alternate = 0,
            bool forced = false,
            bool consumeItem = false,
            ClearItemDropStyle itemDropStyle = ClearItemDropStyle.Drop,
            PasteProjectileMode? projectileMode = null,
            int itemType = 0,
            int itemStack = 1,
            PayloadCell payload = default,
            int targetInventoryIndex = -1) =>
            new(
                MutationId: mutationId,
                OperationId: operationId,
                Kind: kind,
                World: world,
                Type: type,
                Style: style,
                Alternate: alternate,
                Forced: forced,
                ConsumeItem: consumeItem,
                ItemDropStyle: itemDropStyle,
                ProjectileMode: projectileMode,
                ItemType: itemType,
                ItemStack: itemStack,
                Payload: payload,
                TargetInventoryIndex: targetInventoryIndex);
    }
}
