using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ModuleManager;
using static BanditMilitias.Globals;

namespace BanditMilitias
{
    /// <summary>
    /// Owns all item/equipment data used to outfit bandit militia parties and
    /// their heroes. Call <see cref="Populate"/> once on map init, then use
    /// <see cref="GetRandomEquipmentSet"/> and <see cref="AdjustCavalryCount"/>
    /// at party creation time.
    /// </summary>
    internal static class EquipmentPool
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        // ── Max retries per weapon slot before giving up on a constraint ─────────
        // Prevents infinite spin when a modded item set contains only bows/shields.
        private const int SlotRetryLimit = 20;

        // ── Harmony field accessor (only used inside BuildViableEquipmentSet) ────
        private static readonly AccessTools.StructFieldRef<EquipmentElement, ItemModifier> ItemModifier =
            AccessTools.StructFieldRefAccess<EquipmentElement, ItemModifier>("<ItemModifier>k__BackingField");

        // ── Blocked items ────────────────────────────────────────────────────────

        private static readonly HashSet<string> VerbotenItemStringIds = new()
        {
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
        };

        private static readonly HashSet<string> VerbotenSaddleStringIds = new()
        {
            "celtic_frost",
            "saddle_of_aeneas",
            "fortunas_choice",
            "aseran_village_harness",
            "bandit_saddle_steppe",
            "bandit_saddle_desert",
        };

        // ── Owned data ────────────────────────────────────────────────────────────

        internal static Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes    = new();
        internal static List<EquipmentElement> EquipmentItems                              = new();
        internal static List<EquipmentElement> EquipmentItemsNoBow                         = new();
        internal static List<Equipment>        BanditEquipment                             = new();
        internal static List<ItemObject>       Arrows                                      = new();
        internal static List<ItemObject>       Bolts                                       = new();
        internal static List<ItemObject>       Mounts                                      = new();
        internal static List<ItemObject>       Saddles                                     = new();
        internal static List<ItemObject>       CamelSaddles                                = new();
        internal static List<ItemObject>       NonCamelSaddles                             = new();

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        internal static void Reset()
        {
            ItemTypes       = new Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>>();
            EquipmentItems  = new List<EquipmentElement>();
            EquipmentItemsNoBow = new List<EquipmentElement>();
            BanditEquipment = new List<Equipment>();
            Arrows          = new List<ItemObject>();
            Bolts           = new List<ItemObject>();
            Mounts          = new List<ItemObject>();
            Saddles         = new List<ItemObject>();
            CamelSaddles    = new List<ItemObject>();
            NonCamelSaddles = new List<ItemObject>();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Builds all item lists and pre-generates the equipment pool.</summary>
        internal static void Populate()
        {
            var maxValue = Globals.Settings.MaxItemValue;
            var allItems = Items.All.ToListQ();

            Mounts = allItems
                .WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Horse
                    && !i.StringId.Contains("unmountable")
                    && i.Value <= maxValue)
                .ToList();

            Saddles = allItems
                .WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.HorseHarness
                    && !i.StringId.Contains("mule")
                    && !VerbotenSaddleStringIds.Contains(i.StringId)
                    && i.Value <= maxValue)
                .ToList();

            CamelSaddles    = Saddles.WhereQ(s => s.StringId.Contains("camel")).ToList();
            NonCamelSaddles = Saddles.WhereQ(s => !s.StringId.Contains("camel")).ToList();

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

            var runningCivilizedMod = AppDomain.CurrentDomain.GetAssemblies()
                .AnyQ(a => a.FullName.Contains("Civilized"));
            if (!runningCivilizedMod)
                weapons.RemoveAll(i => !i.IsCivilian);

            weapons.RemoveAll(item => VerbotenItemStringIds.Contains(item.StringId));

            Arrows = weapons.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows).ToList();
            Bolts  = weapons.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts).ToList();

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

            EquipmentItems = new List<EquipmentElement>();
            weaponPool.Do(i => EquipmentItems.Add(new EquipmentElement(i)));

            EquipmentItemsNoBow = EquipmentItems
                .WhereQ(x => x.Item.ItemType != ItemObject.ItemTypeEnum.Bow
                          && x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow)
                .ToList();

            // Armour by slot — minimum value of 1000 filters out placeholder items
            foreach (ItemObject.ItemTypeEnum itemType in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
            {
                ItemTypes[itemType] = allItems
                    .WhereQ(i => i.Type == itemType
                        && i.Value >= 1000
                        && i.Value <= maxValue)
                    .ToList();
            }

            // Pre-generate 10 000 ready-to-use equipment sets up front so
            // party creation never blocks on building one at runtime.
            BanditEquipment = new List<Equipment>(10000);
            for (var i = 0; i < 10000; i++)
                BanditEquipment.Add(BuildViableEquipmentSet());

            Logger.LogTrace(
                $"EquipmentPool populated — weapons:{EquipmentItems.Count} " +
                $"armour:{ItemTypes.Values.Sum(v => v.Count)} " +
                $"arrows:{Arrows.Count} bolts:{Bolts.Count} " +
                $"mounts:{Mounts.Count} saddles:{Saddles.Count}");
        }

        /// <summary>Returns a random pre-built equipment set from the pool.</summary>
        internal static Equipment GetRandomEquipmentSet()
            => BanditEquipment.GetRandomElement();

        /// <summary>
        /// Reduces mounted troops in <paramref name="troopRoster"/> until they
        /// make up no more than 50 % of the total headcount.
        /// </summary>
        internal static void AdjustCavalryCount(TroopRoster troopRoster)
        {
            try
            {
                var safety = 0;
                var mountedTroops = troopRoster.GetTroopRoster()
                    .WhereQ(c => !c.Character.Equipment[10].IsEmpty
                        && !c.Character.IsHero
                        && c.Character.OriginalCharacter is null)
                    .ToListQ();
                int mountedCount = mountedTroops.SumQ(e => e.Number);

                while (safety++ < 200)
                {
                    int delta = mountedCount - Convert.ToInt32(troopRoster.TotalManCount / 2);
                    if (delta <= 0) break;
                    if (mountedTroops.Count == 0) break;

                    if (safety == 200)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Bandit Militias error.  Please open a bug report and include the file cavalry.txt from the mod folder.",
                            new Color(1, 0, 0)));
                        var output = new StringBuilder();
                        output.AppendLine(
                            $"NumMountedTroops(troopRoster) {mountedCount} - " +
                            $"Convert.ToInt32(troopRoster.TotalManCount / 2) {Convert.ToInt32(troopRoster.TotalManCount / 2)}");
                        mountedTroops.Do(t => output.AppendLine($"{t.Character}: {t.Number} ({t.WoundedNumber})"));
                        File.WriteAllText(
                            ModuleHelper.GetModuleFullPath("BanditMilitias") + "cavalry.txt",
                            output.ToString());
                    }

                    var element = mountedTroops.GetRandomElement();
                    var count = Math.Min(element.Number, MBRandom.RandomInt(1, delta + 1));
                    troopRoster.AddToCounts(element.Character, -count);
                    mountedCount -= count;
                    if (element.Number - count <= 0)
                        mountedTroops.Remove(element);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Problem adjusting cavalry count, please open a bug report."));
                Logger.LogError(ex, "Error adjusting cavalry count.");
            }
        }

        /// <summary>Counts mounted (non-hero) troops in a roster.</summary>
        internal static int NumMountedTroops(TroopRoster troopRoster)
            => troopRoster.GetTroopRoster()
                .WhereQ(e => e.Character.Equipment[10].Item is not null)
                .SumQ(e => e.Number);

        // ── Private ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds one equipment set: 4 weapon slots obeying the bow/shield
        /// uniqueness rule, plus a full armour set.
        /// <para>
        /// Bug fix: each slot now has a <see cref="SlotRetryLimit"/> retry cap
        /// so an exotic modded item pool (all bows / all shields) cannot cause
        /// an infinite loop.
        /// </para>
        /// </summary>
        private static Equipment BuildViableEquipmentSet()
        {
            var gear       = new Equipment();
            var haveShield = false;
            var haveBow    = false;

            try
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    var retries = 0;

                    EquipmentElement randomElement = default;

                    switch (slot)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            // Ammo already placed by a bow in slot 0/1 — no bow allowed here
                            randomElement = EquipmentItemsNoBow.GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    // Apply a random item modifier when available
                    if (randomElement.Item?.HasArmorComponent == true)
                        ItemModifier(ref randomElement) = randomElement.Item.ArmorComponent
                            .ItemModifierGroup?.ItemModifiers
                            .GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    if (randomElement.Item?.HasWeaponComponent == true)
                        ItemModifier(ref randomElement) = randomElement.Item.WeaponComponent
                            .ItemModifierGroup?.ItemModifiers
                            .GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    // Slot 3 filled by a bow's ammo earlier — we're done
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
                                {
                                    Logger.LogWarning("BuildViableEquipmentSet: bow retry limit reached, skipping slot.");
                                    continue;
                                }
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
                            {
                                Logger.LogWarning("BuildViableEquipmentSet: shield retry limit reached, skipping slot.");
                                continue;
                            }
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
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error building equipment set.");
                Logger.LogError(
                    $"Armour loaded: {ItemTypes.Values.Sum(v => v.Count)}\n\t" +
                    $"Non-armour loaded: {EquipmentItems.Count}\n\t" +
                    $"Arrows:{Arrows.Count}\n\tBolts:{Bolts.Count}\n\t" +
                    $"Mounts:{Mounts.Count}\n\tSaddles:{Saddles.Count}");
            }

            var clone = gear.Clone();
            clone.SyncEquipments = true;
            return clone;
        }
    }
}