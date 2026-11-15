using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Militia leaders, reuse a waiting hero or create a new one (cap per clan) | gate: setting:HeroPoolCap, setting:FemaleSpawnChance
[SuppressMessage("Usage", "BHA0001:Member does not exist in Type")] // Because the constructor will be found by Harmony only at runtime.
internal static class BanditHeroCreator
{
    private static readonly AccessTools.FieldRef<Hero, HeroDeveloper> HeroDeveloperFieldRef = AccessTools.FieldRefAccess<Hero, HeroDeveloper>("_heroDeveloper");
    private static readonly AccessTools.FieldRef<Hero, bool> HasMetFieldRef = AccessTools.FieldRefAccess<Hero, bool>("_hasMet");
    private static readonly AccessTools.FieldRef<CharacterObject, bool> HiddenInEncyclopediaFieldRef = AccessTools.FieldRefAccess<CharacterObject, bool>("<HiddenInEncyclopedia>k__BackingField");
    private static readonly ConstructorInfo HeroDeveloperConstructorRef = AccessTools.Constructor(typeof(HeroDeveloper), [typeof(Hero)]);

    internal static Hero? ReuseOrCreateBanditHero(Settlement settlement, Clan targetClan)
    {
        Hero? reusedBanditHero = ReusableHeroesBehavior.Instance?.TryGetReusableBanditHero(targetClan);
        if (reusedBanditHero is not null)
        {
            if (!AllAliveBanditMilitiaHeroes.Contains(reusedBanditHero))
                AllAliveBanditMilitiaHeroes.Add(reusedBanditHero);
            return reusedBanditHero;
        }

        if (Helper.IsHeroCapReached(targetClan))
            return null;

        Hero? newBanditHero = CreateSpecialHeroFromATemplate(settlement, targetClan);

        if (newBanditHero is null)
            return null;

        HeroDeveloperFieldRef(newBanditHero) ??= (HeroDeveloper)HeroDeveloperConstructorRef.Invoke([newBanditHero]);
        HasMetFieldRef(newBanditHero) = false;
        GenerateBanditHeroName(newBanditHero);
        newBanditHero.ChangeState(Hero.CharacterStates.Active);
        HeroAppearanceService.RandomizeAppearance(newBanditHero);
        HiddenInEncyclopediaFieldRef(newBanditHero.CharacterObject) = false;
        newBanditHero.HiddenInEncyclopedia = false;
        NotorietyBehavior.Instance?.EnsureTracked(newBanditHero);
        EquipmentHelper.AssignHeroEquipmentFromEquipment(newBanditHero, EquipmentPool.GetRandomEquipmentSet());
        TrainBanditLeader(newBanditHero);
        AllAliveBanditMilitiaHeroes.Add(newBanditHero);
        return newBanditHero;
    }

    private static Hero? CreateSpecialHeroFromATemplate(Settlement bornSettlement, Clan heroClan)
    {
        CharacterObject? template = SelectWeightedTemplate(bornSettlement);
        return template is null ? null : HeroCreator.CreateSpecialHero(template, bornSettlement, heroClan);
    }

    private static CharacterObject? SelectWeightedTemplate(Settlement bornSettlement)
    {
        bool isFemale = PickGender();
        int maxWeight = (from characterObject in HeroTemplates where IsUsableCulture(characterObject)
            select TemplateWeight(characterObject)).Sum();

        int remaining = bornSettlement.RandomInt(1, maxWeight + 1);

        foreach (CharacterObject characterObject in HeroTemplates)
        {
            if (!IsUsableCulture(characterObject))
                continue;

            remaining -= TemplateWeight(characterObject);

            if (remaining < 0)
                return characterObject;
        }
        return HeroTemplates.FirstOrDefault(IsUsableCulture)
               ?? HeroTemplates.FirstOrDefault(c => c.Culture is not null);

        bool IsUsableCulture(CharacterObject characterObject)
            => (characterObject.Culture is not null) && characterObject.IsFemale == isFemale;
    }

    private static int TemplateWeight(CharacterObject characterObject)
    {
        int frequency = characterObject.GetTraitLevel(DefaultTraits.Frequency) * Balance.TemplateFrequencyWeightMultiplier;
        return frequency > 0 ? frequency : Balance.TemplateDefaultWeight;
    }

    private static bool PickGender() => MBRandom.RandomInt(0, Balance.FullPercent) < ModSettings.FemaleSpawnChance;

    private static void GenerateBanditHeroName(Hero newBanditHero)
    {
        for (int attempt = 0; attempt < Balance.NameGenerationMaxAttempts; attempt++)
        {
            NameGenerator.Current.GenerateHeroNameAndHeroFullName(newBanditHero, out TextObject generatedFirstName, out TextObject generatedFullName, useDeterministicValues: false);
            if (attempt < Balance.NameGenerationMaxAttempts - 1 && IsNameTaken(generatedFirstName, generatedFullName))
                continue;
            newBanditHero.SetName(generatedFullName, generatedFirstName);
            return;
        }
    }

    private static bool IsNameTaken(TextObject firstName, TextObject fullName)
    {
        string firstCandidate = firstName.ToString();
        string fullCandidate = fullName.ToString();
        if (string.IsNullOrEmpty(firstCandidate) && string.IsNullOrEmpty(fullCandidate))
            return true;

        foreach (Hero hero in AllAliveBanditMilitiaHeroes)
        {
            if (!string.IsNullOrEmpty(firstCandidate) && hero.FirstName?.ToString() == firstCandidate)
                return true;
            if (!string.IsNullOrEmpty(fullCandidate) && hero.Name?.ToString() == fullCandidate)
                return true;
        }
        return false;
    }

    private static void TrainBanditLeader(Hero newBanditHero)
    {
        newBanditHero.AddPower(MBRandom.RandomFloat * Balance.LeaderPowerVariationMax);
        if (!ModSettings.CanTrain)
            return;

        newBanditHero.HeroDeveloper.AddPerk(DefaultPerks.Leadership.VeteransRespect);
        newBanditHero.HeroDeveloper.AddSkillXp(DefaultSkills.Leadership, Balance.LeaderStartingLeadershipXp);
    }
}
