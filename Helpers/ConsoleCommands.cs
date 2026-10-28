using System.Collections.Generic;
using System.Text;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Library;
using static BanditMilitiasRedux.Globals;
// ReSharper disable LoopCanBeConvertedToQuery

namespace BanditMilitiasRedux.Helpers
{
    public static class ConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("stats", "bmr")]
        public static string PrintStats(List<string> args)
        {
            IReadOnlyList<BanditMilitiaPartyComponent> bms = PowerCalculationManager.GetActiveBanditMilitiaParties(true);
            int partyCount = bms.Count;
            int totalTroops = 0;
            int militiaPrisonerCount = 0;
            int leaderless = 0;

            for (int i = 0; i < partyCount; i++)
            {
                MobileParty? mobileParty = bms[i].MobileParty;
                if (mobileParty is null)
                    continue;
                
                if (bms[i].Leader is null)
                    leaderless++;

                totalTroops += mobileParty.MemberRoster.TotalManCount;
                militiaPrisonerCount += mobileParty.Party?.PrisonRoster?.TotalManCount ?? 0;
            }

            int banditClanCount = 0;
            Dictionary<string, int> militiaLeadersPerClan = [];
            IReadOnlyList<Clan> clans = Clan.All;
            for (int i = 0; i < clans.Count; i++)
            {
                Clan clan = clans[i];
                if (!clan.IsBanditFaction)
                    continue;

                banditClanCount++;
                string clanName = clan.Name?.ToString() ?? clan.StringId ?? "UnknownClan";
                militiaLeadersPerClan[clanName] = 0;
            }

            IReadOnlyList<Hero> heroesForClanCount = AllAliveBanditMilitiaHeroes;
            for (int i = 0; i < heroesForClanCount.Count; i++)
            {
                Clan? clan = heroesForClanCount[i]?.Clan;
                if (clan is null)
                    continue;

                string clanName = clan.Name?.ToString() ?? clan.StringId ?? "UnknownClan";
                militiaLeadersPerClan.TryGetValue(clanName, out int current);
                militiaLeadersPerClan[clanName] = current + 1;
            }

            int imprisonedInSettlements = 0;
            int imprisonedInAiLordParties = 0;
            IReadOnlyList<Hero> allBanditMilitiaHeroes = AllAliveBanditMilitiaHeroes;
            foreach (var hero in allBanditMilitiaHeroes)
            {
                if (!hero.IsBanditMilitiaHero() || !hero.IsPrisoner)
                    continue;

                if (hero.CurrentSettlement is not null)
                {
                    imprisonedInSettlements++;
                    continue;
                }

                PartyBase? captorParty = hero.PartyBelongedToAsPrisoner;
                if (captorParty?.MobileParty?.IsLordParty == true)
                    imprisonedInAiLordParties++;
            }

            int waitingForReuseTotal = 0;
            Dictionary<string, int> waitingForReusePerClan = [];
            IReadOnlyDictionary<Clan, List<Hero>> waitingByClan = ReusableHeroesBehavior.GetWaitingHeroesByClan();
            foreach (KeyValuePair<Clan, List<Hero>> kvp in waitingByClan)
            {
                Clan clan = kvp.Key;
                IReadOnlyList<Hero> waitingHeroes = kvp.Value;
                int clanWaitingCount = waitingHeroes.Count;

                waitingForReuseTotal += clanWaitingCount;

                string clanName = clan.Name?.ToString() ?? clan.StringId ?? "UnknownClan";
                waitingForReusePerClan[clanName] = clanWaitingCount;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"All alive heroes                 : {AllAliveBanditMilitiaHeroes.Count}");
            sb.AppendLine($"   waiting for reuse             : {waitingForReuseTotal}");
            sb.AppendLine($"   imprisoned in settlements     : {imprisonedInSettlements}");
            sb.AppendLine($"   imprisoned in parties         : {imprisonedInAiLordParties}");
            sb.AppendLine($"Active parties                   : {partyCount}");
            sb.AppendLine($"Leaderless parties               : {leaderless}");
            sb.AppendLine($"   troops in all parties         : {totalTroops}");
            sb.AppendLine($"   prisoners in all parties      : {militiaPrisonerCount}");
            sb.AppendLine($"Bandit clans in world            : {banditClanCount}");
            sb.AppendLine($"Global militia power             : {MilitiaPowerPercent:0.0}% of non-militia");
            sb.AppendLine($"Avg party power                  : {MilitiaPartyAveragePower:0.0}");
            sb.AppendLine($"Max party size                   : {CalculatedMaxPartySize:0}");
            sb.AppendLine("Heroes per bandit clan            :");
            foreach (KeyValuePair<string, int> kvp in militiaLeadersPerClan)
                sb.AppendLine($" - {kvp.Key}: {kvp.Value}");

            sb.AppendLine("Heroes waiting for reuse per clan :");
            foreach (KeyValuePair<string, int> kvp in waitingForReusePerClan)
                sb.AppendLine($" - {kvp.Key}: {kvp.Value}");

            return sb.ToString();
        }
        
        [CommandLineFunctionality.CommandLineArgumentFunction("heroes", "bmr")]
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
        
        [CommandLineFunctionality.CommandLineArgumentFunction("orphans", "bmr")]
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
        
        [CommandLineFunctionality.CommandLineArgumentFunction("fix_orphans", "bmr")]
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
}