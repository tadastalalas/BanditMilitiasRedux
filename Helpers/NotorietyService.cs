using System;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Helpers
{
    internal static class NotorietyService
    {
        internal static int GetTier(int battlesWon, CampaignTime firstSeen)
        {
            var winTier = CountReached(Globals.NotorietyWinThresholds, battlesWon);
            var daysAlive = (int)(CampaignTime.Now - firstSeen).ToDays;
            var ageTier = CountReached(Globals.NotorietyDayThresholds, daysAlive);
            return winTier > ageTier ? winTier : ageTier;
        }

        private static int CountReached(int[] thresholds, int value)
        {
            var tier = 0;
            for (var i = 0; i < thresholds.Length; i++)
            {
                if (value >= thresholds[i]) tier = i + 1;
                else break;
            }
            return tier;
        }

        internal static string GetEpithet(int tier)
        {
            if (tier <= 0) return null;
            var epithets = Globals.NotorietyEpithets;
            var index = tier - 1;
            return index < epithets.Length ? epithets[index] : epithets[epithets.Length - 1];
        }

        internal static Hero GetMostFearedHunter(Hero leader, out float value)
        {
            value = 0f;
            var bm = leader?.PartyBelongedTo?.GetBanditMilitiaParty();
            if (bm?.Avoidance is null) return null;

            Hero best = null;
            var max = 0f;
            foreach (var pair in bm.Avoidance)
            {
                if (pair.Key is not null && pair.Value > max)
                {
                    max = pair.Value;
                    best = pair.Key;
                }
            }
            value = max;
            return best;
        }

        internal static TextObject BuildEncyclopediaText(Hero hero)
        {
            StringHelpers.SetCharacterProperties("LEADER", hero.CharacterObject, null, false);

            var battlesWon = NotorietyBehavior.Instance?.GetBattlesWon(hero) ?? 0;
            var tier = NotorietyBehavior.Instance?.GetTier(hero) ?? 0;
            var epithet = GetEpithet(tier);

            var result = new TextObject(
                "{=BMREncBase}{LEADER.FIRSTNAME} is a brigand chief leading a band of cutthroats that raids under the name of the {CLAN_NAME}.");
            result.SetTextVariable("CLAN_NAME",
                hero.Clan?.Name ?? new TextObject("{=BMREncNoClan}masterless brigands"));

            if (epithet is not null)
            {
                var note = battlesWon > 0
                    ? new TextObject("{=BMREncEpithet1} Across the land {?LEADER.GENDER}she{?}he{\\?} is spoken of as {LEADER.FIRSTNAME} {EPITHET}, victor of {WINS} {?MANY}battles{?}battle{\\?}.")
                    : new TextObject("{=BMREncEpithet2} Across the land {?LEADER.GENDER}she{?}he{\\?} is spoken of as {LEADER.FIRSTNAME} {EPITHET}.");
                note.SetTextVariable("EPITHET", epithet);
                if (battlesWon > 0)
                {
                    note.SetTextVariable("WINS", battlesWon);
                    note.SetTextVariable("MANY", battlesWon > 1 ? 1 : 0);
                }
                result = Concat(result, note);
            }

            var defeats = NotorietyBehavior.Instance?.GetDefeats(hero) ?? 0;
            if (defeats > 0)
            {
                var defeatNote = new TextObject("{=BMREncDefeats} {?LEADER.GENDER}She{?}He{\\?} has been beaten in battle {DEFEATS} {?MANY}times{?}time{\\?}.");
                
                defeatNote.SetTextVariable("DEFEATS", defeats);
                defeatNote.SetTextVariable("MANY", defeats > 1 ? 1 : 0);
                result = Concat(result, defeatNote);
            }

            Hero? feared = null;
            feared = GetMostFearedHunter(hero, out var avoidance);

            if (!(avoidance >= Globals.NotorietyFearMentionThreshold))
                return result;
            
            var fearNote = new TextObject("{=BMREncFear} Above all others, {?LEADER.GENDER}she{?}he{\\?} fears {HUNTER}.");
            
            fearNote.SetTextVariable("HUNTER", feared.Name);
            result = Concat(result, fearNote);

            return result;
        }

        private static TextObject Concat(TextObject a, TextObject b)
        {
            var t = new TextObject("{=!}{A}{B}");
            t.SetTextVariable("A", a);
            t.SetTextVariable("B", b);
            return t;
        }
    }
}