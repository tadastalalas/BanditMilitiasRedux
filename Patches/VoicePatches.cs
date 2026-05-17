using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanditMilitias.Patches
{
    internal sealed class VoicePatches
    {
        internal static void ApplyManualPatch(Harmony harmony)
        {
            var original = AccessTools.Method(
                typeof(DefaultVoiceOverModel),
                nameof(DefaultVoiceOverModel.GetSoundPathForCharacter));

            if (original is null)
                return;

            harmony.Patch(original, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(VoicePatches), nameof(GenderVoicePostfix)))
            {
                after = new[] { "BanditVoiceFix" }
            });
        }

        internal static void GenderVoicePostfix(CharacterObject character, ref string __result)
        {
            if (string.IsNullOrEmpty(__result))
                return;

            if (character == null)
                return;

            try
            {
                if (!character.IsFemale)
                    return;

                if (character.Occupation != Occupation.Bandit)
                    return;

                __result = "";
            }
            catch (Exception) { }
        }
    }
}