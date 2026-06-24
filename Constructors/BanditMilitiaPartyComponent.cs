using System.Collections.Generic;
using BanditMilitiasRedux.Behaviours;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BanditMilitiasRedux.Globals;
using static BanditMilitiasRedux.Helpers.Helper;

namespace BanditMilitiasRedux.Constructors
{
    public class BanditMilitiaPartyComponent : WarPartyComponent
    {
        [CachedData] private TextObject? _cachedName;

        [SaveableField(1)] private Hero _owner;
        [SaveableField(2)] private Hero _leader;
        [SaveableField(3)] private readonly Settlement _homeSettlement;
        [SaveableField(4)] private readonly Banner? _banner;
        [SaveableField(5)] private readonly string _bannerKey;
        [SaveableField(6)] public CampaignTime LastMergedOrSplitDate = CampaignTime.Now;
        [SaveableField(7)] private readonly Dictionary<Hero, float> _avoidance;

        public override TextObject Name => _cachedName ?? CreateCachedName();
        private Hero Owner { get => _owner; set => _owner = value; }
        public override Hero PartyOwner => Owner;
        public override Hero Leader => _leader;
        public override Settlement HomeSettlement => _homeSettlement;
        public new Banner? Banner => _banner;
        public string BannerKey => _bannerKey;
        public Dictionary<Hero, float> Avoidance => _avoidance;
        
        protected override void OnMobilePartySetOnCreation()
        {
            MobileParty.ActualClan = Owner.Clan;
            MobileParty.Party.SetVisualAsDirty();
            MobileParty.AddElementToMemberRoster(Owner.CharacterObject, 1, true);
            MobileParty.ItemRoster.Add(new ItemRosterElement(DefaultItems.Grain, MBRandom.RandomInt(15, 30)));
            MobileParty.DesiredAiNavigationType = MobileParty.NavigationType.Default;
        }

        private void ChangePartyOwner(Hero owner)
        {
            ClearCachedName();
            Owner = owner;
        }
        
        protected override void OnChangePartyLeader(Hero newLeader)
        {
            _leader = newLeader;
            ClearCachedName();
            ChangePartyOwner(newLeader);
        }

        public override void ClearCachedName() => _cachedName = null;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            IsBandit(MobileParty) = true;
        }

        public BanditMilitiaPartyComponent(Hero ownerHero, Settlement homeSettlement, Dictionary<Hero, float> avoidance)
        {
            _owner = ownerHero;
            _leader = ownerHero;
            _homeSettlement = homeSettlement;
            _banner = Banners.Count > 0 ? Banners.GetRandomElement() : null;
            _bannerKey = Banner?.Serialize() ?? string.Empty;
            _avoidance = avoidance;
            _cachedName = CreateCachedName();
        }

        private TextObject CreateCachedName()
        {
            if (_leader is null)
                return new TextObject(Settings.LeaderlessBanditMilitiaString);
            
            int tier = NotorietyBehavior.Instance?.GetTier(Leader) ?? 0;
            string? epithet = NotorietyBehavior.GetEpithet(tier);
            object leaderName = epithet is null
                ? _leader.FirstName
                : new TextObject("{=BMRLeaderEpithet}{FIRST} {EPITHET}", new Dictionary<string, object>
                { ["FIRST"] = _leader.FirstName, ["EPITHET"] = epithet });

            return new TextObject("{=BMPartyName}{LEADER_NAME}'s {PARTY_NAME}", new Dictionary<string, object>
            { ["IS_BANDIT"] = 1, ["LEADER_NAME"] = leaderName, ["PARTY_NAME"] = Settings.BanditMilitiaString });
        }
    }
}