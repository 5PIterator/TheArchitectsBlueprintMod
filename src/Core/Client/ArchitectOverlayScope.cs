using System;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Overlay categories gated by the locally held architect tool, similar to vanilla wire visibility on <c>Item.mech</c> tools.
    /// </summary>
    [Flags]
    public enum ArchitectOverlayScope
    {
        None = 0,
        Selection = 1 << 0,
        Projection = 1 << 1,
        Inclusion = 1 << 2,
        Paste = 1 << 3,
        All = Selection | Projection | Inclusion | Paste,
    }
}
