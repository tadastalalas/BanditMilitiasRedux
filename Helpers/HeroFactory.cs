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
    /// <summary>
    /// Responsible for creating and initialising bandit militia hero objects.
    /// All Harmony field refs that are exclusive to hero creation live here.
    /// </summary>
    internal static class HeroFactory
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        // ── Harmony field refs (hero-creation exclusive) ─────────────────────────

        internal static readonly AccessTools.FieldRef<Hero, bool> HasMet =
            AccessTools.FieldRefAccess<Hero, bool>("_hasMet");

        internal static readonly AccessTools.FieldRef<Hero, HeroDeveloper> HeroDeveloperField =
            AccessTools.FieldRefAccess<Hero, HeroDeveloper>("_heroDeveloper");

        internal static readonly ConstructorInfo HeroDeveloperConstructor =
            AccessTools.Constructor(typeof(HeroDeveloper), [typeof(Hero)]);

        // Declared but not yet called — kept here so they're co-located with the
        // other hero field refs when they're eventually needed.
        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<CharacterAttribute>> CharacterAttributesField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<CharacterAttribute>>("_characterAttributes");

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<SkillObject>> HeroSkillsField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<SkillObject>>("_heroSkills");

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<TraitObject>> HeroTraitsField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<TraitObject>>("_heroTraits");

        private static readonly AccessTools.FieldRef<Hero, PropertyOwner<PerkObject>> HeroPerksField =
            AccessTools.FieldRefAccess<Hero, PropertyOwner<PerkObject>>("_heroPerks");

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a fully initialised hero, registers it in <see cref="Heroes"/>, 
        /// equips it from the pool, and optionally grants leadership perks.
        /// </summary>
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

        /// <summary>
        /// Creates a hero via <see cref="CustomizedCreateHeroAtOccupation"/> and
        /// wires up all required fields so the game treats it as a temporary
        /// special hero — no clan, no supporter, no death mark.
        /// Mirrors the pattern used by the game's own quest henchman heroes.
        /// </summary>
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

            NameGenerator.Current.GenerateHeroNameAndHeroFullName(
                hero, out TextObject firstName, out TextObject fullName, false);
            hero.SetName(fullName, firstName);

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

        // ── Private ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Weighted-random selection from <see cref="Globals.HeroTemplates"/> using
        /// each template's <c>Frequency</c> trait, then creates a special hero from
        /// the chosen template.
        /// Clan and supporterOfClan are intentionally null — the hero is a
        /// temporary combat leader, not a lord.
        /// Occupation stays as <see cref="Occupation.Bandit"/> inherited from the
        /// template so that <see cref="Extensions.IsBM(CharacterObject)"/> and all
        /// downstream identity checks continue to work correctly.
        /// </summary>
        private static Hero CustomizedCreateHeroAtOccupation(Settlement settlement)
        {
            var maxWeight = 0;
            foreach (var characterObject in HeroTemplates)
            {
                var freq = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                maxWeight += freq > 0 ? freq : 100;
            }

            if (maxWeight == 0)
            {
                Logger.LogWarning("No hero templates available for weighted selection.");
                return null;
            }

            CharacterObject template = null;
            var remaining = settlement.RandomInt(1, maxWeight + 1);

            foreach (var characterObject in HeroTemplates)
            {
                var freq = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                remaining -= freq > 0 ? freq : 100;
                if (remaining < 0)
                {
                    template = characterObject;
                    break;
                }
            }

            // Fallback: RandomInt upper-bound edge case can leave template null
            template ??= HeroTemplates.GetRandomElement();

            if (template is null)
            {
                Logger.LogWarning("Could not select a hero template.");
                return null;
            }

            // Pass null for clan and supporterOfClan — same pattern as the game's
            // own quest henchman heroes (e.g. RivalGangMovingInIssueQuest).
            // Occupation stays as whatever the bandit template provides (Occupation.Bandit)
            // so that CharacterObject.IsBM() and all downstream identity checks continue
            // to work correctly throughout the mod.
            var specialHero = HeroCreator.CreateSpecialHero(
                template, settlement, null, null);

            if (specialHero is null)
            {
                Logger.LogWarning(
                    $"CreateSpecialHero returned null for template {template} at {settlement?.Name}");
                return null;
            }

            specialHero.AddPower(MBRandom.RandomFloat * 20f);
            specialHero.ChangeState(Hero.CharacterStates.Active);
            specialHero.IsFemale = RollFemale();

            Logger.LogTrace($"Created a new hero {specialHero}");
            return specialHero;
        }

        private static bool RollFemale()
            => MBRandom.RandomInt(0, 100) < Globals.Settings.FemaleSpawnChance;
    }
}