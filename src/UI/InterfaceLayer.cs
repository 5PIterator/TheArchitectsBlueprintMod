namespace ArchitectsStencilMod.UI;

/// <summary>
/// Z-ordered slots for world/tool interface drawing and hit testing. Lower values draw first (underneath).
/// </summary>
public enum InterfaceLayer
{
    PasteProgress = 0,
    Selection = 1,
    ProjectionDeselect = 2,
    InclusionPreview = 3,
    ProjectionWorld = 4,
    InventoryOutline = 5,
    ProjectionHud = 6,
}
