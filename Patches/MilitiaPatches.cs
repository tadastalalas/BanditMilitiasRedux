using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using Microsoft.Extensions.Logging;
using SandBox.View.Map;
using SandBox.View.Map.Visuals;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.Tracker;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.InputSystem;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Patches
{
    internal sealed class MilitiaPatches
    {
        private static readonly AccessTools.FieldRef<MobilePartyAi, MobileParty> getMobileParty =
            AccessTools.FieldRefAccess<MobilePartyAi, MobileParty>("_mobileParty");

        [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter))]
        public static class MobilePartyOnPartyInteraction
        {
            public static bool Prefix(PartyBase attackerParty, PartyBase defenderParty)
            {
                var a = attackerParty?.MobileParty;
                var d = defenderParty?.MobileParty;

                if (PartyBase.MainParty == attackerParty || PartyBase.MainParty == defenderParty ||
                    a?.IsBandit != true || d?.IsBandit != true ||
                    a?.IsActive != true || d?.IsActive != true ||
                    FactionManager.IsAtWarAgainstFaction(attackerParty.MapFaction, defenderParty.MapFaction) ||
                    a.IsUsedByAQuest() || d.IsUsedByAQuest())
                    return true;

                if (MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty
                    && (MobileParty.MainParty.ShortTermTargetParty == a
                        || MobileParty.MainParty.ShortTermTargetParty == d))
                    return true;

                MilitiaPartyFactory.TryMergeParties(a, d);
                return false;
            }
        }

        [HarmonyPatch(typeof(MobilePartyVisual), "AddCharacterToPartyIcon")]
        public static class PartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (Globals.Settings.RandomBanners &&
                    characterObject.HeroObject?.PartyBelongedTo?.IsBM() == true)
                {
                    var component = characterObject.HeroObject.PartyBelongedTo.PartyComponent as ModBanditMilitiaPartyComponent;
                    if (component?.BannerKey is not null)
                    {
                        bannerKey = component.BannerKey;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PartyBase), "Banner", MethodType.Getter)]
        public static class PartyBaseBannerPatch
        {
            private static void Postfix(PartyBase __instance, ref Banner __result)
            {
                if (__instance.IsMobile && __instance.MobileParty.IsBM())
                {
                    var bmBanner = __instance.MobileParty.GetBM()?.Banner;
                    if (bmBanner is not null)
                        __result = bmBanner;
                }
            }
        }

        [HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
        public static class PartyGroupAgentOriginBannerGetterPatch
        {
            private static void Postfix(IAgentOriginBase __instance, ref Banner __result)
            {
                var party = (PartyBase)__instance.BattleCombatant;
                if (Globals.Settings.RandomBanners &&
                    party.IsMobile &&
                    party.MobileParty.IsBM())
                {
                    var bmBanner = party.MobileParty?.GetBM()?.Banner;
                    if (bmBanner is not null)
                    {
                        __result = bmBanner;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public static class EnterSettlementActionApplyForPartyPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty?.IsBM() != true)
                    return true;

                mobileParty.SetMoveModeHold();
                return false;
            }
        }

        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public static class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            public static void Prefix(PartyNameplateVM __instance, out bool __state)
            {
                __state = __instance.Party is not null
                          && __instance.Party.IsBandit
                          && __instance.Party.IsBM();
            }

            public static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind, bool __state)
            {
                if (!__state) return;

                var desired = __instance.Party.Name?.ToString();
                if (!string.IsNullOrEmpty(desired) && ____fullNameBind != desired)
                    ____fullNameBind = desired;
            }
        }

        [HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
        public static class PlayerEncounterDoMeetingInternalPatch
        {
            public static bool Prefix(PartyBase ____encounteredParty)
            {
                if (Globals.Settings.SkipConversations && ____encounteredParty.MobileParty.IsBM())
                {
                    GameMenu.SwitchToMenu("encounter");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ConversationManager), "GetSentenceMatch")]
        public static class ConversationManagerGetSentenceMatchPatch
        {
            public static bool Prefix(int sentenceIndex, bool onlyPlayer, List<ConversationSentence> ____sentences, ref bool __result)
            {
                if (Hero.OneToOneConversationHero?.IsBM() == true)
                {
                    var sentence = ____sentences[sentenceIndex];
                    if (Globals.LordConversationTokens.Contains(sentence.InputToken) ||
                        Globals.LordConversationTokens.Contains(sentence.OutputToken))
                    {
                        __result = false;
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerEncounter), "DoCaptureHeroes")]
        public static class PlayerEncounterDoCaptureHeroesPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                CodeMatcher target = codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        CodeMatch.LoadsField(AccessTools.Field(typeof(PlayerEncounter), "_capturedHeroes")),
                        CodeMatch.Calls(AccessTools.Method(typeof(TroopRosterElement), "get_Count")),
                        CodeMatch.LoadsConstant(),
                        CodeMatch.Branches()
                    ).ThrowIfInvalid("Could not find the target at DoCaptureHeroes");

                CodeInstruction[] insertion =
                [
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_capturedHeroes"),
                    CodeInstruction.LoadLocal(0),
                    CodeInstruction.Call(typeof(PlayerEncounterDoCaptureHeroesPatch), nameof(UnCaptureBMHeroes))
                ];
                
                target.Instruction.MoveLabelsTo(insertion[0]);
                target.Insert(insertion);
                
                return codeMatcher.Instructions();
            }

            internal static void UnCaptureBMHeroes(List<TroopRosterElement> capturedHeroes, TroopRoster receivingLootShare)
            {
                foreach (TroopRosterElement element in capturedHeroes.WhereQ(t => t.Character.IsBM()).ToArrayQ())
                {
                    receivingLootShare.AddToCounts(element.Character, element.Number, true, element.WoundedNumber, element.Xp);
                    capturedHeroes.Remove(element);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEncounter), "DoFreeOrCapturePrisonerHeroes")]
        public static class PlayerEncounterDoFreeHeroesPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                CodeMatcher target = codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        CodeMatch.LoadsField(AccessTools.Field(typeof(PlayerEncounter), "_capturedAlreadyPrisonerHeroes")),
                        new CodeMatch(OpCodes.Ldsfld),
                        new CodeMatch(OpCodes.Dup),
                        CodeMatch.Branches()
                    ).ThrowIfInvalid("Could not find the target at DoFreeOrCapturePrisonerHeroes");

                CodeInstruction[] insertion =
                [
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_capturedAlreadyPrisonerHeroes"),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_mapEvent"),
                    CodeInstruction.Call(typeof(PlayerEncounterDoFreeHeroesPatch), nameof(UnFreeBMHeroes))
                ];

                target.Instruction.MoveLabelsTo(insertion[0]);
                target.Insert(insertion);

                return codeMatcher.Instructions();
            }

            private static void UnFreeBMHeroes(List<TroopRosterElement> freedHeroes, MapEvent mapEvent)
            {
                var bmHeroes = freedHeroes.WhereQ(t => t.Character.IsBM()).ToArrayQ();
                if (bmHeroes.Length == 0) return;
                MethodInfo GetPrisonerRosterReceivingLootShare = AccessTools.Method(typeof(MapEvent), "GetPrisonerRosterReceivingLootShare");
                var receivingLootShare = (TroopRoster)GetPrisonerRosterReceivingLootShare.Invoke(mapEvent, [PartyBase.MainParty]);
                foreach (TroopRosterElement element in bmHeroes)
                {
                    if (element.Character.HeroObject.MapFaction?.IsAtWarWith(Hero.MainHero.MapFaction) == true)
                    {
                        var prisonParty = element.Character.HeroObject.PartyBelongedToAsPrisoner;
                        if (prisonParty is not null)
                        {
                            prisonParty.PrisonRoster.RemoveTroop(element.Character);
                            receivingLootShare.AddToCounts(element.Character, 1, true, element.WoundedNumber, element.Xp, false);
                        }
                    }
                    freedHeroes.Remove(element);
                }
            }
        }

        [HarmonyPatch(typeof(PartyBase), nameof(PartyBase.AddPrisoner))]
        public static class PartyBaseAddPrisonerPatch
        {
            public static bool Prefix(PartyBase __instance, CharacterObject element)
            {
                if (__instance != PartyBase.MainParty || !element.IsBM()) return true;
                return !__instance.PrisonerHeroes.Contains(element);
            }
        }

        [HarmonyPatch(typeof(DefaultVoiceOverModel), nameof(DefaultVoiceOverModel.GetSoundPathForCharacter))]
        public static class DefaultVoiceOverModelGetSoundPathForCharacterPatch
        {
            public static void Postfix(CharacterObject character, VoiceObject voiceObject, ref string __result)
            {
                if (!Globals.Settings.CheckVoiceGender)
                    return;
                
                if (character.IsBM() && character.IsFemale)
                {
                    if (!__result.Contains("_female"))
                        __result = "";
                }
            }
        }

        [HarmonyPatch(typeof(MobilePartyAi), "SetAiBehavior")]
        public static class MobilePartyCanAttackPatch
        {
            public static bool Prefix(AiBehavior newAiBehavior, IInteractablePoint interactablePoint, MobilePartyAi __instance)
            {
                if (newAiBehavior != AiBehavior.EngageParty)
                    return true;

                MobileParty attacker = getMobileParty(__instance);
                var bm = attacker.GetBM();
                if (bm is null) return true;

                PartyBase targetPartyBase = interactablePoint as PartyBase;
                MobileParty targetParty = targetPartyBase?.MobileParty;
                if (targetParty is null || !targetPartyBase.IsMobile)
                    return true;

                if (Globals.Settings.IgnoreVillagersCaravans
                    && (targetParty.IsCaravan || targetParty.IsVillager))
                    return false;

                if (targetParty.LeaderHero is not null
                    && bm.Avoidance.TryGetValue(targetParty.LeaderHero, out var heroAvoidance)
                    && MBRandom.RandomFloat * 100f < heroAvoidance)
                    return false;

                var party1Strength = attacker.GetTotalLandStrengthWithFollowers();
                var party2Strength = targetParty.GetTotalLandStrengthWithFollowers();

                if (party1Strength <= 0f)
                    return true;

                var stronger = Math.Max(party1Strength, party2Strength);
                var weaker = Math.Min(party1Strength, party2Strength);
                var deltaPercent = (stronger - weaker) / party1Strength * 100f;

                var cap = party2Strength > party1Strength
                    ? Globals.Settings.MaxStrongerTargetPercent
                    : Globals.Settings.MaxWeakerTargetPercent;

                return cap >= 100 || deltaPercent <= cap;
            }
        }

        [HarmonyPatch(typeof(MapTrackerItemVM), "UpdateProperties")]
        public static class MobilePartyTrackItemVMUpdatePropertiesPatch
        {
            public static void Postfix(MapTrackerItemVM __instance, ref BannerImageIdentifierVM ____factionVisualBind)
            {
                if (__instance.TrackedObject is not MobileParty party || !party.IsBM())
                    return;

                if (!PartyImageMap.TryGetValue(party, out var image))
                {
                    image = new BannerImageIdentifierVM(party.GetBM().Banner);
                    PartyImageMap.Add(party, image);
                }

                ____factionVisualBind = image;
            }
        }

        [HarmonyPatch(typeof(AiLandBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        [HarmonyPatch(typeof(NameGenerator), "GenerateHeroFullName")]
        public static class NameGeneratorGenerateHeroName
        {
            public static void Postfix(Hero hero, TextObject heroFirstName, ref TextObject __result)
            {
                if (hero.CharacterObject.Occupation is not Occupation.Bandit
                    || (hero.PartyBelongedTo is not null
                        && !hero.PartyBelongedTo.IsBM()))
                    return;

                var textObject = heroFirstName;
                var index = (int)AccessTools.Method(typeof(NameGenerator), "SelectNameIndex")
                    .Invoke(NameGenerator.Current, [hero, GangLeaderNames(NameGenerator.Current), 0u, false]);
                NameGenerator.Current.AddName(GangLeaderNames(NameGenerator.Current)[index]);
                textObject = GangLeaderNames(NameGenerator.Current)[index].CopyTextObject();
                textObject.SetTextVariable("FEMALE", hero.IsFemale ? 1 : 0);
                textObject.SetTextVariable("IMPERIAL", hero.Culture.StringId == "empire" ? 1 : 0);
                textObject.SetTextVariable("COASTAL", hero.Culture.StringId is "empire" or "vlandia" ? 1 : 0);
                textObject.SetTextVariable("NORTHERN", hero.Culture.StringId is "battania" or "sturgia" ? 1 : 0);
                textObject.SetTextVariable("FIRSTNAME", heroFirstName);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, textObject);
                __result = textObject;
            }
        }

        [HarmonyPatch(typeof(DefaultSkillLevelingManager), "OnPersonalSkillExercised")]
        public static class DefaultSkillLevelingManagerOnPersonalSkillExercisedPatch
        {
            public static bool Prefix(Hero hero)
            {
                if (hero is null) return true;
                
                if (hero.HeroDeveloper is null)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(MobilePartyAi), "TickInternal")]
        public class MobilePartyCalculateContinueChasingScore
        {
            public static void Prefix(MobilePartyAi __instance)
            {
                MobileParty mobileParty = getMobileParty(__instance);
                if (!mobileParty.IsBM())
                    return;

                if (mobileParty.HasNavalNavigationCapability)
                    return;

                if (mobileParty.DefaultBehavior == AiBehavior.Hold && mobileParty.TargetSettlement is null)
                    MilitiaBehaviorService.BMThink(mobileParty);
            }
        }

        [HarmonyPatch(typeof(MobileParty), "UpdatePartyComponentFlags")]
        public static class MobilePartyInitializeOnLoad
        {
            public static void Postfix(MobileParty __instance)
            {
                if (!__instance.IsBandit && __instance.IsBM())
                    IsBandit(__instance) = true;
            }
        }

        [HarmonyPatch(typeof(PartyUpgraderCampaignBehavior), "GetPossibleUpgradeTargets")]
        public static class PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargets
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                try
                {
                    var codes = instructions.ToListQ();
                    Label jumpLabel = new();
                    var method = AccessTools.Method(typeof(PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargets), nameof(IsBM));
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

                    var stack = new List<CodeInstruction>
        {
                    new(OpCodes.Ldarg_1),
                    new(OpCodes.Call, method),
                    new(OpCodes.Brtrue_S, jumpLabel)
        };

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
                catch (Exception)
                {
                    return instructions;
                }
            }

            private static bool IsBM(PartyBase party)
            {
                return party.MobileParty?.IsBM() == true;
            }
        }

        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "CanPartyUpgradeTroopToTarget")]
        public class DefaultPartyTroopUpgradeModelCanPartyUpgradeTroopToTarget
        {
            public static void Postfix(PartyBase upgradingParty, ref bool __result)
            {
                if (upgradingParty.IsMobile && upgradingParty.MobileParty.IsBM())
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
        public class MobilePartyIsBanditBossParty
        {
            public static bool Prefix(MobileParty __instance, ref bool __result)
            {
                if (__instance.IsBM())
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Hero), nameof(Hero.UpdateHomeSettlement))]
        public class HeroUpdateHomeSettlement
        {
            public static void Postfix(Hero __instance, ref Settlement ____homeSettlement, Settlement ____bornSettlement)
            {
                if (____homeSettlement is not null || ____bornSettlement is null || __instance.Clan?.IsBanditFaction != true) return;
                ____homeSettlement = ____bornSettlement;
            }
        }
        
        [HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
        public class KillCharacterActionApplyInternalPatch
        {
            public static void Postfix(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail actionDetail, bool showNotification, bool isForced = false)
            {
                if (!Heroes.Contains(victim))
                    return;
                Heroes.Remove(victim);
                CharacterRelationManager.Instance.RemoveHero(victim);
            }
        }

        [HarmonyPatch(typeof(PartyBase), "PartySizeLimit", MethodType.Getter)]
        public static class MobilePartyPartySizeLimitGetterPatch
        {
            public static void Postfix(PartyBase __instance, ref int ____cachedPartyMemberSizeLimit, ref int __result)
            {
                if (!Globals.Settings.IgnoreSizePenalty || !__instance.IsMobile || !__instance.MobileParty.IsBM()) return;
                int totalManCount = __instance.MemberRoster.TotalManCount;
                if (__result >= totalManCount) return;
                ____cachedPartyMemberSizeLimit = totalManCount;
                __result = totalManCount;
            }
        }

        [HarmonyPatch(typeof(DefaultDiplomacyModel), nameof(DefaultDiplomacyModel.GetHeroesForEffectiveRelation))]
        public static class DefaultDiplomacyModelGetHeroesForEffectiveRelationPatch
        {
            public static void Postfix(Hero hero1, Hero hero2, ref Hero effectiveHero1, ref Hero effectiveHero2)
            {
                if (hero1?.IsBM() == true || hero2?.IsBM() == true)
                {
                    effectiveHero1 ??= hero1;
                    effectiveHero2 ??= hero2;
                }
            }
        }

        [HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnPrisonerTaken")]
        public static class DefaultLogsCampaignBehaviorOnPrisonerTakenPatch
        {
            public static bool Prefix(PartyBase party, Hero hero)
            {
                if (Globals.Settings.RemovePrisonerMessages && party != PartyBase.MainParty && hero?.IsBM() == true)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnHeroPrisonerReleased")]
        public static class DefaultLogsCampaignBehaviorOnHeroPrisonerReleasedPatch
        {
            public static bool Prefix(PartyBase party, Hero hero)
            {
                if (Globals.Settings.RemovePrisonerMessages && party != PartyBase.MainParty && hero?.IsBM() == true)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    Nuke();
            }

            public static void Postfix()
            {
                InitMap();
                RemoveBadItems();
            }
        }

        [HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
        public static class MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        internal static void PatchSaSDeserters(ref MobileParty __result)
        {
            Traverse.Create(__result).Field<bool>("IsCurrentlyUsedByAQuest").Value = true;
        }

        [HarmonyPatch(typeof(DefaultSkillLevelingManager), "OnAIPartyLootCasualties")]
        public static class OnAIPartyLootCasualtiesPatch
        {
            public static bool Prefix(Hero winnerPartyLeader, PartyBase defeatedParty)
            {
                if (winnerPartyLeader?.PartyBelongedTo?.IsBM() == true
                    || defeatedParty?.MobileParty?.IsBM() == true)
                {
                    if (winnerPartyLeader is null || winnerPartyLeader.IsDead) return false;
                    if (defeatedParty is null || (!defeatedParty.IsSettlement && defeatedParty.MobileParty is null)) return false;
                }
                return true;
            }
        }

        /*
        Removed because:

        In plain English: "If this troop has no upgrade path, kick it out of the roster and say it can't gain XP."
        That's a problem because top?tier troops (Khan's Guard, Banner Knight, Imperial Cataphract, etc.) legally have no upgrade targets. Vanilla returns false for them (they can't be upgraded ? they're already max), but vanilla does not delete them. This patch does.
        So if a Bandit Militia recruits / merges in a tier 6 unit, this code throws it away.
        If your goal is "BMs can keep top?tier troops": ? Just delete the whole patch. The vanilla method already handles "no upgrade targets" correctly ? it just returns false for XP gain, which is harmless.
        Why does the patch exist at all? It looks like a leftover defensive measure from when the mod was generating broken cloned troop objects with UpgradeTargets == null (bad/corrupt entries). That problem is better handled where the troops are created, not by silently nuking them everywhere.
        Recommendation: Delete the patch. If you ever see broken troops, fix the creator (in MilitiaPartyFactory / EquipmentPool), not this getter.

        [HarmonyPatch(typeof(MobilePartyHelper), nameof(MobilePartyHelper.CanTroopGainXp))]
        public static class MobilePartyHelperCanTroopGainXpPatch
        {
            public static bool Prefix(PartyBase owner, CharacterObject character, ref bool __result)
            {
                if (character?.UpgradeTargets is not null)
                    return true;

                if (owner?.IsMobile == true && owner.MobileParty.IsBM()
                    && character is not null
                    && owner.MemberRoster?.Contains(character) == true)
                {
                    owner.MemberRoster.RemoveTroop(character);
                }

                __result = false;
                return false;
            }
        }
        */
    }
}