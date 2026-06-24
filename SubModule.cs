using System;
using System.Linq;
using System.Reflection;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Helpers;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using MCM.Common;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux
{
    public class SubModule : MBSubModuleBase
    {
        private static readonly string? Name = typeof(SubModule).Namespace;
        public static readonly PlatformDirectoryPath ConfigDir = EngineFilePaths.ConfigsPath + $"/ModSettings/Global/{Name}";
        private static readonly Harmony Harmony = new("BanditMilitiasRedux");

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (Campaign.Current is null)
                return;

            if (Input.IsKeyPressed(InputKey.N) && Input.IsKeyDown(InputKey.LeftControl) && Input.IsKeyDown(InputKey.LeftAlt))
                Helper.NukeEverything();
        }

        private static readonly MethodInfo CreateRandomBannerInternal = AccessTools.Method("TaleWorlds.Core.Banner:CreateRandomBannerInternal");

        internal static void CacheBanners()
        {
            var args = new object[2];
            Banners.Capacity = Math.Max(Banners.Capacity, 5000);
            for (var i = 0; i < 5000; i++)
            {
                args[0] = MBRandom.RandomInt(0, int.MaxValue);
                args[1] = -1;
                if (CreateRandomBannerInternal.Invoke(null, args) is Banner banner)
                    Banners.Add(banner);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            Settings.XpGift = new Dropdown<string>(DifficultyXpMap.Keys.SelectQ(k => k.ToString()), 1);
            Settings.GoldReward = new Dropdown<string>(GoldMap.Keys.SelectQ(k => k.ToString()), 1);
            AdjustForLoadOrder();
        }

        private static void AdjustForLoadOrder()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var bm = assemblies.First(a => a.FullName.StartsWith("BanditMilitias"));
            var cek = assemblies.FirstOrDefaultQ(x => x.FullName.StartsWith("CalradiaExpandedKingdoms"));
            if (cek is null)
                return;
            if (assemblies.FindIndex(a => a == bm) > assemblies.FindIndex(a => a == cek))
                Settings.RandomBanners = false;
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            if (gameStarterObject is CampaignGameStarter gameStarter)
            {
                gameStarter.AddBehavior(new MilitiaBehavior());
                gameStarter.AddBehavior(new ReusableHeroesBehavior());
                gameStarter.AddBehavior(new NotorietyBehavior());
                gameStarter.AddBehavior(new DialogsBehavior());
            }
            if (MCMSettings.Instance != null)
                MCMSettings.OnSettingsChanged += OnSettingsChanged;
        }

        private static readonly MethodInfo ResetCached = AccessTools.Method("TaleWorlds.CampaignSystem.Party.MobileParty:ResetCached");

        private static void OnSettingsChanged()
        {
            if (Campaign.Current is null)
                return;
            
            RaidCap = Helper.CalculateRaidCap();

            foreach (BanditMilitiaPartyComponent? banditMilitiaPartyComponent in PowerCalculationManager.GetActiveBanditMilitiaParties(true).ToArrayQ())
            {
                banditMilitiaPartyComponent?.ClearCachedName();
                if (banditMilitiaPartyComponent?.MobileParty is null)
                    continue;
                ResetCached.Invoke(banditMilitiaPartyComponent.MobileParty, null);
            }
        }

        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);

            ClearGlobals();
            BanditMilitiaManager.ResetUpgraderBehavior();
            MCMSettings.OnSettingsChanged -= OnSettingsChanged;
            IsGlobalModStateInitialized = false;
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            CacheBanners();
        }
    }
}