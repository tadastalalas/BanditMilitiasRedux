using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static BanditMilitias.Globals;

namespace BanditMilitias.Helpers
{
    internal static class HeroFactory
    {
        internal static readonly AccessTools.FieldRef<Hero, bool> HasMet =
            AccessTools.FieldRefAccess<Hero, bool>("_hasMet");

        internal static readonly AccessTools.FieldRef<Hero, HeroDeveloper> HeroDeveloperField =
            AccessTools.FieldRefAccess<Hero, HeroDeveloper>("_heroDeveloper");

        internal static readonly ConstructorInfo HeroDeveloperConstructor =
            AccessTools.Constructor(typeof(HeroDeveloper), [typeof(Hero)]);

        private static readonly HashSet<CultureObject> _blacklistedCultures = [];

        internal static Hero CreateHero(Settlement settlement)
        {
            var hero = CreateOrReuseHero(settlement);

            if (hero is null)
                return null;

            Heroes.Add(hero);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, EquipmentPool.GetRandomEquipmentSet());

            if (Globals.Settings.CanTrain)
            {
                hero.HeroDeveloper.AddPerk(DefaultPerks.Leadership.VeteransRespect);
                hero.HeroDeveloper.AddSkillXp(DefaultSkills.Leadership, 150);
            }
            return hero;
        }

        internal static Hero CreateOrReuseHero(Settlement settlement)
        {
            if (settlement is null)
                return null;

            var hero = CustomizedCreateHeroAtOccupation(settlement);

            if (hero is null)
                return null;

            HasMet(hero) = false;
            hero.BornSettlement = settlement;

            try
            {
                NameGenerator.Current.GenerateHeroNameAndHeroFullName(
                    hero, out TextObject firstName, out TextObject fullName, false);
                hero.SetName(fullName, firstName);
            }
            catch (Exception)
            {
                if (hero.Culture is not null)
                    _blacklistedCultures.Add(hero.Culture);
            }

            HeroDeveloperField(hero) = HeroDeveloperConstructor.Invoke([hero]) as HeroDeveloper;
            hero.HeroDeveloper.InitializeHeroDeveloper();
            hero.Initialize();
            hero.ChangeState(Hero.CharacterStates.Active);
            hero.EncyclopediaText = TextObject.GetEmpty().CopyTextObject();
            HeroAppearanceService.RandomizeAppearance(hero);
            return hero;
        }

        private static Hero CustomizedCreateHeroAtOccupation(Settlement settlement)
        {
            const int MaxAttempts = 6;

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var template = SelectWeightedTemplate(settlement);
                if (template is null)
                    return null;

                Hero specialHero;
                try
                {
                    specialHero = HeroCreator.CreateSpecialHero(
                        template, settlement, null, null);
                }
                catch (Exception)
                {
                    continue;
                }

                if (specialHero is null)
                    continue;

                specialHero.AddPower(MBRandom.RandomFloat * 20f);
                specialHero.ChangeState(Hero.CharacterStates.Active);
                specialHero.IsFemale = RollFemale();

                return specialHero;
            }
            return null;
        }

        private static CharacterObject SelectWeightedTemplate(Settlement settlement)
        {
            static bool IsUsable(CharacterObject co)
                => co.Culture is null || !_blacklistedCultures.Contains(co.Culture);

            var maxWeight = 0;
            foreach (var characterObject in HeroTemplates)
            {
                if (!IsUsable(characterObject)) continue;
                var freq = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                maxWeight += freq > 0 ? freq : 100;
            }

            if (maxWeight == 0)
                return null;

            var remaining = settlement.RandomInt(1, maxWeight + 1);

            foreach (var characterObject in HeroTemplates)
            {
                if (!IsUsable(characterObject)) continue;
                var freq = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                remaining -= freq > 0 ? freq : 100;
                if (remaining < 0)
                    return characterObject;
            }

            return HeroTemplates.FirstOrDefault(IsUsable);
        }

        private static bool RollFemale()
            => MBRandom.RandomInt(0, 100) < Globals.Settings.FemaleSpawnChance;
    }
}