using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using BanditMilitiasRedux.Behaviours;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Helpers
{
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
                return reusedBanditHero;
            
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
                select characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10 into freq select freq > 0 ? freq : 100).Sum();

            int remaining = bornSettlement.RandomInt(1, maxWeight + 1);

            foreach (var characterObject in HeroTemplates)
            {
                if (!IsUsableCulture(characterObject))
                    continue;
                
                int freq = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                remaining -= freq > 0 ? freq : 100;
                
                if (remaining < 0)
                    return characterObject;
            }
            return HeroTemplates.FirstOrDefault(IsUsableCulture)
                   ?? HeroTemplates.FirstOrDefault(c => c.Culture is not null);

            bool IsUsableCulture(CharacterObject characterObject)
                => (characterObject.Culture is not null) && characterObject.IsFemale == isFemale;
        }

        private static bool PickGender() => MBRandom.RandomInt(0, 100) < Settings.FemaleSpawnChance;

        private static void GenerateBanditHeroName(Hero newBanditHero)
        {
            const int maxAttempts = 200;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                NameGenerator.Current.GenerateHeroNameAndHeroFullName(newBanditHero, out var generatedFirstName, out var generatedFullName, useDeterministicValues: false);
                if (attempt < maxAttempts - 1 && IsNameTaken(generatedFirstName, generatedFullName))
                    continue;
                newBanditHero.SetName(generatedFullName, generatedFirstName);
                return;
            }
        }

        private static bool IsNameTaken(TextObject firstName, TextObject fullName)
        {
            var firstCandidate = firstName.ToString();
            var fullCandidate  = fullName.ToString();
            if (string.IsNullOrEmpty(firstCandidate) && string.IsNullOrEmpty(fullCandidate))
                return true;

            foreach (var hero in AllAliveBanditMilitiaHeroes)
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
            newBanditHero.AddPower(MBRandom.RandomFloat * 20f); // Slight power variation. Min 0, Max 20 power to be added on creation.
            if (!Settings.CanTrain)
                return;
            
            newBanditHero.HeroDeveloper.AddPerk(DefaultPerks.Leadership.VeteransRespect);
            newBanditHero.HeroDeveloper.AddSkillXp(DefaultSkills.Leadership, 150);
        }
    }
}