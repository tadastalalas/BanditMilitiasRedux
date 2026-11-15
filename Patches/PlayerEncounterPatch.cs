using System.Collections.Generic;
using System.Reflection.Emit;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.LinQuick;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Skip militia conversations on encounter | gate: setting:SkipConversations | Prefix: skips the vanilla meeting and opens the encounter menu
[HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
internal class PlayerEncounterDoMeetingInternalPatch
{
    private const string EncounterMenuId = "encounter";

    internal static bool Prefix(PartyBase ____encounteredParty)
    {
        if (!ModSettings.SkipConversations || ____encounteredParty?.MobileParty?.IsBanditMilitiaParty() != true)
            return true;

        GameMenu.SwitchToMenu(EncounterMenuId);
        return false;
    }
}

// BMR-FEATURE: Militia heroes are not freed by the vanilla prisoner release | gate: always | Transpiler: the freed list is a vanilla local step with no hook
[HarmonyPatch(typeof(PlayerEncounter), "DoFreeOrCapturePrisonerHeroes")]
internal class PlayerEncounterDoFreeOrCapturePrisonerHeroesPatch
{
    private const string CapturedAlreadyPrisonerHeroesField = "_capturedAlreadyPrisonerHeroes";

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        CodeMatcher codeMatcher = new(instructions);

        CodeMatcher target = codeMatcher
            .MatchStartForward(
                new CodeMatch(OpCodes.Ldarg_0),
                CodeMatch.LoadsField(AccessTools.Field("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter:" + CapturedAlreadyPrisonerHeroesField)),
                new CodeMatch(OpCodes.Ldsfld),
                new CodeMatch(OpCodes.Dup),
                CodeMatch.Branches()
            ).ThrowIfInvalid("Could not find the target at DoFreeOrCapturePrisonerHeroes");

        CodeInstruction[] insertion =
        [
            CodeInstruction.LoadArgument(0),
            CodeInstruction.LoadField(typeof(PlayerEncounter), CapturedAlreadyPrisonerHeroesField),
            CodeInstruction.Call(typeof(PlayerEncounterDoFreeOrCapturePrisonerHeroesPatch), nameof(UnFreeBanditMilitiaHeroes))
        ];

        target.Instruction.MoveLabelsTo(insertion[0]);
        target.Insert(insertion);

        return codeMatcher.Instructions();
    }

    private static void UnFreeBanditMilitiaHeroes(List<TroopRosterElement> freedHeroes)
    {
        if (freedHeroes is null)
            return;

        TroopRosterElement[] militiaHeroes = freedHeroes.WhereQ(t => t.Character.IsBanditMilitiaCharacterObject()).ToArrayQ();
        if (militiaHeroes.Length == 0)
            return;

        foreach (TroopRosterElement element in militiaHeroes)
        {
            Hero? hero = element.Character.HeroObject;
            if (hero is { IsAlive: true })
            {
                TakePrisonerAction.Apply(PartyBase.MainParty, hero);
                hero.IsKnownToPlayer = true;
            }

            freedHeroes.Remove(element);
        }
    }
}
