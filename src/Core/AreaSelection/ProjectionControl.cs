using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using static ArchitectsStencilMod.Core.ToolActionManager;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// Handles interaction with projections: moving, and (later) mirroring, resizing, rotating, save/copy to clipboard.
    /// Driven by tools via <see cref="ToolAction.COPY"/> and <see cref="ToolAction.CUT"/>.
    /// </summary>
    public sealed class ProjectionControl : ModSystem
    {
        /// <summary>
        /// Handles projection input for the given action kind.
        /// </summary>
        public void Handle(ToolActionKey key, Player player, Point cursorTile, ProjectionKind kind)
        {
            System.ArgumentNullException.ThrowIfNull(player);

            var projectionSystem = ModContent.GetInstance<ProjectionSystem>();

            switch (key)
            {
                case ToolActionKey.LeftClick:
                    projectionSystem.DragOrCreate(player, cursorTile, kind);
                    return;

                case ToolActionKey.LeftHold:
                    projectionSystem.DragOrCreate(player, cursorTile, kind);
                    return;

                case ToolActionKey.LeftRelease:
                    projectionSystem.EndDrag(player);
                    return;

                case ToolActionKey.RightClick:
                    if (!projectionSystem.RemoveProjectionAt(player, cursorTile, kind) &&
                        !projectionSystem.TryGetProjectionAt(player, cursorTile, out _))
                    {
                        return;
                    }
                    return;

                default:
                    return;
            }
        }
    }
}
