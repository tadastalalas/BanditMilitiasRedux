using System;
using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia leaders use bandit voice lines; female leaders keep female lines | gate: setting:CheckVoiceGender | Postfix: replaces the path vanilla picked
[HarmonyPatch(typeof(DefaultVoiceOverModel), "GetSoundPathForCharacter")]
internal class DefaultVoiceOverModelPatch
{
    private const string FemaleVoiceMarker = "_female";
    private const string FallbackVoiceCulture = "looters";
    private const string PlatformToken = "$PLATFORM";
    private const string PlatformName = "PC";
    private const string SoundExtension = ".ogg";

    private static readonly List<string> Candidates = [];
    private static readonly char[] PathSeparators = ['\\', '/'];

    internal static void Postfix(CharacterObject character, VoiceObject voiceObject, ref string __result)
    {
        if (character is null || voiceObject?.VoicePaths is null || !character.IsBanditMilitiaCharacterObject())
            return;

        if (character.IsFemale)
        {
            // No vanilla female bandit lines exist; keep the gender guard until we ship our own.
            if (ModSettings.CheckVoiceGender && __result?.Contains(FemaleVoiceMarker) != true)
                __result = "";
            return;
        }

        string? culture = character.HeroObject?.Clan?.Culture?.StringId;
        Candidates.Clear();
        if (culture is not null)
            AddBanditVoiceMatches(voiceObject.VoicePaths, culture, Candidates);
        if (Candidates.Count == 0 && culture != FallbackVoiceCulture)
            AddBanditVoiceMatches(voiceObject.VoicePaths, FallbackVoiceCulture, Candidates);
        if (Candidates.Count == 0)
            return;

        string path = Candidates[character.HeroObject?.RandomInt(Candidates.Count) ?? 0].Replace(PlatformToken, PlatformName);
        __result = path.EndsWith(SoundExtension, StringComparison.OrdinalIgnoreCase) ? path : path + SoundExtension;
    }

    // Legacy bandit lines: "{culture}_NN_token", e.g. "looters_01_greeting".
    private static void AddBanditVoiceMatches(MBReadOnlyList<string> paths, string culture, List<string> result)
    {
        string prefix = culture + "_";
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            int start = path.LastIndexOfAny(PathSeparators) + 1;
            if (string.Compare(path, start, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;
            int p = start + prefix.Length;
            if (p + 3 > path.Length || !char.IsDigit(path[p]) || !char.IsDigit(path[p + 1]) || path[p + 2] != '_')
                continue;
            result.Add(path);
        }
    }
}
