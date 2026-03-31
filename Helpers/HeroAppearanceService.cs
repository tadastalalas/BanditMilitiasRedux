using System;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BanditMilitias.Helpers
{
    internal sealed class HeroAppearanceService
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<HeroAppearanceService>();

        public interface IAppearanceStrategy
        {
            BodyProperties GenerateBodyProperties(Hero hero);
        }

        private sealed class RandomAppearanceStrategy : IAppearanceStrategy
        {
            public BodyProperties GenerateBodyProperties(Hero hero)
            {
                if (hero?.CharacterObject == null)
                {
                    Logger.LogWarning("Cannot generate appearance: hero or CharacterObject is null");
                    return default;
                }

                var faceParams = CreateFaceGenerationParams(hero);
                var bodyProperties = hero.CharacterObject.GetBodyProperties(hero.BattleEquipment);

                faceParams.SetRandomParamsExceptKeys(
                    faceParams.CurrentRace,
                    faceParams.CurrentGender,
                    (int)faceParams.CurrentAge,
                    out float _);

                MBBodyProperties.ProduceNumericKeyWithParams(
                    faceParams,
                    hero.BattleEquipment.EarsAreHidden,
                    hero.BattleEquipment.MouthIsHidden,
                    ref bodyProperties);

                return bodyProperties;
            }

            private static FaceGenerationParams CreateFaceGenerationParams(Hero hero)
            {
                return FaceGenerationParams.Create() with
                {
                    CurrentRace = hero.CharacterObject.Race,
                    CurrentGender = hero.IsFemale ? 1 : 0,
                    CurrentAge = hero.Age
                };
            }
        }

        private sealed class BodyPropertyApplicator
        {
            private static readonly Lazy<Action<Hero, StaticBodyProperties>> _setStaticBodyProps = new(() =>
            {
                var property = AccessTools.Property(typeof(Hero), "StaticBodyProperties");
                return (hero, props) =>
                {
                    try
                    {
                        property?.SetValue(hero, props);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to set StaticBodyProperties for hero {hero?.Name}");
                    }
                };
            });

            public void Apply(Hero hero, StaticBodyProperties staticProperties)
            {
                if (hero == null)
                {
                    Logger.LogWarning("Cannot apply body properties: hero is null");
                    return;
                }

                _setStaticBodyProps.Value(hero, staticProperties);
            }
        }

        private static readonly Lazy<RandomAppearanceStrategy> _randomStrategy = new();
        private static readonly Lazy<BodyPropertyApplicator> _applicator = new();

        public static void RandomizeAppearance(Hero hero)
        {
            RandomizeAppearance(hero, _randomStrategy.Value);
        }

        public static void RandomizeAppearance(Hero hero, IAppearanceStrategy strategy)
        {
            if (hero == null)
            {
                Logger.LogWarning("Cannot randomize appearance: hero is null");
                return;
            }

            if (strategy == null)
            {
                Logger.LogWarning("Cannot randomize appearance: strategy is null");
                return;
            }

            try
            {
                var bodyProperties = strategy.GenerateBodyProperties(hero);
                _applicator.Value.Apply(hero, bodyProperties.StaticProperties);

                Logger.LogTrace($"Randomized appearance for hero: {hero.Name}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error randomizing appearance for hero {hero?.Name}");
            }
        }
    }
}