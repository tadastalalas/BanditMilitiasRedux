using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ModuleManager;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Helpers
{
    internal static class EquipmentPool
    {
        private const int SlotRetryLimit = 20;

        private static readonly AccessTools.StructFieldRef<EquipmentElement, ItemModifier> ItemModifier =
            AccessTools.StructFieldRefAccess<EquipmentElement, ItemModifier>("<ItemModifier>k__BackingField");

        private static readonly HashSet<string> VerbotenItemStringIds =
        [
            "bound_adarga",
            "old_kite_sparring_shield_shoulder",
            "old_horsemans_kite_shield_shoulder",
            "old_horsemans_kite_shield",
            "banner_mid",
            "banner_big",
            "campaign_banner_small",
            "torch",
            "wooden_sword_t1",
            "wooden_sword_t2",
            "wooden_2hsword_t1",
            "practice_spear_t1",
            "horse_whip",
            "push_fork",
            "mod_banner_1",
            "mod_banner_2",
            "mod_banner_3",
            "throwing_stone",
            "ballista_projectile",
            "ballista_projectile_burning",
            "boulder",
            "pot",
            "grapeshot_stack",
            "grapeshot_fire_stack",
            "grapeshot_projectile",
            "grapeshot_fire_projectile",
            "oval_shield",
        ];

        private static readonly HashSet<string> VerbotenSaddleStringIds =
        [
            "celtic_frost",
            "saddle_of_aeneas",
            "fortunas_choice",
            "aseran_village_harness",
            "bandit_saddle_steppe",
            "bandit_saddle_desert",
        ];

        internal static Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = [];
        internal static List<EquipmentElement> EquipmentItems = [];
        internal static List<EquipmentElement> EquipmentItemsNoBow = [];
        internal static List<Equipment> BanditEquipment = [];
        internal static List<ItemObject> Arrows = [];
        internal static List<ItemObject> Bolts = [];
        internal static List<ItemObject> Mounts = [];
        internal static List<ItemObject> Saddles = [];
        internal static List<ItemObject> CamelSaddles = [];
        internal static List<ItemObject> NonCamelSaddles = [];

        internal static void Reset()
        {
            ItemTypes = [];
            EquipmentItems = [];
            EquipmentItemsNoBow = [];
            BanditEquipment = [];
            Arrows = [];
            Bolts = [];
            Mounts = [];
            Saddles = [];
            CamelSaddles = [];
            NonCamelSaddles = [];
        }

        internal static void Populate()
        {
            var maxValue = Settings!.MaxItemValue;
            var allItems = Items.All.ToListQ();

            Mounts = [.. allItems
                .WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Horse
                    && !i.StringId.Contains("unmountable")
                    && i.Value <= maxValue)];

            Saddles = [.. allItems
                .WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.HorseHarness
                    && !i.StringId.Contains("mule")
                    && !VerbotenSaddleStringIds.Contains(i.StringId)
                    && i.Value <= maxValue)];

            CamelSaddles = [.. Saddles.WhereQ(s => s.StringId.Contains("camel"))];
            NonCamelSaddles = [.. Saddles.WhereQ(s => !s.StringId.Contains("camel"))];

            var weapons = allItems.WhereQ(i =>
                    !i.IsCraftedByPlayer
                    && i.ItemType is not (ItemObject.ItemTypeEnum.Goods
                        or ItemObject.ItemTypeEnum.Horse
                        or ItemObject.ItemTypeEnum.HorseHarness
                        or ItemObject.ItemTypeEnum.Animal
                        or ItemObject.ItemTypeEnum.Banner
                        or ItemObject.ItemTypeEnum.Book
                        or ItemObject.ItemTypeEnum.Invalid)
                    && i.ItemCategory.StringId != "garment"
                    && !i.StringId.EndsWith("blunt")
                    && !i.StringId.Contains("sparring")
                    && i.Value <= maxValue)
                .ToList();
            /*
            var runningCivilizedMod = AppDomain.CurrentDomain.GetAssemblies()
                .AnyQ(a => a.FullName.Contains("Civilized"));
            if (runningCivilizedMod)
                weapons.RemoveAll(i => !i.IsCivilian);
            */
            weapons.RemoveAll(item => VerbotenItemStringIds.Contains(item.StringId));

            Arrows = [.. weapons.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows)];
            Bolts  = [.. weapons.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts)];

            var weaponPool = weapons
                .WhereQ(i => i.ItemType is
                    ItemObject.ItemTypeEnum.OneHandedWeapon or
                    ItemObject.ItemTypeEnum.TwoHandedWeapon or
                    ItemObject.ItemTypeEnum.Polearm         or
                    ItemObject.ItemTypeEnum.Thrown          or
                    ItemObject.ItemTypeEnum.Shield          or
                    ItemObject.ItemTypeEnum.Bow             or
                    ItemObject.ItemTypeEnum.Crossbow)
                .ToList();

            EquipmentItems = [];
            weaponPool.Do(i => EquipmentItems.Add(new EquipmentElement(i)));

            EquipmentItemsNoBow = [.. EquipmentItems
                .WhereQ(x => x.Item.ItemType != ItemObject.ItemTypeEnum.Bow
                          && x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow)];

            foreach (ItemObject.ItemTypeEnum itemType in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
            {
                ItemTypes[itemType] = [.. allItems
                    .WhereQ(i => i.Type == itemType
                        && i.Value >= 1000
                        && i.Value <= maxValue)];
            }

            BanditEquipment = new List<Equipment>(10000);
            for (var i = 0; i < 10000; i++)
                BanditEquipment.Add(BuildViableEquipmentSet());
        }

        internal static Equipment GetRandomEquipmentSet()
            => BanditEquipment.GetRandomElement();

        

        internal static void AdjustCavalryCount(TroopRoster troopRoster)
        {
            int safety = 0;
            while (safety++ < 200)
            {
                var mountedTroops = troopRoster.GetTroopRoster()
                    .WhereQ(c => IsMounted(c.Character)
                        && !c.Character.IsHero
                        && c.Character.OriginalCharacter is null)
                    .ToListQ();
                
                if (mountedTroops.Count == 0)
                    break;
                
                int mountedCount = mountedTroops.SumQ(e => e.Number);

                int mountedExcessCount = mountedCount - Convert.ToInt32(troopRoster.TotalManCount / 2);
                
                if (mountedExcessCount <= 0)
                    break;

                var element = mountedTroops.GetRandomElement();
                var mountedTroopsToRemoveCount = Math.Min(element.Number, MBRandom.RandomInt(1, mountedExcessCount + 1));
                troopRoster.AddToCounts(element.Character, -mountedTroopsToRemoveCount);
            }
        }

        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            var roster = troopRoster.GetTroopRoster();
            int total = 0;
            for (int i = 0, n = roster.Count; i < n; i++)
            {
                var e = roster[i];
                if (e.Character is null)
                    continue;

                if (IsMounted(e.Character))
                    total += e.Number;
            }
            return total;
        }
        
        private static bool IsMounted(CharacterObject character) => !character.Equipment[10].IsEmpty;

        private static Equipment BuildViableEquipmentSet()
        {
            var gear       = new Equipment();
            var haveShield = false;
            var haveBow    = false;
            var retries = 0;

            try
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    EquipmentElement randomElement = default;
                    switch (slot)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            randomElement = EquipmentItemsNoBow.GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    if (randomElement.Item?.HasArmorComponent == true)
                        ItemModifier(ref randomElement) = randomElement.Item.ArmorComponent
                            .ItemModifierGroup?.ItemModifiers
                            .GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    if (randomElement.Item?.HasWeaponComponent == true)
                        ItemModifier(ref randomElement) = randomElement.Item.WeaponComponent
                            .ItemModifierGroup?.ItemModifiers
                            .GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    if (slot == 3 && !gear[3].IsEmpty)
                        break;

                    if (randomElement.Item?.ItemType is ItemObject.ItemTypeEnum.Bow
                                                     or ItemObject.ItemTypeEnum.Crossbow)
                    {
                        if (slot < 3)
                        {
                            if (haveBow)
                            {
                                if (++retries >= SlotRetryLimit)
                                    continue;

                                slot--;
                                continue;
                            }

                            haveBow      = true;
                            gear[slot]   = randomElement;

                            if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow && Arrows.Count > 0)
                                gear[3] = new EquipmentElement(Arrows[MBRandom.RandomInt(0, Arrows.Count)]);
                            else if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow && Bolts.Count > 0)
                                gear[3] = new EquipmentElement(Bolts[MBRandom.RandomInt(0, Bolts.Count)]);
                            continue;
                        }

                        randomElement = EquipmentItemsNoBow.GetRandomElement();
                    }

                    if (randomElement.Item?.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        if (haveShield)
                        {
                            if (++retries >= SlotRetryLimit)
                                continue;

                            slot--;
                            continue;
                        }

                        haveShield = true;
                    }

                    gear[slot] = randomElement;
                }

                if (ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].Count > 0)
                    gear[5] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].GetRandomElement());
                if (ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].Count > 0)
                    gear[6] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].GetRandomElement());
                if (ItemTypes[ItemObject.ItemTypeEnum.LegArmor].Count > 0)
                    gear[7] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.LegArmor].GetRandomElement());
                if (ItemTypes[ItemObject.ItemTypeEnum.HandArmor].Count > 0)
                    gear[8] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HandArmor].GetRandomElement());
                if (ItemTypes[ItemObject.ItemTypeEnum.Cape].Count > 0)
                    gear[9] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.Cape].GetRandomElement());
            }
            catch (Exception) { }

            var clone = gear.Clone();
            clone.SyncEquipments = true;
            return clone;
        }
    }
}