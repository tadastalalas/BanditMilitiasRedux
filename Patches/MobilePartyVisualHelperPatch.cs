#if !LOWER_THAN_1_5
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;
using HarmonyLib;
using SandBox.View;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BanditMilitiasRedux.Patches;

// v1.5.7 only: replaces MobilePartyVisualAddCharacterToPartyIconBannerPatch (1.4.x), whose bannerKey parameter no longer exists.
// The map icon banner now comes from party.LeaderHero.ClanBanner.BannerCode inside GetHumanAgentPartyVisual.
// BMR-FEATURE: Militia banners on the map icon | gate: setting:RandomBanners | Transpiler: the banner code is a local of the vanilla method, no argument or result to change
[HarmonyPatch(typeof(SandBoxViewHelpers.MobilePartyVisualHelper), "GetHumanAgentPartyVisual")]
internal class MobilePartyVisualHelperPatch
{
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = [.. instructions];
        MethodInfo bannerCodeGetter = AccessTools.PropertyGetter(typeof(Banner), nameof(Banner.BannerCode));
        MethodInfo resolver = AccessTools.Method(typeof(MobilePartyVisualHelperPatch), nameof(ResolveBannerCode));

        for (int i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(bannerCodeGetter))
                continue;

            // Stack: bannerCode -> ResolveBannerCode(bannerCode, party). Argument 2 of the static vanilla method is "party".
            codes.InsertRange(i + 1, [new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Call, resolver)]);
            return codes;
        }

        BmrLog.Error("GetHumanAgentPartyVisual transpiler: Banner.BannerCode not found, militia map icons keep the clan banner.");
        return codes;
    }

    private static string? ResolveBannerCode(string? bannerCode, PartyBase? party)
    {
        if (!ModSettings.RandomBanners || party?.LeaderHero?.PartyBelongedTo?.IsBanditMilitiaParty() != true)
            return bannerCode;

        BanditMilitiaPartyComponent? component = party.LeaderHero.PartyBelongedTo.PartyComponent as BanditMilitiaPartyComponent;
        return component?.BannerKey ?? bannerCode;
    }
}
#endif
