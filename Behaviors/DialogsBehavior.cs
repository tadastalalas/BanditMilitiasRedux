using System;
using BanditMilitiasRedux.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.SceneInformationPopupTypes;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Behaviors;

// BMR-FEATURE: Militia leader talk; keep / release / execute a captured leader | gate: always
public sealed class DialogsBehavior : CampaignBehaviorBase
{
    private const int Priority = 200;

    // Our dialog line ids and tokens.
    private const string AskWhoAttackerId = "bmr_ask_who_atk";
    private const string AskWhoDefenderId = "bmr_ask_who_def";
    private const string WhoAnswerToken = "bmr_who_answer";
    private const string WhoAnswerAttackerId = "bmr_who_answer_atk";
    private const string WhoAnswerDefenderId = "bmr_who_answer_def";
    private const string CaptiveIntroId = "bmr_captive_intro";
    private const string CaptiveChoiceToken = "bmr_captive_choice";
    private const string CaptiveKeepId = "bmr_captive_keep";
    private const string CaptiveReleaseId = "bmr_captive_release";
    private const string CaptiveExecuteId = "bmr_captive_execute";
    private const string CaptiveExecuteAnswerId = "bmr_captive_execute_answer";
    private const string CaptiveExecuteConfirmId = "bmr_captive_execute_confirm";
    private const string CaptiveExecuteCancelId = "bmr_captive_execute_cancel";
    private const string PrisonerPretalkId = "bmr_prisoner_pretalk";
    private const string PrisonerExecuteId = "bmr_prisoner_execute";
    private const string PrisonerExecuteCancelId = "bmr_prisoner_execute_cancel";

    // Vanilla tokens.
    private const string BanditAttackerToken = "bandit_attacker";
    private const string BanditDefenderToken = "bandit_defender";
    private const string StartToken = "start";
    private const string CloseWindowToken = "close_window";
    private const string LordPretalkToken = "lord_pretalk";
    private const string PrisonerRecruitStartPlayerToken = "prisoner_recruit_start_player";

    private const string LeaderNameVariable = "LEADER_NAME";

    public override void RegisterEvents() => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);

    public override void SyncData(IDataStore dataStore) { }

    private static void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(AskWhoAttackerId, BanditAttackerToken, WhoAnswerToken, Texts.Ids.Who,
            bmr_ask_who_on_condition, null, Priority);

        starter.AddPlayerLine(AskWhoDefenderId, BanditDefenderToken, WhoAnswerToken, Texts.Ids.Who,
            bmr_ask_who_on_condition, null, Priority);

        starter.AddDialogLine(WhoAnswerAttackerId, WhoAnswerToken, BanditAttackerToken, Texts.Ids.WhoAnswer,
            bmr_who_answer_atk_on_condition, null, Priority);

        starter.AddDialogLine(WhoAnswerDefenderId, WhoAnswerToken, BanditDefenderToken, Texts.Ids.WhoAnswer,
            bmr_who_answer_def_on_condition, null, Priority);

        starter.AddDialogLine(CaptiveIntroId, StartToken, CaptiveChoiceToken, Texts.Ids.CaptiveIntro,
            bmr_captive_intro_on_condition, null, Priority);

        starter.AddPlayerLine(CaptiveKeepId, CaptiveChoiceToken, CloseWindowToken, Texts.Ids.CaptiveKeep,
            null, bmr_captive_keep_on_consequence, Priority);

        starter.AddPlayerLine(CaptiveReleaseId, CaptiveChoiceToken, CloseWindowToken, Texts.Ids.CaptiveRelease,
            null, bmr_captive_release_on_consequence, Priority);

        starter.AddPlayerLine(CaptiveExecuteId, CaptiveChoiceToken, CaptiveExecuteAnswerId, Texts.Ids.CaptiveExecute,
            null, null, Priority);

        starter.AddDialogLine(CaptiveExecuteAnswerId, CaptiveExecuteAnswerId, CaptiveExecuteConfirmId, Texts.Ids.CaptiveExecuteAnswer,
            null, null, Priority);

        starter.AddPlayerLine(CaptiveExecuteConfirmId, CaptiveExecuteConfirmId, CloseWindowToken, Texts.Ids.CaptiveExecuteConfirm,
            null, bmr_captive_execute_confirm_on_consequence, Priority);

        starter.AddPlayerLine(CaptiveExecuteCancelId, CaptiveExecuteConfirmId, CloseWindowToken, Texts.Ids.CaptiveExecuteCancel,
            bmr_captive_execute_cancel_on_condition, bmr_captive_keep_on_consequence, Priority);

        starter.AddDialogLine(PrisonerPretalkId, LordPretalkToken, PrisonerRecruitStartPlayerToken, Texts.Ids.PrisonerPretalk,
            bmr_prisoner_pretalk_on_condition, null, Priority);

        starter.AddPlayerLine(PrisonerExecuteId, PrisonerRecruitStartPlayerToken, CaptiveExecuteAnswerId, Texts.Ids.CaptiveExecute,
            bmr_prisoner_execute_on_condition, null, Priority);

        starter.AddPlayerLine(PrisonerExecuteCancelId, CaptiveExecuteConfirmId, PrisonerRecruitStartPlayerToken, Texts.Ids.PrisonerExecuteCancel,
            bmr_prisoner_execute_cancel_on_condition, null, Priority + 1);
    }

    private static bool bmr_ask_who_on_condition() => Hero.OneToOneConversationHero?.IsBanditMilitiaHero() == true;

    private static bool bmr_who_answer_atk_on_condition() => SetLeaderNameVariable() && IsPlayerAttacker();

    private static bool bmr_who_answer_def_on_condition() => SetLeaderNameVariable() && !IsPlayerAttacker();

    private static bool bmr_captive_intro_on_condition()
    {
        if (!IsCaptureContext())
            return false;

        Hero? hero = Hero.OneToOneConversationHero;
        return hero is not null && (hero.IsBanditMilitiaHero() || hero.Occupation == Occupation.Bandit);
    }

    private static void bmr_captive_keep_on_consequence()
    {
        Hero? hero = Hero.OneToOneConversationHero;
        if (hero is null)
            return;

        if (hero.PartyBelongedToAsPrisoner != PartyBase.MainParty)
            TakePrisonerAction.Apply(PartyBase.MainParty, hero);

        hero.IsKnownToPlayer = true;
    }

    private static void bmr_captive_release_on_consequence()
    {
        Hero? hero = Hero.OneToOneConversationHero;
        if (hero is not { IsAlive: true })
            return;

        if (hero.IsPrisoner)
            EndCaptivityAction.ApplyByReleasedAfterBattle(hero);
        else
            ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(hero);
    }

    private static void bmr_captive_execute_confirm_on_consequence()
    {
        Hero? hero = Hero.OneToOneConversationHero;
        if (hero is not { IsAlive: true })
            return;

        Action executeHero = () => KillCharacterAction.ApplyByExecution(hero, Hero.MainHero, true, isForced: true);
#if LOWER_THAN_1_5
        MBInformationManager.ShowSceneNotification(
            HeroExecutionSceneNotificationData.CreateForInformingPlayer(
                Hero.MainHero, hero, SceneNotificationData.RelevantContextType.Any, executeHero));
#else
        // v1.5.7: CreateForInformingPlayer takes the execution date as a new third parameter.
        MBInformationManager.ShowSceneNotification(
            HeroExecutionSceneNotificationData.CreateForInformingPlayer(
                Hero.MainHero, hero, CampaignTime.Now, SceneNotificationData.RelevantContextType.Any, executeHero));
#endif
    }

    private static bool bmr_prisoner_pretalk_on_condition() => Hero.OneToOneConversationHero is { IsPrisoner: true } hero && hero.IsBanditMilitiaHero();

    private static bool bmr_prisoner_execute_on_condition()
        => Hero.OneToOneConversationHero is { IsPrisoner: true, IsAlive: true } hero
           && hero.IsBanditMilitiaHero()
           && hero.PartyBelongedToAsPrisoner == PartyBase.MainParty;

    private static bool bmr_captive_execute_cancel_on_condition() => IsCaptureContext();

    private static bool bmr_prisoner_execute_cancel_on_condition() => !IsCaptureContext() && bmr_prisoner_execute_on_condition();

    private static bool IsPlayerAttacker() => PlayerEncounter.Current is not null && PlayerEncounter.PlayerIsAttacker;

    private static bool SetLeaderNameVariable()
    {
        Hero? hero = Hero.OneToOneConversationHero;
        if (hero is not null)
            MBTextManager.SetTextVariable(LeaderNameVariable, hero.Name.ToString());
        return true;
    }

    private static bool IsCaptureContext()
        => Campaign.Current?.CurrentConversationContext is ConversationContext.FreeOrCapturePrisonerHero
            or ConversationContext.CapturedLord;
}
