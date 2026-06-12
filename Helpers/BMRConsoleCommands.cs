using System.Text;
using TaleWorlds.Library;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Helpers
{
    public static class BMRConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("stats", "bmr")]
        public static string PrintStats(System.Collections.Generic.List<string> args)
        {
            var bms = PowerCalculationService.GetCachedBMs(true);
            int partyCount = bms.Count;
            int totalTroops = 0;
            int leaderless = 0;
            for (int i = 0; i < partyCount; i++)
            {
                var mp = bms[i]?.MobileParty;
                if (mp is null) continue;
                totalTroops += mp.MemberRoster.TotalManCount;
                if (bms[i].Leader is null) leaderless++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== BanditMilitiasRedux ===");
            sb.AppendLine($"Active militias      : {partyCount}");
            sb.AppendLine($"Leaderless militias  : {leaderless}");
            sb.AppendLine($"Total troops         : {totalTroops}");
            sb.AppendLine($"Tracked BM heroes    : {Heroes.Count}");
            sb.AppendLine($"Global militia power : {MilitiaPowerPercent:0.0}% of non-militia");
            sb.AppendLine($"Avg party power      : {MilitiaPartyAveragePower:0.0}");
            sb.AppendLine($"Max party size       : {CalculatedMaxPartySize:0}");
            return sb.ToString();
        }
    }
}