using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace TheArchitectsBlueprintMod.Core.Compat.Adapters
{
    /// <summary>Modular copy/sanitize/paste hooks for payload entity compatibility.</summary>
    internal interface ICompatibilityAdapter
    {
        string Id { get; }

        int Priority { get; }

        /// <summary>Returns whether this adapter may run for the current session/document mods.</summary>
        bool IsAvailable(CompatibilityContext ctx);

        /// <summary>Returns whether this adapter claims the entity FullName/phase in <paramref name="ctx"/>.</summary>
        bool Handles(CompatibilityContext ctx);

        /// <summary>Attempts copy; return false to defer to a lower-priority adapter.</summary>
        bool TryCopy(CompatibilityContext ctx, TileEntity te, out TagCompound? data);

        /// <summary>Attempts sanitize/storage rewrite; return false to defer.</summary>
        bool TrySanitize(CompatibilityContext ctx, TagCompound data, out TagCompound? cleaned);

        /// <summary>Attempts paste/apply; return false to defer, true when handled (see <paramref name="applied"/>).</summary>
        bool TryPaste(CompatibilityContext ctx, int worldX, int worldY, TagCompound data, out bool applied);
    }
}
