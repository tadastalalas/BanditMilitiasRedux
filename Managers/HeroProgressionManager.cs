using System;
using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Diagnostics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Militia leader progression, won battles give skill XP (scaled by enemy strength), a focus point every 7 wins and an attribute point every 14 wins | gate: setting:CanTrain, setting:HeroXpPerWin
internal static class HeroProgressionManager
{
    private static readonly EquipmentIndex[] WeaponSlots = [EquipmentIndex.Weapon0, EquipmentIndex.Weapon1, EquipmentIndex.Weapon2, EquipmentIndex.Weapon3];

    // True while the mod itself gives XP, so the XP multiplier patch leaves it unscaled.
    internal static bool IsGivingUnscaledXp { get; private set; }

    internal static void AddUnscaledSkillXp(Hero hero, SkillObject skill, float xp, bool shouldNotify = true)
    {
        IsGivingUnscaledXp = true;
        try
        {
            hero.HeroDeveloper.AddSkillXp(skill, xp, true, shouldNotify);
        }
        finally
        {
            IsGivingUnscaledXp = false;
        }
    }

    // battlesWon already includes this win.
    internal static void RewardWin(Hero leader, float strengthRatio, int battlesWon)
    {
        if (!ModSettings.CanTrain || leader?.HeroDeveloper is null)
            return;

        List<SkillObject> weaponSkills = GetWeaponSkillsStrongestFirst(leader);

        int baseXp = ModSettings.HeroXpPerWin;
        if (baseXp > 0)
        {
            float factor = Math.Min(Math.Max(strengthRatio, Balance.HeroWinXpRatioMin), Balance.HeroWinXpRatioMax);
            float xp = baseXp * factor;
            AddUnscaledSkillXp(leader, DefaultSkills.Leadership, xp, false);
            AddUnscaledSkillXp(leader, DefaultSkills.Tactics, xp, false);
            foreach (SkillObject skill in weaponSkills)
                AddUnscaledSkillXp(leader, skill, xp, false);
            AddUnscaledSkillXp(leader, IsMounted(leader) ? DefaultSkills.Riding : DefaultSkills.Athletics, xp, false);

            if (BmrLog.IsDebugEnabled)
                BmrLog.Debug($"Win reward: {leader.Name} got {xp:0} XP per skill (strength ratio {strengthRatio:0.00}, wins {battlesWon}).");
        }

        if (battlesWon > 0 && battlesWon % Balance.FocusPointEveryWins == 0)
            TryAddFocusPoint(leader, weaponSkills);

        if (battlesWon > 0 && battlesWon % Balance.AttributePointEveryWins == 0)
            TryAddAttributePoint(leader);
    }

    private static List<SkillObject> GetWeaponSkillsStrongestFirst(Hero hero)
    {
        List<SkillObject> skills = [];
        foreach (EquipmentIndex slot in WeaponSlots)
        {
            EquipmentElement element = hero.BattleEquipment[slot];
            SkillObject? skill = element.IsEmpty ? null : element.Item?.RelevantSkill;
            if (skill is not null && !skills.Contains(skill))
                skills.Add(skill);
        }

        // Stable for equal values: the earlier weapon slot stays first.
        List<SkillObject> sorted = [];
        foreach (SkillObject skill in skills)
        {
            int value = hero.GetSkillValue(skill);
            int index = 0;
            while (index < sorted.Count && hero.GetSkillValue(sorted[index]) >= value)
                index++;
            sorted.Insert(index, skill);
        }

        return sorted;
    }

    private static bool IsMounted(Hero hero) => !hero.BattleEquipment[EquipmentIndex.Horse].IsEmpty;

    // Strongest weapon skill in use that is not full, then the next one, then Tactics, then Leadership; else nothing.
    private static void TryAddFocusPoint(Hero leader, List<SkillObject> weaponSkills)
    {
        int maxFocus = Campaign.Current.Models.CharacterDevelopmentModel.MaxFocusPerSkill;
        HeroDeveloper developer = leader.HeroDeveloper;

        List<SkillObject> candidates = [.. weaponSkills, DefaultSkills.Tactics, DefaultSkills.Leadership];
        foreach (SkillObject skill in candidates)
        {
            if (developer.GetFocus(skill) >= maxFocus)
                continue;

            developer.AddFocus(skill, 1, false);
            if (BmrLog.IsDebugEnabled)
                BmrLog.Debug($"Win reward: {leader.Name} got a focus point in {skill.Name}.");
            return;
        }
    }

    // A random attribute that is not full; if all are full, nothing.
    private static void TryAddAttributePoint(Hero leader)
    {
        int maxAttribute = Campaign.Current.Models.CharacterDevelopmentModel.MaxAttribute;
        CharacterAttribute[] attributes =
        [
            DefaultCharacterAttributes.Vigor, DefaultCharacterAttributes.Control, DefaultCharacterAttributes.Endurance,
            DefaultCharacterAttributes.Cunning, DefaultCharacterAttributes.Social, DefaultCharacterAttributes.Intelligence,
        ];

        List<CharacterAttribute> notFull = [];
        foreach (CharacterAttribute attribute in attributes)
        {
            if (leader.GetAttributeValue(attribute) < maxAttribute)
                notFull.Add(attribute);
        }

        if (notFull.Count == 0)
            return;

        CharacterAttribute chosen = notFull[MBRandom.RandomInt(notFull.Count)];
        leader.HeroDeveloper.AddAttribute(chosen, 1, false);
        if (BmrLog.IsDebugEnabled)
            BmrLog.Debug($"Win reward: {leader.Name} got an attribute point in {chosen.Name}.");
    }
}
