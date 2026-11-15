using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Diagnostics;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militias may upgrade troops into any target (vanilla blocks bandit parties) | gate: always | Transpiler: the bandit-culture check is inline in the vanilla loop
[HarmonyPatch(typeof(PartyUpgraderCampaignBehavior), "GetPossibleUpgradeTargets")]
internal class PartyUpgraderCampaignBehaviorPatch
{
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        try
        {
            List<CodeInstruction> codes = instructions.ToList();
            Label jumpLabel = new();
            MethodInfo method = AccessTools.Method(typeof(PartyUpgraderCampaignBehaviorPatch), nameof(IsBanditMilitia));
            int index;
            for (index = codes.Count - 1; index >= 0; index--)
            {
                if (codes[index].opcode == OpCodes.Call
                    && codes[index + 1].opcode == OpCodes.Callvirt
                    && codes[index + 2].opcode == OpCodes.Callvirt
                    && codes[index + 3].opcode == OpCodes.Ldarg_1)
                {
                    break;
                }
            }

            for (; index >= 0; index--)
            {
                if (codes[index].labels.Count <= 0) continue;
                jumpLabel = codes[index].labels[0];
                break;
            }

            if (jumpLabel.GetHashCode() == 0)
                throw new Exception("Could not find jumpLabel");

            List<CodeInstruction> stack =
            [
                new(OpCodes.Ldarg_1),
                new(OpCodes.Call, method),
                new(OpCodes.Brtrue_S, jumpLabel)
            ];

            int insertion = 0;
            for (; index >= 0; index--)
            {
                if (codes[index].opcode == OpCodes.Ldarg_1
                    && codes[index + 1].opcode == OpCodes.Callvirt
                    && codes[index + 2].opcode == OpCodes.Callvirt
                    && codes[index + 3].opcode == OpCodes.Brfalse_S)
                {
                    insertion = index;
                    codes[index].MoveLabelsTo(stack[0]);
                    break;
                }
            }

            if (insertion == 0)
                throw new Exception("Could not find insertion point.");

            codes.InsertRange(insertion, stack);
            return codes.AsEnumerable();
        }
        catch (Exception e)
        {
            BmrLog.Error($"BMR-TRANSPILER: GetPossibleUpgradeTargets patch failed, vanilla kept: {e.Message}");
            return instructions;
        }
    }

    private static bool IsBanditMilitia(PartyBase party) => party.MobileParty?.IsBanditMilitiaParty() == true;
}
