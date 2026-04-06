using System.Reflection.Emit;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.Tracker;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using static BanditMilitias.Helper;

namespace BanditMilitias.Patches
{
    public static class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    Nuke();
            }

            public static void Postfix()
            {
                InitMap();
                RemoveBadItems();
            }
        }

        [HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
        public static class MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        // ServeAsSoldier issue where the MobileParty isn't a quest party
        internal static void PatchSaSDeserters(ref MobileParty __result)
        {
            Traverse.Create(__result).Field<bool>("IsCurrentlyUsedByAQuest").Value = true;
        }
    }
}