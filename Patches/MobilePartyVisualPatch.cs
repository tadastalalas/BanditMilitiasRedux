using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;
using HarmonyLib;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Diagnostics, logs party icons whose mount has no monster | gate: setting:LoggingEnabled | Prefix: must read the character before vanilla builds the mount visual
[HarmonyPatch(typeof(MobilePartyVisual), "AddCharacterToPartyIcon")]
internal class MobilePartyVisualAddCharacterToPartyIconPatch
{
    internal static void Prefix(PartyBase party, CharacterObject characterObject)
    {
        if (!BmrLog.IsDebugEnabled || characterObject is null || !characterObject.HasMount())
            return;

        EquipmentElement mount = characterObject.Equipment[EquipmentIndex.Horse];
        if (mount.Item?.HorseComponent?.Monster is not null)
            return;

        BmrLog.Debug("BMR-MOUNT-NULLCHECK: " +
            $"party={party?.Name} char={characterObject.StringId} isHero={characterObject.IsHero} " +
            $"hero={characterObject.HeroObject?.Name} isOurs={characterObject.IsBanditMilitiaCharacterObject()} " +
            $"mountItem={mount.Item?.StringId ?? "null"}");
    }
}

#if LOWER_THAN_1_5
// v1.4.7 / v1.4.8 only. v1.5.7 uses MobilePartyVisualHelperPatch (the bannerKey parameter is gone).
// BMR-FEATURE: Militia banners on the map icon | gate: setting:RandomBanners | Prefix: the banner key is an input of the vanilla method
[HarmonyPatch(typeof(MobilePartyVisual), "AddCharacterToPartyIcon")]
internal class MobilePartyVisualAddCharacterToPartyIconBannerPatch
{
    internal static void Prefix(CharacterObject characterObject, ref string bannerKey)
    {
        if (!ModSettings.RandomBanners || characterObject?.HeroObject?.PartyBelongedTo?.IsBanditMilitiaParty() != true)
            return;

        BanditMilitiaPartyComponent? component = characterObject.HeroObject.PartyBelongedTo.PartyComponent as BanditMilitiaPartyComponent;

        if (component?.BannerKey is not null)
            bannerKey = component.BannerKey;
    }
}
#endif
