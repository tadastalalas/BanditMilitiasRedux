using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BanditMilitias.Helpers;
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

namespace BanditMilitias
{
    internal static class HeroFactory
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        internal static readonly AccessTools.FieldRef<Hero, bool> HasMet =
            AccessTools.FieldRefAccess<Hero, bool>("_hasMet");

        internal static readonly AccessTools.FieldRef<Hero, HeroDeveloper> HeroDeveloperField =
            AccessTools.FieldRefAccess<Hero, HeroDeveloper>("_heroDeveloper");

        internal static readonly ConstructorInfo HeroDeveloperConstructor =
            AccessTools.Constructor(typeof(HeroDeveloper), [typeof(Hero)]);

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<CharacterAttribute>> CharacterAttributesField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<CharacterAttribute>>("_characterAttributes");

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<SkillObject>> HeroSkillsField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<SkillObject>>("_heroSkills");

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<TraitObject>> HeroTraitsField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<TraitObject>>("_heroTraits");

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<PerkObject>> HeroPerksField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<PerkObject>>("_heroPerks");

        private static readonly HashSet<CultureObject> _blacklistedCultures = [];

        internal static Hero CreateHero(Settlement settlement)
        {
            var hero = CreateOrReuseHero(settlement);

            if (hero is null)
            {
                Logger.LogError($"Failed to create hero for settlement {settlement?.Name}");
                return null;
            }

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
            {
                Logger.LogError("Cannot create hero: settlement is null");
                return null;
            }

            var hero = CustomizedCreateHeroAtOccupation(settlement);

            if (hero is null)
            {
                Logger.LogError("Failed to create hero via CustomizedCreateHeroAtOccupation");
                return null;
            }

            HasMet(hero) = false;
            hero.BornSettlement = settlement;

            try
            {
                NameGenerator.Current.GenerateHeroNameAndHeroFullName(
                    hero, out TextObject firstName, out TextObject fullName, false);
                hero.SetName(fullName, firstName);
            }
            catch (Exception ex)
            {
                if (hero.Culture is not null)
                    _blacklistedCultures.Add(hero.Culture);

                Logger.LogWarning(
                    $"Name regeneration failed for culture '{hero.Culture?.StringId ?? "<null>"}'; " +
                    $"keeping the name assigned by CreateSpecialHero. ({ex.GetType().Name}: {ex.Message})");
            }

            HeroDeveloperField(hero) = HeroDeveloperConstructor.Invoke([hero]) as HeroDeveloper;
            hero.HeroDeveloper.InitializeHeroDeveloper();
            hero.Initialize();
            hero.ChangeState(Hero.CharacterStates.Active);
            hero.EncyclopediaText = TextObject.GetEmpty().CopyTextObject();
            HeroAppearanceService.RandomizeAppearance(hero);
            return hero;
        }

        internal static void RandomizeHeroAppearance(Hero hero)
            => HeroAppearanceService.RandomizeAppearance(hero);

        private static Hero CustomizedCreateHeroAtOccupation(Settlement settlement)
        {
            const int MaxAttempts = 6;

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var template = SelectWeightedTemplate(settlement);
                if (template is null)
                {
                    Logger.LogWarning(
                        "No usable hero templates available for weighted selection " +
                        "(all candidate cultures have been blacklisted due to broken name lists).");
                    return null;
                }

                Hero specialHero;
                try
                {
                    specialHero = HeroCreator.CreateSpecialHero(
                        template, settlement, null, null);
                }
                catch (Exception ex)
                {
                    var culture = template.Culture;
                    if (culture is not null && _blacklistedCultures.Add(culture))
                    {
                        Logger.LogWarning(
                            $"Culture '{culture.StringId}' produced an exception during " +
                            $"hero creation (likely missing/empty name lists from a broken " +
                            $"mod or corrupted save). Blacklisting it for this session. " +
                            $"({ex.GetType().Name}: {ex.Message})");
                    }
                    else if (culture is null)
                    {
                        Logger.LogError(
                            $"Hero template {template?.StringId} has no culture and threw " +
                            $"during CreateSpecialHero: {ex}");
                        return null;
                    }
                    continue;
                }

                if (specialHero is null)
                {
                    Logger.LogWarning(
                        $"CreateSpecialHero returned null for template {template} at {settlement?.Name}");
                    continue;
                }

                specialHero.AddPower(MBRandom.RandomFloat * 20f);
                specialHero.ChangeState(Hero.CharacterStates.Active);

                specialHero.IsFemale = RollFemale();

                Logger.LogTrace($"Created a new hero {specialHero}");
                return specialHero;
            }

            Logger.LogWarning(
                $"Could not create a hero after {MaxAttempts} attempts; all chosen " +
                $"templates produced exceptions. Skipping this spawn.");
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