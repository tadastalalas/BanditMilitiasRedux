using BanditMilitiasRedux.Core;
using HarmonyLib;
using SandBox.ViewModelCollection.Nameplate;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia party names on the map nameplate | gate: setting:BanditMilitiaString | Prefix + Postfix: Prefix records whether the party is a militia, Postfix rewrites the name vanilla built
[HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
internal class PartyNameplateVMPatch
{
    internal static void Prefix(PartyNameplateVM __instance, out bool __state)
    {
        __state = __instance?.Party is not null && __instance.Party.IsBandit && __instance.Party.IsBanditMilitiaParty();
    }

    internal static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind, bool __state)
    {
        if (!__state || __instance?.Party is null)
            return;

        string? desired = __instance.Party.Name?.ToString();
        if (!string.IsNullOrEmpty(desired) && ____fullNameBind != desired)
            ____fullNameBind = desired!;
    }
}
