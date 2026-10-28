using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Helpers;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.View.Map.Visuals;
using SandBox.ViewModelCollection.Nameplate;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Encyclopedia;
using TaleWorlds.CampaignSystem.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.Tracker;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitiasRedux.Globals;
using static BanditMilitiasRedux.Helpers.Helper;

namespace BanditMilitiasRedux.Patches
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedType.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    internal sealed class MilitiaPatches
    {
        private static readonly AccessTools.FieldRef<MobilePartyAi, MobileParty> MobilePartyFieldRef =
            AccessTools.FieldRefAccess<MobilePartyAi, MobileParty>("_mobileParty");

        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Postfix()
            {
                if (IsGlobalModStateInitialized)
                    return;
                
                InitializeGlobalModState();
            }
        }

        [HarmonyPatch(typeof(EncounterManager), "StartPartyEncounter")]
        public static class EncounterManagerStartPartyEncounterPatch
        {
            public static bool Prefix(PartyBase attackerParty, PartyBase defenderParty)
            {
                if (!BanditMilitiaManager.CanMobilePartiesMerge(attackerParty.MobileParty, defenderParty.MobileParty))
                    return true;

                if (BanditMilitiaManager.IfEngagedByThePlayer(attackerParty.MobileParty, defenderParty.MobileParty))
                    return true;
                
                BanditMilitiaManager.TryMergeBanditParties(attackerParty.MobileParty, defenderParty.MobileParty);
                return false;
            }
        }
        
        [HarmonyPatch(typeof(MobilePartyVisual), "AddCharacterToPartyIcon")]
        public static class MobilePartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (!Settings.RandomBanners || characterObject.HeroObject?.PartyBelongedTo?.IsBanditMilitiaParty() != true)
                    return;
                
                var component = characterObject.HeroObject.PartyBelongedTo.PartyComponent as BanditMilitiaPartyComponent;
                
                if (component?.BannerKey is not null)
                    bannerKey = component.BannerKey;
            }
        }
        
        [HarmonyPatch(typeof(MobilePartyVisual), "AddCharacterToPartyIcon")]
        public static class MobilePartyVisualAddCharacterToPartyIconDiagnosticPatch
        {
            private static void Prefix(PartyBase party, CharacterObject characterObject)
            {
                if (characterObject is null || !characterObject.HasMount())
                    return;

                EquipmentElement mount = characterObject.Equipment[EquipmentIndex.Horse];
                if (mount.Item?.HorseComponent?.Monster is not null)
                    return;

                Logs.WriteToFile("BMR-MOUNT-NULLCHECK",
                    $"party={party?.Name} char={characterObject.StringId} isHero={characterObject.IsHero} " +
                    $"hero={characterObject.HeroObject?.Name} isOurs={characterObject.IsBanditMilitiaCharacterObject()} " +
                    $"mountItem={mount.Item?.StringId ?? "null"}");
            }
        }

        [HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
        public static class PartyGroupAgentOriginBannerGetterPatch
        {
            private static void Postfix(IAgentOriginBase __instance, ref Banner __result)
            {
                var party = (PartyBase)__instance.BattleCombatant;
                if (!Settings.RandomBanners || !party.IsMobile || !party.MobileParty.IsBanditMilitiaParty())
                    return;
                
                var bmBanner = party.MobileParty?.GetBanditMilitiaParty()?.Banner;
                if (bmBanner is not null)
                    __result = bmBanner;
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public static class EnterSettlementActionApplyForPartyPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (!mobileParty.IsBanditMilitiaParty())
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
                __state = __instance.Party is not null && __instance.Party.IsBandit && __instance.Party.IsBanditMilitiaParty();
            }

            public static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind, bool __state)
            {
                if (!__state)
                    return;

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
                if (!Settings.SkipConversations || ____encounteredParty.MobileParty?.IsBanditMilitiaParty() != true)
                    return true;
                
                GameMenu.SwitchToMenu("encounter");
                return false;
            }
        }

        [HarmonyPatch(typeof(ConversationManager), "GetSentenceMatch")]
        public static class ConversationManagerGetSentenceMatchPatch
        {
            public static bool Prefix(int sentenceIndex, bool onlyPlayer, List<ConversationSentence> ____sentences, ref bool __result)
            {
                if (Hero.OneToOneConversationHero?.IsBanditMilitiaHero() != true)
                    return true;

                if (LordConversationTokens is null)
                    return true;
                
                var sentence = ____sentences[sentenceIndex];
                if (!LordConversationTokens.Contains(sentence.InputToken) && !LordConversationTokens.Contains(sentence.OutputToken))
                    return true;
                
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultVoiceOverModel), "GetSoundPathForCharacter")]
        public static class DefaultVoiceOverModelGetSoundPathForCharacterPatch
        {
            private static readonly List<string> Candidates = [];
            private static readonly char[] PathSeparators = ['\\', '/'];

            public static void Postfix(CharacterObject character, VoiceObject voiceObject, ref string __result)
            {
                if (character is null || voiceObject?.VoicePaths is null || !character.IsBanditMilitiaCharacterObject())
                    return;

                if (character.IsFemale)
                {
                    // No vanilla female bandit lines exist; keep the gender guard until we ship our own.
                    if (Globals.Settings.CheckVoiceGender && __result?.Contains("_female") != true)
                        __result = "";
                    return;
                }

                string? culture = character.HeroObject?.Clan?.Culture?.StringId;
                Candidates.Clear();
                if (culture is not null)
                    AddBanditVoiceMatches(voiceObject.VoicePaths, culture, Candidates);
                if (Candidates.Count == 0 && culture != "looters")
                    AddBanditVoiceMatches(voiceObject.VoicePaths, "looters", Candidates);
                if (Candidates.Count == 0)
                    return;

                string path = Candidates[character.HeroObject?.RandomInt(Candidates.Count) ?? 0].Replace("$PLATFORM", "PC");
                __result = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? path : path + ".ogg";
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

        [HarmonyPatch(typeof(MobilePartyAi), "SetAiBehavior")]
        public static class MobilePartyAiSetAiBehaviorPatch
        {
            public static bool Prefix(AiBehavior newAiBehavior, IInteractablePoint interactablePoint, MobilePartyAi __instance)
            {
                if (newAiBehavior != AiBehavior.EngageParty)
                    return true;

                MobileParty attacker = MobilePartyFieldRef(__instance);
                var bm = attacker.GetBanditMilitiaParty();
                
                if (bm is null) return true;

                PartyBase targetPartyBase = interactablePoint as PartyBase;
                MobileParty targetParty = targetPartyBase?.MobileParty;
                
                if (targetParty is null || !targetPartyBase.IsMobile)
                    return true;
                
                if (!FactionManager.IsAtWarAgainstFaction(attacker.MapFaction, targetParty.MapFaction))
                    return true;

                if (Globals.Settings.IgnoreVillagersCaravans
                    && (targetParty.IsCaravan || targetParty.IsVillager))
                    return false;

                if (targetParty.LeaderHero is not null
                    && bm.Avoidance is not null &&  bm.Avoidance.TryGetValue(targetParty.LeaderHero, out var heroAvoidance)
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
                if (__instance.TrackedObject is not MobileParty party || !party.IsBanditMilitiaParty())
                    return;

                if (!PartyImageMap.TryGetValue(party, out var image))
                {
                    image = new BannerImageIdentifierVM(party.GetBanditMilitiaParty()!.Banner);
                    PartyImageMap.Add(party, image);
                }

                ____factionVisualBind = image;
            }
        }
        
        // BMR-FEATURE: Militia map markers | gate: always | Postfix: remember the map's tracker provider so militia markers can be refreshed
        [HarmonyPatch(typeof(MapTrackerProvider), MethodType.Constructor)]
        public static class MapTrackerProviderCtorPatch
        {
            public static void Postfix(MapTrackerProvider __instance) => MilitiaTrackerManager.SetProvider(__instance);
        }

        // BMR-FEATURE: Militia map markers | gate: setting:ShowMilitiaMarkers | Postfix: vanilla never tracks bandit parties
        [HarmonyPatch(typeof(MapTrackerProvider), "CanAddMobileParty")]
        public static class MapTrackerProviderCanAddMobilePartyPatch
        {
            public static void Postfix(MobileParty party, ref bool __result)
            {
                if (!__result && party is not null && MilitiaTrackerManager.ShouldTrack(party))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(AiLandBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBanditMilitiaParty();
        }
        
        /*
        [HarmonyPatch(typeof(NameGenerator), "GenerateHeroFullName")]
        public static class NameGeneratorGenerateHeroName
        {
            private static readonly MethodInfo _selectNameIndex = AccessTools.Method(typeof(NameGenerator), "SelectNameIndex");

            public static void Postfix(Hero hero, TextObject heroFirstName, ref TextObject __result)
            {
                if (!hero.IsBM())
                    return;

                var names = GangLeaderNames(NameGenerator.Current);
                var index = (int)_selectNameIndex.Invoke(NameGenerator.Current, [hero, names, 0u, false]);
                // NameGenerator.Current.AddName(names[index]);
                // var textObject = names[index].CopyTextObject();
                NameGenerator.Current.AddName(GangLeaderNames(NameGenerator.Current)[index]);
                var textObject = GangLeaderNames(NameGenerator.Current)[index].CopyTextObject();
                textObject.SetTextVariable("FEMALE", hero.IsFemale ? 1 : 0);
                textObject.SetTextVariable("IMPERIAL", hero.Culture.StringId == "empire" ? 1 : 0);
                textObject.SetTextVariable("COASTAL", hero.Culture.StringId is "empire" or "vlandia" ? 1 : 0);
                textObject.SetTextVariable("NORTHERN", hero.Culture.StringId is "battania" or "sturgia" ? 1 : 0);
                textObject.SetTextVariable("FIRSTNAME", heroFirstName);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, textObject);
                __result = textObject;
            }
        }
        */

        [HarmonyPatch(typeof(MobilePartyAi), "TickInternal")]
        public class MobilePartyAiTickInternalPatch
        {
            public static void Prefix(MobilePartyAi __instance)
            {
                MobileParty mobileParty = MobilePartyFieldRef(__instance);
                if (!mobileParty.IsBanditMilitiaParty())
                    return;

                if (mobileParty is { DefaultBehavior: AiBehavior.Hold, TargetSettlement: null })
                    BanditMilitiaManager.Think(mobileParty);
            }
        }

        [HarmonyPatch(typeof(MobileParty), "UpdatePartyComponentFlags")]
        public static class MobilePartyUpdatePartyComponentFlagsPatch
        {
            public static void Postfix(MobileParty __instance)
            {
                if (__instance.IsBanditMilitiaParty() && !__instance.IsBandit)
                    IsBandit(__instance) = true;
            }
        }

        [HarmonyPatch(typeof(PartyUpgraderCampaignBehavior), "GetPossibleUpgradeTargets")]
        public static class PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargetsPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                try
                {
                    var codes = instructions.ToListQ();
                    Label jumpLabel = new();
                    var method = AccessTools.Method(typeof(PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargetsPatch), nameof(IsBM));
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
                catch (Exception e)
                {
                    Logs.WriteToFile("BMR-TRANSPILER", $"GetPossibleUpgradeTargets patch failed, vanilla kept: {e.Message}");
                    return instructions;
                }
            }

            private static bool IsBM(PartyBase party)
            {
                return party.MobileParty?.IsBanditMilitiaParty() == true;
            }
        }

        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "CanPartyUpgradeTroopToTarget")]
        public class DefaultPartyTroopUpgradeModelCanPartyUpgradeTroopToTargetPatch
        {
            public static void Postfix(PartyBase upgradingParty, ref bool __result)
            {
                if (upgradingParty.MobileParty.IsBanditMilitiaParty() && upgradingParty.IsMobile)
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
        public class MobilePartyIsBanditBossPartyPatch
        {
            public static bool Prefix(MobileParty __instance, ref bool __result)
            {
                if (!__instance.IsBanditMilitiaParty())
                    return true;
                
                __result = false;
                return false;
            }
        }

        // I'm not sure why we have this patch.
        [HarmonyPatch(typeof(Hero), "UpdateHomeSettlement")]
        public class HeroUpdateHomeSettlementPatch
        {
            public static void Postfix(Hero __instance, ref Settlement ____homeSettlement, Settlement ____bornSettlement)
            {
                if (!__instance.IsBanditMilitiaHero())
                    return;

                ____homeSettlement = ____bornSettlement;
            }
        }
        
        [HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
        public class KillCharacterActionApplyInternalPatch
        {
            private static readonly MethodInfo AddPlayerTraitXp = AccessTools.Method(typeof(TraitLevelingHelper), "AddPlayerTraitXPAndLogEntry");
            
            public static void Prefix(Hero victim, KillCharacterAction.KillCharacterActionDetail actionDetail)
            {
                if (victim?.IsBanditMilitiaHero() == true
                    && actionDetail is KillCharacterAction.KillCharacterActionDetail.Executed
                        or KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent)
                    SuppressedExecutionVictim = victim;
            }

            public static void Postfix(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail actionDetail, bool showNotification, bool isForced = false)
            {
                bool wasBmExecution = victim is not null && SuppressedExecutionVictim == victim;
                if (wasBmExecution)
                    SuppressedExecutionVictim = null;

                if (victim is null || victim.IsAlive || !AllAliveBanditMilitiaHeroes.Contains(victim))
                    return;

                AllAliveBanditMilitiaHeroes.Remove(victim);
                CharacterRelationManager.Instance.RemoveHero(victim);

                if (wasBmExecution && killer == Hero.MainHero && Globals.Settings.ExecutionHonorXp > 0)
                    AddPlayerTraitXp?.Invoke(null, [DefaultTraits.Honor, Globals.Settings.ExecutionHonorXp, ActionNotes.SacrificedTroops, victim]);
            }
        }

        [HarmonyPatch(typeof(ChangeRelationAction), "ApplyInternal")]
        public static class ChangeRelationActionApplyInternalPatch
        {
            private static bool Prefix(Hero originalHero, Hero originalGainedRelationWith)
            {
                var bmInvolved = originalHero?.IsBanditMilitiaHero() == true || originalGainedRelationWith?.IsBanditMilitiaHero() == true;

                if (!bmInvolved)
                    return true;

                return IsUsable(originalHero) && IsUsable(originalGainedRelationWith);
            }

            private static Exception Finalizer(Hero originalHero, Hero originalGainedRelationWith, Exception __exception)
            {
                if (__exception is null)
                    return null;

                bool bmInvolved = originalHero?.IsBanditMilitiaHero() == true || originalGainedRelationWith?.IsBanditMilitiaHero() == true;
                if (!bmInvolved)
                    return __exception;

                Logs.WriteToFile("BMR-RELATION-EXCEPTION", $"{originalHero?.StringId} <-> {originalGainedRelationWith?.StringId}: {__exception}");
                return null;
            }

            private static bool IsUsable(Hero hero)
                => hero is not null
                   && hero.CharacterObject is not null
                   && hero.CharacterObject.Id.InternalValue != 0;
        }

        [HarmonyPatch(typeof(PartyBase), "PartySizeLimit", MethodType.Getter)]
        public static class MobilePartyPartySizeLimitGetterPatch
        {
            public static void Postfix(PartyBase __instance, ref int ____cachedPartyMemberSizeLimit, ref int __result)
            {
                if (!Globals.Settings.IgnoreSizePenalty || !__instance.IsMobile || !__instance.MobileParty.IsBanditMilitiaParty()) return;
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
                if (hero1?.IsBanditMilitiaHero() == true || hero2?.IsBanditMilitiaHero() == true)
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
                return !Settings.RemovePrisonerMessages || party == PartyBase.MainParty || hero?.IsBanditMilitiaHero() != true;
            }
        }

        [HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnHeroPrisonerReleased")]
        public static class DefaultLogsCampaignBehaviorOnHeroPrisonerReleasedPatch
        {
            public static bool Prefix(PartyBase party, Hero hero)
            {
                return !Settings.RemovePrisonerMessages || party == PartyBase.MainParty || hero?.IsBanditMilitiaHero() != true;
            }
        }

        [HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
        public static class MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBanditMilitiaParty();
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
                if (winnerPartyLeader?.PartyBelongedTo?.IsBanditMilitiaParty() == true
                    || defeatedParty?.MobileParty?.IsBanditMilitiaParty() == true)
                {
                    if (winnerPartyLeader is null || winnerPartyLeader.IsDead) return false;
                    if (winnerPartyLeader.HeroDeveloper is null) return false;
                    if (defeatedParty is null || (!defeatedParty.IsSettlement && defeatedParty.MobileParty is null)) return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(GainRenownAction), "ApplyInternal")]
        public static class GainRenownActionApplyInternalPatch
        {
            public static bool Prefix(Hero hero)
            {
                if (hero is null)
                    return true;

                return !hero.IsBanditMilitiaHero();
            }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.HasNavalNavigationCapability), MethodType.Getter)]
        public static class MobilePartyHasNavalNavigationCapabilityPatch
        {
            public static bool Prefix(MobileParty __instance, ref bool __result)
            {
                if (!__instance.IsBanditMilitiaParty())
                    return true;

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "OnHeroDailyTick")]
        public static class HeroSpawnCampaignBehaviorOnHeroDailyTickPatch
        {
            public static bool Prefix(Hero hero) => hero?.IsBanditMilitiaHero() != true;
        }

        [HarmonyPatch(typeof(DefaultEncyclopediaHeroPage), nameof(DefaultEncyclopediaHeroPage.IsValidEncyclopediaItem))]
        internal static class EncyclopediaHeroVisibilityPatch
        {
            private static void Postfix(object o, ref bool __result)
            {
                if (__result)
                    return;

                if (o is Hero hero
                    && hero.IsBanditMilitiaHero()
                    && hero is { IsTemplate: false, IsReady: true }
                    && !hero.CharacterObject.HiddenInEncyclopedia
                    && !hero.HiddenInEncyclopedia)
                {
                    __result = true;
                }
            }
        }

        // Blocks navigation to bandit-clan encyclopedia pages, which the vanilla
        // clan page VM cannot build (it is designed around Clan.NonBanditFactions).
        [HarmonyPatch(typeof(EncyclopediaManager), nameof(EncyclopediaManager.GoToLink),
            new[] { typeof(string), typeof(string) })]
        internal static class EncyclopediaBanditClanLinkPatch
        {
            private static bool Prefix(string pageType, string stringID)
            {
                // Only intercept clan links; the clan page identifier is "Faction".
                var clan = Campaign.Current?.CampaignObjectManager.Find<Clan>(stringID);
                if (clan is not null && clan.IsBanditFaction)
                    return false; // swallow the click: do nothing, no page change.

                return true; // all other links behave normally.
            }
        }

        [HarmonyPatch(typeof(EncyclopediaHeroPageVM), "UpdateInformationText")]
        public static class EncyclopediaHeroPageUpdateInformationTextPatch
        {
            private static readonly AccessTools.FieldRef<EncyclopediaHeroPageVM, Hero> HeroField =
                AccessTools.FieldRefAccess<EncyclopediaHeroPageVM, Hero>("_hero");

            public static void Postfix(EncyclopediaHeroPageVM __instance)
            {
                var hero = HeroField(__instance);
                if (hero is null || !hero.IsBanditMilitiaHero())
                    return;

                __instance.InformationText = NotorietyBehavior.BuildEncyclopediaText(hero).ToString();
            }
        }
        
        [HarmonyPatch(typeof(MobileParty), "MapFaction", MethodType.Getter)]
        public static class MobilePartyMapFactionNullGuardPatch
        {
            public static void Postfix(MobileParty __instance, ref IFaction __result)
            {
                if (__result is not null || !__instance.IsBanditMilitiaParty())
                    return;

                __result = __instance.GetBanditMilitiaParty()?.PartyOwner?.Clan?.MapFaction
                           ?? __instance.HomeSettlement?.OwnerClan?.MapFaction
                           ?? Clan.BanditFactions.FirstOrDefault();
            }
        }
        
        [HarmonyPatch(typeof(DefaultExecutionRelationModel), nameof(DefaultExecutionRelationModel.GetRelationChangeForExecutingHero))]
        public static class ExecutionRelationModelGetRelationChangePatch
        {
            public static void Postfix(Hero victim, ref int __result, ref bool showQuickNotification)
            {
                if (victim?.IsBanditMilitiaHero() != true)
                    return;
                __result = 0;
                showQuickNotification = false;
            }
        }
        
        [HarmonyPatch(typeof(TraitLevelingHelper), "AddPlayerTraitXPAndLogEntry")]
        public static class TraitLevelingHelperAddPlayerTraitXpPatch
        {
            // Prefix: must skip vanilla's -1000 Honor; OnLordExecuted itself gets inlined so patching it does nothing.
            public static bool Prefix(TraitObject trait, int xpValue)
                => SuppressedExecutionVictim is null || trait != DefaultTraits.Honor || xpValue != -1000;
        }
    }
}