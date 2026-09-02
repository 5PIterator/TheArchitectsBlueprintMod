namespace TheArchitectsBlueprintMod.Core
{
    internal enum PasteRejectReason : byte
    {
        None = 0,
        InvalidProjection = 1,
        InvalidPayload = 2,
        InvalidPacket = 3,
        NotHost = 4,
        AreaLimit = 5,
        VolumeLimit = 6,
        PlayerOperationLimit = 7,
        ServerOperationLimit = 8,
        AlreadyActive = 9,
        OutOfWorldTiles = 10,
        UnrevealedTiles = 11,
        OutOfRange = 12,
    }
}
