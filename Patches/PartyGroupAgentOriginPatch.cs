using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia banners in battle | gate: setting:RandomBanners | Postfix: swap the banner vanilla picked
[HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
internal class PartyGroupAgentOriginPatch
{
    // Property-getter patch (hot): plain checks only.
    internal static void Postfix(IAgentOriginBase __instance, ref Banner __result)
    {
        if (__instance?.BattleCombatant is not PartyBase party)
            return;

        if (!ModSettings.RandomBanners || !party.IsMobile || !party.MobileParty.IsBanditMilitiaParty())
            return;

        Banner? militiaBanner = party.MobileParty?.GetBanditMilitiaParty()?.Banner;
        if (militiaBanner is not null)
            __result = militiaBanner;
    }
}
