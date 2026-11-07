using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Helpers
{
    // BMR-FEATURE: Serve as Soldier compat, BMR leaves alone the bandit party SaS controls for a bait run | gate: always (SaS installed)
    internal static class SaSCompat
    {
        private const string ApiTypeName = "ServeAsSoldier.Compat.SaSPublicApi";
        private const string IsPartyReservedMethodName = "IsPartyReserved";
        private const string LogTag = "BMR-SAS-COMPAT";

        private static Func<MobileParty, bool>? _isPartyReserved;
        private static bool _resolved;

        internal static bool IsReservedBySaS(MobileParty? party)
        {
            if (party is null)
                return false;

            if (!_resolved)
                Resolve();

            return _isPartyReserved is not null && _isPartyReserved(party);
        }

        private static void Resolve()
        {
            _resolved = true;

            // SaS is optional and not referenced at build time.
#pragma warning disable BHA0003
            Type? apiType = AccessTools.TypeByName(ApiTypeName);
#pragma warning restore BHA0003
            MethodInfo? method = apiType is null ? null : AccessTools.Method(apiType, IsPartyReservedMethodName, [typeof(MobileParty)]);
            if (method is { IsStatic: true } && method.ReturnType == typeof(bool))
                _isPartyReserved = AccessTools.MethodDelegate<Func<MobileParty, bool>>(method);

            Logs.WriteToFile(LogTag, _isPartyReserved is not null
                ? $"Found {ApiTypeName}.{IsPartyReservedMethodName}: the SaS bait-run party is left alone."
                : $"{ApiTypeName}.{IsPartyReservedMethodName} not found (SaS not installed or older version): compat off.");
        }
    }
}
