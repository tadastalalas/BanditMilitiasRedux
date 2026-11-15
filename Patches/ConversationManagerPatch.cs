using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Lord conversation lines are hidden for militia leaders | gate: always | Prefix: rejects the sentence before vanilla matches it
[HarmonyPatch(typeof(ConversationManager), "GetSentenceMatch")]
internal class ConversationManagerPatch
{
    internal static bool Prefix(int sentenceIndex, bool onlyPlayer, List<ConversationSentence> ____sentences, ref bool __result)
    {
        if (Hero.OneToOneConversationHero?.IsBanditMilitiaHero() != true)
            return true;

        if (LordConversationTokens is null || ____sentences is null)
            return true;

        ConversationSentence sentence = ____sentences[sentenceIndex];
        if (!LordConversationTokens.Contains(sentence.InputToken) && !LordConversationTokens.Contains(sentence.OutputToken))
            return true;

        __result = false;
        return false;
    }
}
