using System;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Diagnostics;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Relation changes with broken militia heroes are skipped; their exceptions are swallowed and logged | gate: always | Prefix + Finalizer: Prefix skips vanilla for unusable heroes, Finalizer swallows exceptions that involve militia heroes
[HarmonyPatch(typeof(ChangeRelationAction), "ApplyInternal")]
internal class ChangeRelationActionPatch
{
    internal static bool Prefix(Hero originalHero, Hero originalGainedRelationWith)
    {
        bool militiaInvolved = originalHero?.IsBanditMilitiaHero() == true || originalGainedRelationWith?.IsBanditMilitiaHero() == true;

        if (!militiaInvolved)
            return true;

        return IsUsable(originalHero) && IsUsable(originalGainedRelationWith);
    }

    internal static Exception? Finalizer(Hero originalHero, Hero originalGainedRelationWith, Exception __exception)
    {
        if (__exception is null)
            return null;

        bool militiaInvolved = originalHero?.IsBanditMilitiaHero() == true || originalGainedRelationWith?.IsBanditMilitiaHero() == true;
        if (!militiaInvolved)
            return __exception;

        BmrLog.Error($"BMR-RELATION-EXCEPTION: {originalHero?.StringId} <-> {originalGainedRelationWith?.StringId}", __exception);
        return null;
    }

    private static bool IsUsable(Hero? hero)
        => hero is not null
           && hero.CharacterObject is not null
           && hero.CharacterObject.Id.InternalValue != 0;
}
