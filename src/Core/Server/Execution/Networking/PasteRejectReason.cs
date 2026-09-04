namespace TheArchitectsBlueprintMod.Core
{
    internal enum PasteRejectReason : byte
    {
        None = 0,
        InvalidProjection = 1,
        InvalidPayload = 2,
        InvalidPacket = 3,
        AreaLimit = 4,
        VolumeLimit = 5,
        PlayerOperationLimit = 6,
        ServerOperationLimit = 7,
        AlreadyActive = 8,
        OutOfWorldTiles = 9,
        UnrevealedTiles = 10,
        OutOfRange = 11,
    }
}
