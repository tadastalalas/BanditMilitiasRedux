using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;
using static BanditMilitiasRedux.Core.Globals;
// ReSharper disable LoopCanBeConvertedToQuery

namespace BanditMilitiasRedux.Diagnostics;

// BMR-FEATURE: Console commands bmr.stats, bmr.settings, bmr.heroes, bmr.orphans, bmr.fix_orphans | gate: always
// Console text is English only (diagnostics). bmr.stats and bmr.settings also write their output to the BMR log.
public static class ConsoleCommands
{
    private const string CommandGroup = "bmr";
    private const string NoCampaign = "Load a campaign first.";
    private const string Unknown = "unknown";
    private const string NoClan = "(no clan)";
    private const string TotalRow = "Total";
    private const string ColumnSeparator = " | ";
    private const string Indent = "  ";
    private const string WarningMark = "  ! ";
    private const string ChangedMark = " *";
    private const int SettingNameWidth = 30;

    private sealed class ClanRow(string name, bool showCap)
    {
        internal readonly string Name = name;
        internal readonly bool ShowCap = showCap;
        internal int Heroes, Leading, Members, Waiting, Prisoners, Stuck, Militias, Troops;
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("stats", CommandGroup)]
    public static string PrintStats(List<string> args)
    {
        if (Campaign.Current is null)
            return NoCampaign;

        string output = BuildStats();
        BmrLog.Info("bmr.stats" + Environment.NewLine + output);
        return output;
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("settings", CommandGroup)]
    public static string PrintSettings(List<string> args)
    {
        string output = BuildSettings();
        BmrLog.Info("bmr.settings" + Environment.NewLine + output);
        return output;
    }

    private static string BuildStats()
    {
        int cap = HeroesCapPerClan;
        Dictionary<Clan, ClanRow> rowsByClan = new();
        List<ClanRow> rows = [];
        ClanRow? noClanRow = null;
        AddBanditClanRows(rowsByClan, rows);

        ClanRow RowFor(Clan? clan)
        {
            if (clan is null)
                return noClanRow ??= new ClanRow(NoClan, false);
            if (!rowsByClan.TryGetValue(clan, out ClanRow? row))
            {
                rowsByClan[clan] = row = new ClanRow($"{clan.Name} [{clan.StringId}] (not bandit)", false);
                rows.Add(row);
            }
            return row;
        }

        // Every hero is counted once, in the first state that fits: prisoner, leading, member, waiting, stuck.
        int alive = 0, leading = 0, members = 0, waiting = 0, stuck = 0, inSettlements = 0, inParties = 0;
        int deadListed = 0, waitingButBusy = 0;
        IReadOnlyList<Hero> heroes = AllAliveBanditMilitiaHeroes;
        for (int i = 0; i < heroes.Count; i++)
        {
            Hero hero = heroes[i];
            if (hero is null)
                continue;
            if (!hero.IsAlive)
            {
                deadListed++;
                continue;
            }

            alive++;
            bool isWaiting = ReusableHeroesBehavior.IsWaitingForReuse(hero);
            ClanRow row = RowFor(hero.Clan);
            row.Heroes++;
            if (hero.IsPrisoner)
            {
                row.Prisoners++;
                if (hero.CurrentSettlement is not null) inSettlements++;
                else inParties++;
                if (isWaiting) waitingButBusy++;
            }
            else if (hero.PartyBelongedTo is { } party)
            {
                if (party.LeaderHero == hero)
                {
                    leading++;
                    row.Leading++;
                }
                else
                {
                    members++;
                    row.Members++;
                }
                if (isWaiting) waitingButBusy++;
            }
            else if (isWaiting)
            {
                waiting++;
                row.Waiting++;
            }
            else
            {
                stuck++;
                row.Stuck++;
            }
        }

        int waitingNotTracked = 0;
        foreach (KeyValuePair<Clan, List<Hero>> kvp in ReusableHeroesBehavior.GetWaitingHeroesByClan())
            for (int i = 0; i < kvp.Value.Count; i++)
                if (!AllAliveBanditMilitiaHeroes.Contains(kvp.Value[i]))
                    waitingNotTracked++;

        int militias = 0, leaderless = 0, troops = 0, prisoners = 0, largest = 0;
        IReadOnlyList<BanditMilitiaPartyComponent?> components = PowerCalculationManager.GetActiveBanditMilitiaParties(true);
        for (int i = 0; i < components.Count; i++)
        {
            BanditMilitiaPartyComponent? component = components[i];
            MobileParty? militia = component?.MobileParty;
            if (component is null || militia is null)
                continue;

            militias++;
            if (component.Leader is null)
                leaderless++;
            int size = militia.MemberRoster.TotalManCount;
            troops += size;
            prisoners += militia.PrisonRoster?.TotalManCount ?? 0;
            if (size > largest)
                largest = size;
            ClanRow row = RowFor(militia.ActualClan);
            row.Militias++;
            row.Troops += size;
        }

        if (noClanRow is not null)
            rows.Add(noClanRow);

        StringBuilder sb = new();
        AppendHeader(sb);
        sb.AppendLine();
        sb.AppendLine($"Heroes alive: {alive} (cap {cap} per clan)");
        sb.AppendLine($"{Indent}leading a militia   : {leading}");
        sb.AppendLine($"{Indent}member of a party   : {members}");
        sb.AppendLine($"{Indent}waiting for reuse   : {waiting}");
        sb.AppendLine($"{Indent}prisoner            : {inSettlements + inParties} (settlements {inSettlements}, parties {inParties})");
        sb.AppendLine($"{Indent}stuck               : {stuck}");
        sb.AppendLine();
        sb.AppendLine($"Militias: {militias} (leaderless {leaderless})");
        sb.AppendLine($"{Indent}troops {troops}, prisoners {prisoners}, largest {largest} of max size {CalculatedMaxPartySize:0}");
        sb.AppendLine($"{Indent}power {MilitiaPowerPercent:0.0}% of non-militia (limit {ModSettings.GlobalPowerPercent}%), average party power {MilitiaPartyAveragePower:0}");
        sb.AppendLine($"{Indent}raiding {BanditMilitiaManager.CountRaidingMilitias()} of raid cap {RaidCap}");
        sb.AppendLine();
        AppendClanTable(sb, rows, cap);
        sb.AppendLine();
        AppendCounters(sb);
        sb.AppendLine();
        AppendWarnings(sb, alive, leading + members + waiting + inSettlements + inParties + stuck, stuck, leaderless, deadListed, waitingButBusy, waitingNotTracked);
        return sb.ToString();
    }

    // Keyed by the clan itself: different clans can share a name, so the id is added when a name repeats.
    private static void AddBanditClanRows(Dictionary<Clan, ClanRow> rowsByClan, List<ClanRow> rows)
    {
        List<Clan> banditClans = [];
        Dictionary<string, int> nameCounts = new();
        IReadOnlyList<Clan> clans = Clan.All;
        for (int i = 0; i < clans.Count; i++)
        {
            Clan clan = clans[i];
            if (clan is null || !clan.IsBanditFaction)
                continue;
            banditClans.Add(clan);
            string name = clan.Name?.ToString() ?? clan.StringId;
            nameCounts.TryGetValue(name, out int count);
            nameCounts[name] = count + 1;
        }

        for (int i = 0; i < banditClans.Count; i++)
        {
            Clan clan = banditClans[i];
            string name = clan.Name?.ToString() ?? clan.StringId;
            ClanRow row = new(nameCounts[name] > 1 ? $"{name} [{clan.StringId}]" : name, true);
            rowsByClan[clan] = row;
            rows.Add(row);
        }
    }

    private static void AppendHeader(StringBuilder sb)
    {
        AssemblyName assembly = typeof(ModInfo).Assembly.GetName();
        string builtFor = assembly.Name.StartsWith(ModInfo.ModuleId + ".") ? assembly.Name.Substring(ModInfo.ModuleId.Length + 1) : Unknown;
        sb.AppendLine($"{ModInfo.ModuleId} {assembly.Version} (built for {builtFor}), game {RunningGameVersion()}");
        sb.AppendLine($"Day {Helper.DaysPlayed():0} of the campaign ({CampaignTime.Now}). Settings from {SettingsSource()}. Debug logging {OnOff(ModSettings.LoggingEnabled)}.");
    }

    private static void AppendClanTable(StringBuilder sb, List<ClanRow> rows, int cap)
    {
        string[] headers = ["Clan", "Heroes", "Lead", "Member", "Wait", "Prison", "Stuck", "Militias", "Troops"];
        List<string[]> cells = [];
        ClanRow total = new(TotalRow, false);
        for (int i = 0; i < rows.Count; i++)
        {
            ClanRow row = rows[i];
            cells.Add(Cells(row, cap));
            total.Heroes += row.Heroes;
            total.Leading += row.Leading;
            total.Members += row.Members;
            total.Waiting += row.Waiting;
            total.Prisoners += row.Prisoners;
            total.Stuck += row.Stuck;
            total.Militias += row.Militias;
            total.Troops += row.Troops;
        }
        cells.Add(Cells(total, cap));

        int[] widths = new int[headers.Length];
        for (int c = 0; c < headers.Length; c++)
        {
            widths[c] = headers[c].Length;
            for (int r = 0; r < cells.Count; r++)
                widths[c] = Math.Max(widths[c], cells[r][c].Length);
        }

        AppendTableRow(sb, headers, widths);
        for (int r = 0; r < cells.Count; r++)
        {
            if (r == cells.Count - 1)
                AppendRule(sb, widths);
            AppendTableRow(sb, cells[r], widths);
        }
    }

    private static string[] Cells(ClanRow row, int cap) =>
    [
        row.Name, row.ShowCap ? $"{row.Heroes}/{cap}" : row.Heroes.ToString(), row.Leading.ToString(), row.Members.ToString(),
        row.Waiting.ToString(), row.Prisoners.ToString(), row.Stuck.ToString(), row.Militias.ToString(), row.Troops.ToString()
    ];

    // First column left-aligned (names), the others right-aligned (numbers).
    private static void AppendTableRow(StringBuilder sb, string[] cells, int[] widths)
    {
        for (int c = 0; c < cells.Length; c++)
        {
            if (c > 0)
                sb.Append(ColumnSeparator);
            sb.Append(c == 0 ? cells[c].PadRight(widths[c]) : cells[c].PadLeft(widths[c]));
        }
        sb.AppendLine();
    }

    private static void AppendRule(StringBuilder sb, int[] widths)
    {
        int length = 0;
        for (int c = 0; c < widths.Length; c++)
            length += widths[c] + (c > 0 ? ColumnSeparator.Length : 0);
        sb.AppendLine(new string('-', length));
    }

    private static void AppendCounters(StringBuilder sb)
    {
        string since = BmrCounters.CountingSince is { } start ? $"{(CampaignTime.Now - start).ToDays:0.0} days" : Unknown;
        sb.AppendLine($"Since load ({since}, not saved):");
        sb.AppendLine($"{Indent}militias formed {BmrCounters.MilitiasFormed}, merged {BmrCounters.MilitiasMerged}, split {BmrCounters.MilitiasSplit}, defeated {BmrCounters.MilitiasDefeated}");
        sb.AppendLine($"{Indent}heroes created {BmrCounters.HeroesCreated}, reused {BmrCounters.HeroesReused}, captured {BmrCounters.HeroesCaptured}, freed {BmrCounters.HeroesFreed}, killed {BmrCounters.HeroesKilled}");
        sb.AppendLine($"{Indent}stuck heroes put back on load: {BmrCounters.StuckHeroesReclaimedOnLoad}");
    }

    private static void AppendWarnings(StringBuilder sb, int alive, int categorized, int stuck, int leaderless, int deadListed, int waitingButBusy, int waitingNotTracked)
    {
        int before = sb.Length;
        sb.AppendLine("Warnings:");
        int headerLength = sb.Length;
        if (categorized != alive)
            sb.AppendLine($"{WarningMark}hero states add up to {categorized}, not {alive}");
        if (stuck > 0)
            sb.AppendLine($"{WarningMark}{stuck} stuck hero(es): alive, free, in no party and not waiting. They count toward the clan cap but cannot lead.");
        if (leaderless > 0)
            sb.AppendLine($"{WarningMark}{leaderless} militia(s) without a leader");
        if (deadListed > 0)
            sb.AppendLine($"{WarningMark}{deadListed} dead hero(es) still in the hero list");
        if (waitingButBusy > 0)
            sb.AppendLine($"{WarningMark}{waitingButBusy} hero(es) on the waiting list are in a party or a prisoner");
        if (waitingNotTracked > 0)
            sb.AppendLine($"{WarningMark}{waitingNotTracked} hero(es) on the waiting list are not in the hero list");

        if (sb.Length == headerLength)
        {
            sb.Length = before;
            sb.AppendLine("No problems found.");
        }
    }

    private static string BuildSettings()
    {
        StringBuilder sb = new();
        sb.AppendLine($"Settings from {SettingsSource()}.{ChangedMark} = changed from the default.");

        sb.AppendLine("Spawning & Formation");
        Setting(sb, nameof(ModSettings.HeroPoolCap), ModSettings.HeroPoolCap, ModConfiguration.HeroPoolCapDefault);
        Setting(sb, nameof(ModSettings.MergeableSize), ModSettings.MergeableSize, ModConfiguration.MergeableSizeDefault);
        Setting(sb, nameof(ModSettings.RandomSplitChance), ModSettings.RandomSplitChance, ModConfiguration.RandomSplitChanceDefault);

        sb.AppendLine("Training & Growth");
        Setting(sb, nameof(ModSettings.CanTrain), ModSettings.CanTrain, ModConfiguration.CanTrainDefault);
        Setting(sb, nameof(ModSettings.UseBanditOnlyTroopTrees), ModSettings.UseBanditOnlyTroopTrees, ModConfiguration.UseBanditOnlyTroopTreesDefault);
        Setting(sb, nameof(ModSettings.DailyTrainingChance), ModSettings.DailyTrainingChance, ModConfiguration.DailyTrainingChanceDefault);
        Setting(sb, "XpGift", (ModConfiguration.XpGiftLevel)ModSettings.XpGiftIndex, ModConfiguration.XpGiftDefault);
        Setting(sb, nameof(ModSettings.UpgradeUnitsPercent), ModSettings.UpgradeUnitsPercent, ModConfiguration.UpgradeUnitsPercentDefault);
        Setting(sb, nameof(ModSettings.MaxTrainingTier), ModSettings.MaxTrainingTier, ModConfiguration.MaxTrainingTierDefault);
        Setting(sb, nameof(ModSettings.GrowthChance), ModSettings.GrowthChance, ModConfiguration.GrowthChanceDefault);
        Setting(sb, nameof(ModSettings.GrowthPercent), ModSettings.GrowthPercent, ModConfiguration.GrowthPercentDefault);
        Setting(sb, nameof(ModSettings.HeroXpMultiplier), ModSettings.HeroXpMultiplier, ModConfiguration.HeroXpMultiplierDefault);
        Setting(sb, nameof(ModSettings.HeroXpPerWin), ModSettings.HeroXpPerWin, ModConfiguration.HeroXpPerWinDefault);

        sb.AppendLine("Power & Balance");
        Setting(sb, nameof(ModSettings.GlobalPowerPercent), ModSettings.GlobalPowerPercent, ModConfiguration.GlobalPowerPercentDefault);
        Setting(sb, "GoldReward", (ModConfiguration.GoldRewardLevel)ModSettings.GoldRewardIndex, ModConfiguration.GoldRewardDefault);

        sb.AppendLine("Behavior & AI");
        Setting(sb, nameof(ModSettings.MaxStrongerTargetPercent), ModSettings.MaxStrongerTargetPercent, ModConfiguration.MaxStrongerTargetPercentDefault);
        Setting(sb, nameof(ModSettings.MaxWeakerTargetPercent), ModSettings.MaxWeakerTargetPercent, ModConfiguration.MaxWeakerTargetPercentDefault);
        Setting(sb, nameof(ModSettings.IgnoreVillagersCaravans), ModSettings.IgnoreVillagersCaravans, ModConfiguration.IgnoreVillagersCaravansDefault);
        Setting(sb, nameof(ModSettings.ExecutionHonorXp), ModSettings.ExecutionHonorXp, ModConfiguration.ExecutionHonorXpDefault);
        Setting(sb, nameof(ModSettings.AllowPillaging), ModSettings.AllowPillaging, ModConfiguration.AllowPillagingDefault);
        Setting(sb, nameof(ModSettings.PillagingChance), ModSettings.PillagingChance, ModConfiguration.PillagingChanceDefault);
        Setting(sb, nameof(ModSettings.RaidCapPercent), ModSettings.RaidCapPercent, ModConfiguration.RaidCapPercentDefault);

        sb.AppendLine("Appearance & Customization");
        Setting(sb, nameof(ModSettings.BanditMilitiaString), ModSettings.BanditMilitiaString, null);
        Setting(sb, nameof(ModSettings.LeaderlessBanditMilitiaString), ModSettings.LeaderlessBanditMilitiaString, null);
        Setting(sb, nameof(ModSettings.RandomBanners), ModSettings.RandomBanners, ModConfiguration.RandomBannersDefault);
        Setting(sb, nameof(ModSettings.FemaleSpawnChance), ModSettings.FemaleSpawnChance, ModConfiguration.FemaleSpawnChanceDefault);
        Setting(sb, nameof(ModSettings.CheckVoiceGender), ModSettings.CheckVoiceGender, ModConfiguration.CheckVoiceGenderDefault);

        sb.AppendLine("UI & Notifications");
        Setting(sb, nameof(ModSettings.ShowRaids), ModSettings.ShowRaids, ModConfiguration.ShowRaidsDefault);
        Setting(sb, nameof(ModSettings.SkipConversations), ModSettings.SkipConversations, ModConfiguration.SkipConversationsDefault);
        Setting(sb, nameof(ModSettings.RemovePrisonerMessages), ModSettings.RemovePrisonerMessages, ModConfiguration.RemovePrisonerMessagesDefault);
        Setting(sb, nameof(ModSettings.ShowMilitiaMarkers), ModSettings.ShowMilitiaMarkers, ModConfiguration.ShowMilitiaMarkersDefault);
        Setting(sb, nameof(ModSettings.TrackedSizeMinimum), ModSettings.TrackedSizeMinimum, ModConfiguration.TrackedSizeMinimumDefault);

        sb.AppendLine("Advanced");
        Setting(sb, nameof(ModSettings.CooldownHours), ModSettings.CooldownHours, ModConfiguration.CooldownHoursDefault);
        Setting(sb, nameof(ModSettings.MaxItemValue), ModSettings.MaxItemValue, ModConfiguration.MaxItemValueDefault);
        Setting(sb, nameof(ModSettings.IgnoreSizePenalty), ModSettings.IgnoreSizePenalty, ModConfiguration.IgnoreSizePenaltyDefault);

        sb.AppendLine("Technical settings");
        Setting(sb, nameof(ModSettings.LoggingEnabled), ModSettings.LoggingEnabled, ModConfiguration.LoggingEnabledDefault);
        return sb.ToString();
    }

    // No default (texts: the default depends on the game language): never marked as changed.
    private static void Setting(StringBuilder sb, string name, object value, object? defaultValue)
    {
        string text = value is bool flag ? OnOff(flag) : value.ToString();
        bool changed = defaultValue is not null && !value.Equals(defaultValue);
        sb.AppendLine($"{Indent}{name.PadRight(SettingNameWidth)}: {text}{(changed ? ChangedMark : string.Empty)}");
    }

    private static string SettingsSource() => ModSettings.UsesMcm ? "MCM" : ModInfo.ConfigurationFileName;

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string RunningGameVersion()
    {
        try
        {
            return ApplicationVersion.FromParametersFile().ToString();
        }
        catch (Exception)
        {
            return Unknown; // diagnostics only: a missing or unreadable version file must not break the command
        }
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("heroes", CommandGroup)]
    public static string PrintHeroes(List<string> args)
    {
        IReadOnlyList<Hero> heroes = AllAliveBanditMilitiaHeroes;
        int heroCount = heroes.Count;

        List<string[]> rows = [];
        for (int i = 0; i < heroCount; i++)
        {
            Hero hero = heroes[i];
            if (hero is null)
                continue;

            string name = hero.FirstName?.ToString() ?? hero.Name?.ToString() ?? "Unknown";
            string clanName = hero.Clan?.Name?.ToString() ?? "No clan";

            string state;
            string location;
            if (hero.IsPrisoner)
            {
                state = "Prisoner";
                if (hero.CurrentSettlement is not null)
                    location = hero.CurrentSettlement.Name?.ToString() ?? "Unknown settlement";
                else
                    location = hero.PartyBelongedToAsPrisoner?.Name?.ToString() ?? "Unknown captor";
            }
            else
            {
                state = "Active";
                location = hero.PartyBelongedTo?.Name?.ToString() ?? "No party";
            }

            CampaignTime firstSeen = NotorietyBehavior.Instance?.GetFirstSeen(hero) ?? CampaignTime.Now;
            int daysAlive = (int)(CampaignTime.Now - firstSeen).ToDays;
            string aliveText = daysAlive == 1 ? "Alive for 1 day." : $"Alive for {daysAlive} days.";
            string waitingText = ReusableHeroesBehavior.IsWaitingForReuse(hero) ? "Yes" : "No";

            rows.Add([name, state, waitingText, location, clanName, aliveText]);
        }
        
        rows.Sort((a, b) =>
        {
            int clanCompare = string.Compare(a[4], b[4], System.StringComparison.OrdinalIgnoreCase);
            return clanCompare != 0 ? clanCompare : string.Compare(a[0], b[0], System.StringComparison.OrdinalIgnoreCase);
        });

        int nameWidth = "Name".Length;
        int stateWidth = "State".Length;
        int waitingWidth = "Waiting List".Length;
        int locationWidth = "Current Party".Length;
        int clanWidth = "Clan".Length;
        for (int i = 0; i < rows.Count; i++)
        {
            string[] row = rows[i];
            if (row[0].Length > nameWidth) nameWidth = row[0].Length;
            if (row[1].Length > stateWidth) stateWidth = row[1].Length;
            if (row[2].Length > waitingWidth) waitingWidth = row[2].Length;
            if (row[3].Length > locationWidth) locationWidth = row[3].Length;
            if (row[4].Length > clanWidth) clanWidth = row[4].Length;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Heroes : {rows.Count}");
        sb.AppendLine($"{"Name".PadRight(nameWidth)} | {"State".PadRight(stateWidth)} | {"Waiting List".PadRight(waitingWidth)} | {"Current Party".PadRight(locationWidth)} | {"Clan".PadRight(clanWidth)} | Alive In Days");
        for (int i = 0; i < rows.Count; i++)
        {
            string[] row = rows[i];
            sb.AppendLine($"{row[0].PadRight(nameWidth)} | {row[1].PadRight(stateWidth)} | {row[2].PadRight(waitingWidth)} | {row[3].PadRight(locationWidth)} | {row[4].PadRight(clanWidth)} | {row[5]}");
        }

        return sb.ToString();
    }
    
    [CommandLineFunctionality.CommandLineArgumentFunction("orphans", CommandGroup)]
    public static string ListOrphans(List<string> strings)
    {
        StringBuilder sb = new();
        foreach (MobileParty party in MobileParty.All)
        {
            TroopRoster roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                Hero? hero = roster.GetCharacterAtIndex(i)?.HeroObject;
                if (hero is null || hero.IsBanditMilitiaHero() || hero.Clan?.IsBanditFaction != true)
                    continue;
                sb.AppendLine($"{hero.Name} | {hero.Occupation} | {hero.Clan.Name} | party={party.StringId} | {party.PartyComponent?.GetType().Name} | leader={(party.LeaderHero?.Name.ToString() ?? "none")} | size={roster.TotalManCount}");
            }
        }
        return sb.Length == 0 ? "No orphaned bandit heroes in parties." : sb.ToString();
    }
    
    [CommandLineFunctionality.CommandLineArgumentFunction("fix_orphans", CommandGroup)]
    public static string FixOrphans(List<string> strings)
    {
        int adopted = 0;
        List<MobileParty> emptied = [];
        foreach (MobileParty party in MobileParty.All)
        {
            if (party.PartyComponent is not BanditPartyComponent || party.MapEvent is not null)
                continue;

            TroopRoster roster = party.MemberRoster;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                Hero? hero = roster.GetCharacterAtIndex(i)?.HeroObject;
                if (hero is null || hero.IsBanditMilitiaHero() || hero.Occupation != Occupation.Bandit || hero.Clan?.IsBanditFaction != true)
                    continue;

                roster.AddToCounts(hero.CharacterObject, -1);
                AllAliveBanditMilitiaHeroes.Add(hero);
                ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(hero);
                adopted++;
            }
            if (roster.TotalManCount == 0)
                emptied.Add(party);
        }
        for (int i = 0; i < emptied.Count; i++)
            DestroyPartyAction.Apply(null, emptied[i]);

        return $"Re-adopted {adopted} hero(es), destroyed {emptied.Count} empty party(ies).";
    }
}
