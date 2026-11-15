using BanditMilitiasRedux.Core;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Behaviors;

// BMR-FEATURE: Defection, a militia hero who leaves his bandit clan stops being a militia hero | gate: always
public sealed class HeroDefectionBehavior : CampaignBehaviorBase
{
    private const string LeaderTag = "LEADER";

    public override void RegisterEvents()
        => CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);

    public override void SyncData(IDataStore dataStore) { }

    private static void OnHeroChangedClan(Hero? hero, Clan? oldClan)
    {
        if (hero is null || !hero.IsBanditMilitiaHero() || hero.Clan?.IsBanditFaction == true)
            return;

        AllAliveBanditMilitiaHeroes.Remove(hero);
        ReusableHeroesBehavior.RemoveHeroFromTheWaitingDictionary(hero, oldClan);

        // Only fill the gap if the recruiting mod left him a Bandit; never override Lord etc.
        if (hero.Occupation == Occupation.Bandit)
            hero.SetNewOccupation(Occupation.Wanderer);

        hero.EncyclopediaText = BuildDefectionText(hero, oldClan);
    }

    private static TextObject BuildDefectionText(Hero hero, Clan? oldClan)
    {
        TextObject text = Texts.EncyclopediaDefected(oldClan?.Name ?? Texts.EncyclopediaNoClan);
        StringHelpers.SetCharacterProperties(LeaderTag, hero.CharacterObject, text, false);
        return text;
    }
}
