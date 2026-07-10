using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Content.Items;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Maps architect tool item types to overlay scopes and resolves whether a registered overlay family should draw.
    /// Visibility is evaluated from the held item each frame via <see cref="ArchitectOverlayPlayer"/>.
    /// </summary>
    public sealed class ArchitectOverlayVisibility : ModSystem
    {
        private static readonly Dictionary<int, ArchitectOverlayScope> ItemScopes = new();

        /// <summary>Registers overlay scopes for a mod item type.</summary>
        internal static void RegisterItem(int itemType, ArchitectOverlayScope scope) => ItemScopes[itemType] = scope;

        /// <summary>Returns overlay scopes for <paramref name="itemType"/>, or <see cref="ArchitectOverlayScope.None"/>.</summary>
        public static ArchitectOverlayScope GetItemScope(int itemType) =>
            ItemScopes.TryGetValue(itemType, out ArchitectOverlayScope scope) ? scope : ArchitectOverlayScope.None;

        /// <summary>Returns true when any required scope bit is set on the held tool scopes.</summary>
        public static bool IsFamilyVisible(string? family, ArchitectOverlayScope heldScope)
        {
            if (heldScope == ArchitectOverlayScope.None)
                return false;

            ArchitectOverlayScope required = GetRequiredScope(family);
            if (required == ArchitectOverlayScope.None)
                return false;

            return (heldScope & required) != 0;
        }

        public override void OnModLoad()
        {
            var selection = ArchitectOverlayScope.Selection;
            var inclusion = ArchitectOverlayScope.Selection | ArchitectOverlayScope.Projection | ArchitectOverlayScope.Inclusion;
            var projection = ArchitectOverlayScope.Projection;
            var projectionPaste = ArchitectOverlayScope.Projection | ArchitectOverlayScope.Paste;

            Register<PinStick>(selection);
            Register<SelectionWand>(selection);
            Register<IncludeStick>(inclusion);
            Register<ExcludeStick>(inclusion);
            Register<InclusionWand>(inclusion);
            Register<CopyStick>(projection);
            Register<ProjectionWand>(projectionPaste);
            Register<PasteStick>(projectionPaste);
            Register<ExecutionWand>(projectionPaste);
            Register<ArchitectsStencil>(ArchitectOverlayScope.All);
            Register<DebugStick>(ArchitectOverlayScope.All);
        }

        private static void Register<T>(ArchitectOverlayScope scope) where T : ModItem =>
            RegisterItem(ModContent.ItemType<T>(), scope);

        private static ArchitectOverlayScope GetRequiredScope(string? family)
        {
            if (string.IsNullOrEmpty(family))
                return ArchitectOverlayScope.None;

            if (family.StartsWith("paste", StringComparison.Ordinal))
                return ArchitectOverlayScope.Paste;

            if (family.StartsWith("selection:", StringComparison.Ordinal))
                return ArchitectOverlayScope.Selection;

            if (family.StartsWith("inclusion:", StringComparison.Ordinal))
                return ArchitectOverlayScope.Inclusion;

            if (string.Equals(family, "projection:excluded-tiles", StringComparison.Ordinal))
                return ArchitectOverlayScope.Projection | ArchitectOverlayScope.Inclusion;

            if (family.StartsWith("projection:", StringComparison.Ordinal)
                || family.StartsWith("inventory:", StringComparison.Ordinal))
                return ArchitectOverlayScope.Projection;

            return ArchitectOverlayScope.None;
        }
    }
}
