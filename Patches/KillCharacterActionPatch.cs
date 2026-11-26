using System.Reflection;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Diagnostics;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia hero death cleanup; executing a militia leader grants the optional Honor XP | gate: setting:ExecutionHonorXp | Prefix + Postfix: Prefix marks the execution before vanilla applies its Honor penalty, Postfix cleans up after the death
[HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
internal class KillCharacterActionPatch
{
    private static readonly MethodInfo AddPlayerTraitXp = AccessTools.Method(typeof(TraitLevelingHelper), "AddPlayerTraitXPAndLogEntry");

    internal static void Prefix(Hero victim, KillCharacterAction.KillCharacterActionDetail actionDetail)
    {
        if (victim?.IsBanditMilitiaHero() == true
            && actionDetail is KillCharacterAction.KillCharacterActionDetail.Executed
                or KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent)
            SuppressedExecutionVictim = victim;
    }

    internal static void Postfix(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail actionDetail, bool showNotification, bool isForced = false)
    {
        bool wasMilitiaExecution = victim is not null && SuppressedExecutionVictim == victim;
        if (wasMilitiaExecution)
            SuppressedExecutionVictim = null;

        if (victim is null || victim.IsAlive || !AllAliveBanditMilitiaHeroes.Contains(victim))
            return;

        AllAliveBanditMilitiaHeroes.Remove(victim);
        BmrCounters.HeroesKilled++;
        CharacterRelationManager.Instance.RemoveHero(victim);

        int executionHonorXp = ModSettings.ExecutionHonorXp;
        if (wasMilitiaExecution && killer == Hero.MainHero && executionHonorXp > 0)
            AddPlayerTraitXp?.Invoke(null, [DefaultTraits.Honor, executionHonorXp, ActionNotes.SacrificedTroops, victim]);
    }
}
