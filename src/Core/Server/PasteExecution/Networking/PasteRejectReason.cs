namespace ArchitectsStencilMod.Core
{
    internal enum PasteRejectReason : byte
    {
        None = 0,
        InvalidProjection = 1,
        InvalidPacket = 2,
        NotHost = 3,
        AreaLimit = 4,
        VolumeLimit = 5,
        PlayerOperationLimit = 6,
        ServerOperationLimit = 7,
        AlreadyActive = 8,
    }
}
