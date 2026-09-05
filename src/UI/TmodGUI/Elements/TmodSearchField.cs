using Terraria.Localization;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Helpers for vanilla <see cref="Terraria.GameContent.UI.Elements.UISearchBar"/> placeholder text.</summary>
    internal static class TmodSearchField
    {
        public static string NormalizeContents(string? text, LocalizedText placeholder)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string hint = placeholder.Value;
            if (!string.IsNullOrEmpty(hint) && text == hint)
                return string.Empty;

            return text;
        }
    }
}
