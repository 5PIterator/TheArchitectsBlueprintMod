using ArchitectsStencilMod.Debug;
using Terraria.ModLoader;

namespace ArchitectsStencilMod
{
    /// <summary>
    /// Root mod class for The Architect's Stencil.
    /// This is intentionally minimal; core systems live under the src/Core namespace.
    /// </summary>
    public sealed class ArchitectsStencilMod : Mod
    {
        public const string ModInternalName = "ArchitectsStencilMod";

        public override void Load()
        {
            ASMLog.Initialize(this);
        }

        public override void Unload()
        {
            ASMLog.Unload();
        }
    }
}
