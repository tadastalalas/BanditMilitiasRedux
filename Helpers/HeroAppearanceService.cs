using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BanditMilitiasRedux.Helpers
{
    internal static class HeroAppearanceService
    {
        private interface IAppearanceStrategy
        {
            BodyProperties GenerateBodyProperties(Hero hero);
        }

        private sealed class RandomAppearanceStrategy : IAppearanceStrategy
        {
            public BodyProperties GenerateBodyProperties(Hero hero)
            {
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
            private static readonly Lazy<Action<Hero, StaticBodyProperties>> SetStaticBodyProps = new(() =>
            {
                var property = AccessTools.Property("TaleWorlds.CampaignSystem.Hero:StaticBodyProperties");
                return (hero, props) => property?.SetValue(hero, props);
            });

            internal void Apply(Hero hero, StaticBodyProperties staticProperties)
            {
                SetStaticBodyProps.Value(hero, staticProperties);
            }
        }

        private static readonly Lazy<RandomAppearanceStrategy> RandomStrategy = new();
        private static readonly Lazy<BodyPropertyApplicator> Applicator = new();

        public static void RandomizeAppearance(Hero hero)
        {
            RandomizeAppearance(hero, RandomStrategy.Value);
        }

        private static void RandomizeAppearance(Hero hero, IAppearanceStrategy strategy)
        {
            var bodyProperties = strategy.GenerateBodyProperties(hero);
            Applicator.Value.Apply(hero, bodyProperties.StaticProperties);
        }
    }
}