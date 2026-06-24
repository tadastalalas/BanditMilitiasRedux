using System.Collections.Generic;
using System.Text;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using static BanditMilitiasRedux.Globals;

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
            const int leaderless = 0;

            for (int i = 0; i < partyCount; i++)
            {
                MobileParty? mobileParty = bms[i].MobileParty;
                if (mobileParty is null)
                {
                    continue;
                }

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
                {
                    continue;
                }

                banditClanCount++;

                int leaderCount = 0;
                IReadOnlyList<Hero> heroes = clan.Heroes;
                for (int h = 0; h < heroes.Count; h++)
                {
                    Hero hero = heroes[h];
                    if (hero.IsBanditMilitiaHero())
                    {
                        leaderCount++;
                    }
                }

                string clanName = clan.Name?.ToString() ?? clan.StringId ?? "UnknownClan";
                militiaLeadersPerClan[clanName] = leaderCount;
            }

            int imprisonedInSettlements = 0;
            int imprisonedInAiLordParties = 0;
            IReadOnlyList<Hero> allBanditMilitiaHeroes = AllAliveBanditMilitiaHeroes;
            for (int i = 0; i < allBanditMilitiaHeroes.Count; i++)
            {
                Hero hero = allBanditMilitiaHeroes[i];
                if (!hero.IsBanditMilitiaHero() || !hero.IsPrisoner)
                {
                    continue;
                }

                if (hero.CurrentSettlement is not null)
                {
                    imprisonedInSettlements++;
                    continue;
                }

                PartyBase? captorParty = hero.PartyBelongedToAsPrisoner;
                if (captorParty?.MobileParty?.IsLordParty == true)
                {
                    imprisonedInAiLordParties++;
                }
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
            sb.AppendLine("=== BanditMilitiasRedux ===");
            sb.AppendLine($"Active militias      : {partyCount}");
            sb.AppendLine($"Leaderless militias  : {leaderless}");
            sb.AppendLine($"Total troops         : {totalTroops}");
            sb.AppendLine($"Militia prisoners    : {militiaPrisonerCount}");
            sb.AppendLine($"All BM heroes        : {AllAliveBanditMilitiaHeroes.Count}");
            sb.AppendLine($"BM heroes waiting for reuse (total): {waitingForReuseTotal}");
            sb.AppendLine($"Bandit clans in world: {banditClanCount}");
            sb.AppendLine($"Global militia power : {MilitiaPowerPercent:0.0}% of non-militia");
            sb.AppendLine($"Avg party power      : {MilitiaPartyAveragePower:0.0}");
            sb.AppendLine($"Max party size       : {CalculatedMaxPartySize:0}");
            sb.AppendLine($"BM leaders imprisoned in settlements : {imprisonedInSettlements}");
            sb.AppendLine($"BM leaders imprisoned in AI lord parties : {imprisonedInAiLordParties}");
            sb.AppendLine("BM leaders per bandit clan:");
            foreach (KeyValuePair<string, int> kvp in militiaLeadersPerClan)
            {
                sb.AppendLine($" - {kvp.Key}: {kvp.Value}");
            }

            sb.AppendLine("BM heroes waiting for reuse per clan:");
            foreach (KeyValuePair<string, int> kvp in waitingForReusePerClan)
            {
                sb.AppendLine($" - {kvp.Key}: {kvp.Value}");
            }

            return sb.ToString();
        }
    }
}