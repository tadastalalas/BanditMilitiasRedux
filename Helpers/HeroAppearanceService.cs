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
        public interface IAppearanceStrategy
        {
            BodyProperties GenerateBodyProperties(Hero hero);
        }

        private sealed class RandomAppearanceStrategy : IAppearanceStrategy
        {
            public BodyProperties GenerateBodyProperties(Hero hero)
            {
                if (hero?.CharacterObject == null)
                    return default;

                var bodyProperties = hero.CharacterObject.GetBodyProperties(hero.BattleEquipment);

                var faceParams = FaceGenerationParams.Create();
                MBBodyProperties.GetParamsFromKey(
                    ref faceParams,
                    bodyProperties,
                    hero.BattleEquipment.EarsAreHidden,
                    hero.BattleEquipment.MouthIsHidden);

                faceParams.CurrentRace = hero.CharacterObject.Race;
                faceParams.CurrentGender = hero.IsFemale ? 1 : 0;
                faceParams.CurrentAge = hero.Age;

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
                    catch (Exception) { }
                };
            });

            public void Apply(Hero hero, StaticBodyProperties staticProperties)
            {
                if (hero == null)
                    return;

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
                return;

            if (strategy == null)
                return;

            try
            {
                var bodyProperties = strategy.GenerateBodyProperties(hero);
                _applicator.Value.Apply(hero, bodyProperties.StaticProperties);
            }
            catch (Exception) { }
        }
    }
}