using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Helpers
{
    internal static class BountyService
    {
        internal static void GrantDefeatBounty(Hero defeatedLeader, float militiaStrength)
        {
            if (defeatedLeader is null || Hero.MainHero is null)
                return;

            var t = militiaStrength <= 0f
                ? 0f
                : Math.Min(1f, militiaStrength / Globals.BountyStrengthForMaxReward);

            var gold = (int)(Globals.BountyGoldMin + t * (Globals.BountyGoldMax - Globals.BountyGoldMin));
            var renown = (int)Math.Round(Globals.BountyRenownMin + t * (Globals.BountyRenownMax - Globals.BountyRenownMin));
            if (renown < Globals.BountyRenownMin)
                renown = Globals.BountyRenownMin;

            GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, disableNotification: true);
            if (renown > 0)
                GainRenownAction.Apply(Hero.MainHero, renown);

            var msg = new TextObject("{=BMRBounty}You collected a bounty of {GOLD} denars and {RENOWN} renown for {LEADER}.");
            msg.SetTextVariable("GOLD", gold);
            msg.SetTextVariable("RENOWN", renown);
            msg.SetTextVariable("LEADER", defeatedLeader.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
        }
    }
}