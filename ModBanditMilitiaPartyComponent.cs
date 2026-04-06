using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BanditMilitias.Globals;
using static BanditMilitias.Helper;

namespace BanditMilitias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<ModBanditMilitiaPartyComponent>();

        [SaveableField(1)] public readonly Banner Banner;
        [SaveableField(2)] public readonly string BannerKey;
        [SaveableField(3)] public CampaignTime LastMergedOrSplitDate = CampaignTime.Now;
        [SaveableField(4)] public Dictionary<Hero, float> Avoidance = new();
        [SaveableField(5)] private Hero leader;
        [SaveableField(6)] private Settlement homeSettlement;
        [SaveableField(7)] public CampaignVec2 NavalPatrolPosition;
        [CachedData] private TextObject cachedName;

        public override Settlement HomeSettlement => homeSettlement;
        public override Hero Leader => leader;
        public override Hero PartyOwner => MobileParty?.ActualClan?.Leader; // clan is null during nuke  
        private static readonly MethodInfo OnWarPartyRemoved = AccessTools.Method(typeof(Clan), "OnWarPartyRemoved");

        public override TextObject Name
        {
            get
            {
                cachedName ??= CreateCachedName();
                return cachedName;
            }
        }

        public void ChangePartyLeader(Hero newLeader)
        {
            base.ChangePartyLeader(newLeader);

            if (newLeader is null)
            {
                if (!Heroes.Contains(leader))
                {
                    ForceRemoveLeader();
                }
            }
            else
            {
                if (leader is not null)
                {
                    Logger.LogDebug($"{newLeader.Name} is taking over {MobileParty.Name}({MobileParty.StringId}) from {leader.Name}[{leader.HeroState}].");
                }
                leader = newLeader;
                newLeader.Clan = MobileParty.ActualClan;
                ClearCachedName();
            }
        }

        public void ForceRemoveLeader()
        {
            if (leader is null) return;
            leader = null;
            ClearCachedName();
        }

        private readonly Clan _targetClan;

        protected override void OnMobilePartySetOnCreation()
        {
            // ActualClan must be set here — before OnInitialize fires —
            // because base.OnInitialize() calls this.Clan.OnWarPartyAdded(this)
            // which reads MobileParty.ActualClan. If null it crashes.
            MobileParty.ActualClan = _targetClan;
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            if (!IsBandit(MobileParty))
                IsBandit(MobileParty) = true;
            OnWarPartyRemoved.Invoke(Clan, [this]);
        }

        public ModBanditMilitiaPartyComponent(Settlement settlement, Hero hero, Clan clan = null)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();

            _targetClan = clan ?? settlement.OwnerClan;

            hero ??= CreateHero(settlement);

            _bornSettlement(hero) = settlement;

            // Assign the bandit clan to the hero. Verified safe from KillCharacterAction source:
            // - IsBanditFaction guard prevents DestroyClanAction from firing
            // - GiveGoldAction targets the bandit faction dummy leader — harmless void
            // - No kingdom succession logic applies to bandit faction heroes
            hero.Clan = _targetClan;
            //hero.UpdateHomeSettlement();
            HiddenInEncyclopedia(hero.CharacterObject) = true;
            homeSettlement = settlement;
            leader = hero;
        }

        internal TextObject CreateCachedName()
        {
            if (leader is null)
            {
                return new TextObject(Globals.Settings.LeaderlessBanditMilitiaString, new Dictionary<string, object>
                {
                    ["IS_BANDIT"] = 1
                });
            }

            return new TextObject("{=BMPartyName}{LEADER_NAME}'s {PARTY_NAME}", new Dictionary<string, object>
            {
                ["IS_BANDIT"] = 1,
                ["LEADER_NAME"] = leader.FirstName,
                ["PARTY_NAME"] = Globals.Settings.BanditMilitiaString
            });
        }

        public override void ClearCachedName()
        {
            cachedName = null;
        }

        protected override void OnFinalize()
        {
            if (MobileParty is not null) PartyImageMap.Remove(MobileParty);
            leader = null;

            base.OnFinalize();
        }
    }
}
