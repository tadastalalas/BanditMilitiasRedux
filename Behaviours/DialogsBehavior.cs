using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
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
    }
}