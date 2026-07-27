using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.SceneInformationPopupTypes;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Behaviours
{
    public sealed class DialogsBehavior : CampaignBehaviorBase
    {
        private const int Priority = 200;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore) { }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine(
                "bmr_ask_who_atk",
                "bandit_attacker",
                "bmr_who_answer",
                "{=BMRWho}Who are you, and who do you ride for?",
                bmr_leader_condition, null, Priority);

            starter.AddPlayerLine(
                "bmr_ask_who_def",
                "bandit_defender",
                "bmr_who_answer",
                "{=BMRWho}Who are you, and who do you ride for?",
                bmr_leader_condition, null, Priority);

            starter.AddDialogLine(
                "bmr_who_answer_atk",
                "bmr_who_answer",
                "bandit_attacker",
                "{=BMRWhoAnswer}They call me {LEADER_NAME}. We answer to no lord and no king — only to coin and the open road.",
                bmr_who_answer_atk_condition, null, Priority);

            starter.AddDialogLine(
                "bmr_who_answer_def",
                "bmr_who_answer",
                "bandit_defender",
                "{=BMRWhoAnswer}They call me {LEADER_NAME}. We answer to no lord and no king — only to coin and the open road.",
                bmr_who_answer_def_condition, null, Priority);
            
            starter.AddDialogLine("bmr_captive_intro", "start", "bmr_captive_choice",
                "{=BMRCaptiveIntro}So it ends. Do what you will with me.",
                bmr_captive_condition, null, Priority);

            starter.AddPlayerLine("bmr_captive_keep", "bmr_captive_choice", "close_window",
                "{=BMRCaptiveKeep}You are my prisoner now.",
                null, bmr_captive_keep_consequence, Priority);

            starter.AddPlayerLine("bmr_captive_release", "bmr_captive_choice", "close_window",
                "{=BMRCaptiveRelease}Get out of my sight.",
                null, bmr_captive_release_consequence, Priority);
            
            starter.AddPlayerLine("bmr_captive_execute", "bmr_captive_choice", "bmr_captive_execute_answer",
                "{=BMRCaptiveExecute}Do not expect mercy. Off with your head!",
                null, null, Priority);

            starter.AddDialogLine("bmr_captive_execute_answer", "bmr_captive_execute_answer", "bmr_captive_execute_confirm",
                "{=BMRCaptiveExecuteAnswer}Then be quick about it. My lads will remember your face — and they'll come looking for you.",
                null, null, Priority);

            starter.AddPlayerLine("bmr_captive_execute_confirm", "bmr_captive_execute_confirm", "close_window",
                "{=BMRCaptiveExecuteConfirm}I care not. Prepare to die!",
                null, bmr_captive_execute_consequence, Priority);

            starter.AddPlayerLine("bmr_captive_execute_cancel", "bmr_captive_execute_confirm", "close_window",
                "{=BMRCaptiveExecuteCancel}Fine then. You are my prisoner now.",
                null, bmr_captive_keep_consequence, Priority);
        }

        private static bool bmr_leader_condition()
            => Hero.OneToOneConversationHero?.IsBanditMilitiaHero() == true;

        private static bool bmr_player_is_attacker()
            => PlayerEncounter.Current is not null && PlayerEncounter.PlayerIsAttacker;
        
        private static bool bmr_who_answer_atk_condition()
            => bmr_set_leader_name() && bmr_player_is_attacker();

        private static bool bmr_who_answer_def_condition()
            => bmr_set_leader_name() && !bmr_player_is_attacker();

        private static bool bmr_set_leader_name()
        {
            var hero = Hero.OneToOneConversationHero;
            if (hero is not null)
                MBTextManager.SetTextVariable("LEADER_NAME", hero.Name.ToString());
            return true;
        }
        
        private static bool bmr_captive_condition()
        {
            if (Campaign.Current?.CurrentConversationContext is not (ConversationContext.FreeOrCapturePrisonerHero
                or ConversationContext.CapturedLord))
                return false;

            Hero? hero = Hero.OneToOneConversationHero;
            return hero is not null && (hero.IsBanditMilitiaHero() || hero.Occupation == Occupation.Bandit);
        }

        private static void bmr_captive_keep_consequence()
        {
            Hero? hero = Hero.OneToOneConversationHero;
            if (hero is null)
                return;

            if (hero.PartyBelongedToAsPrisoner != PartyBase.MainParty)
                TakePrisonerAction.Apply(PartyBase.MainParty, hero);

            hero.IsKnownToPlayer = true;
        }

        private static void bmr_captive_release_consequence()
        {
            Hero? hero = Hero.OneToOneConversationHero;
            if (hero is { IsPrisoner: true })
                EndCaptivityAction.ApplyByReleasedAfterBattle(hero);
        }
        
        private static void bmr_captive_execute_consequence()
        {
            Hero? hero = Hero.OneToOneConversationHero;
            if (hero is not { IsAlive: true })
                return;

            MBInformationManager.ShowSceneNotification(
                HeroExecutionSceneNotificationData.CreateForInformingPlayer(
                    Hero.MainHero, hero, SceneNotificationData.RelevantContextType.Any,
                    () => KillCharacterAction.ApplyByExecution(hero, Hero.MainHero, true, isForced: true)));
        }
    }
}