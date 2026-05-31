using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace TheArchitectsBlueprintMod.UI.TmodGUI.Elements
{
    /// <summary>Vanilla UI texture path lookup (<c>Images/UI/...</c>).</summary>
    internal static class TmodUIAssets
    {
        private const string UiRoot = "Images/UI/";

        public static Asset<Texture2D> RequestTexture(string texturePath) =>
            Main.Assets.Request<Texture2D>(texturePath);

        /// <summary>Shared chrome under <c>Images/UI/</c>.</summary>
        public static class Shared
        {
            public const string ButtonDeletePath = UiRoot + "ButtonDelete";
            public const string ButtonRenamePath = UiRoot + "ButtonRename";
            public const string DividerPath = UiRoot + "Divider";
            public const string ScrollbarPath = UiRoot + "Scrollbar";
            public const string ScrollbarInnerPath = UiRoot + "ScrollbarInner";
            public const string SearchCancelPath = UiRoot + "SearchCancel";
            public const string SettingsTogglePath = UiRoot + "Settings_Toggle";
        }

        /// <summary>Character creation UI under <c>Images/UI/CharCreation/</c>.</summary>
        public static class CharCreation
        {
            public const string CopyButtonPath = UiRoot + "CharCreation/Copy";
        }

        /// <summary>Bestiary UI under <c>Images/UI/Bestiary/</c>.</summary>
        public static class Bestiary
        {
            private const string Root = UiRoot + "Bestiary/";

            public const string BiomeHallowPath = Root + "Biome_Hallow";

            public const string ButtonBackPath = Root + "Button_Back";
            public const string ButtonBorderPath = Root + "Button_Border";
            public const string ButtonFilteringPath = Root + "Button_Filtering";
            public const string ButtonForwardPath = Root + "Button_Forward";
            public const string ButtonSearchPath = Root + "Button_Search";
            public const string ButtonSearch2Path = Root + "Button_Search_2";
            public const string ButtonSearchBorderPath = Root + "Button_Search_Border";
            public const string ButtonSortingPath = Root + "Button_Sorting";
            public const string ButtonWideBorderPath = Root + "Button_Wide_Border";

            public const string IconLockedPath = Root + "Icon_Locked";
            public const string IconRankDimPath = Root + "Icon_Rank_Dim";
            public const string IconRankLightPath = Root + "Icon_Rank_Light";
            public const string IconTagsShadowPath = Root + "Icon_Tags_Shadow";

            public const string PortraitFrontPath = Root + "Portrait_Front";
            public const string SlotBackPath = Root + "Slot_Back";
            public const string SlotFrontPath = Root + "Slot_Front";
            public const string SlotOverlayPath = Root + "Slot_Overlay";
            public const string SlotSelectionPath = Root + "Slot_Selection";

            public const string StatAttackPath = Root + "Stat_Attack";
            public const string StatCopperPath = Root + "Stat_Copper";
            public const string StatDefensePath = Root + "Stat_Defense";
            public const string StatGoldPath = Root + "Stat_Gold";
            public const string StatHpPath = Root + "Stat_HP";
            public const string StatKnockbackPath = Root + "Stat_Knockback";
            public const string StatPanelPath = Root + "Stat_Panel";
            public const string StatPlatinumPath = Root + "Stat_Platinum";
            public const string StatSilverPath = Root + "Stat_Silver";

            public const string NpcPortraitTestPath = Root + "NPCs/Test";

            public static string NpcPortraitPath(int npcId) => Root + "NPCs/NPC_" + npcId;

            public static Texture2D DefaultEntryIcon =>
                TmodUIAssets.RequestTexture(IconLockedPath).Value;
        }
    }
}
