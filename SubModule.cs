using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BanditMilitias.Helpers;
using BanditMilitias.Patches;
using Bannerlord.ButterLib.Common.Extensions;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog.Events;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade;
using static BanditMilitias.Globals;
using Module = TaleWorlds.MountAndBlade.Module;

namespace BanditMilitias
{
    public class SubModule : MBSubModuleBase
    {
        public static readonly string Name = typeof(SubModule).Namespace!;
        public static readonly PlatformDirectoryPath ConfigDir = EngineFilePaths.ConfigsPath + $"/ModSettings/Global/{Name}";
        public static readonly Harmony harmony = new("BanditMilitiasRedux");
        public static SubModule Instance { get; private set; }

        public void OnServiceRegistration()
        {
            string configPath = new PlatformFilePath(ConfigDir, $"{Name}.json").FileFullPath;
            var config = File.Exists(configPath) ? JsonConvert.DeserializeObject<BanditMilitiaConfig>(File.ReadAllText(configPath)) : new BanditMilitiaConfig();
            this.AddSerilogLoggerProvider($"{Name}.log", [$"{Name}.*"], o => o.MinimumLevel.Is((LogEventLevel)config.MinLogLevel));
        }

        protected override void OnSubModuleLoad()
        {
            Instance = this;
            OnServiceRegistration();
            RunManualPatches();
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (Campaign.Current is null)
                return;

            if (Input.IsKeyPressed(InputKey.N)
                && Input.IsKeyDown(InputKey.LeftControl)
                && Input.IsKeyDown(InputKey.LeftAlt))
            {
                Helper.Nuke();
            }
        }

        internal static void CacheBanners()
        {
            for (var i = 0; i < 5000; i++)
            {
                var banner = (Banner)AccessTools.Method(typeof(Banner), "CreateRandomBannerInternal")
                    .Invoke(typeof(Banner), [MBRandom.RandomInt(0, int.MaxValue), -1]);
                if (banner is not null)
                    Banners.Add(banner);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            Globals.Settings = MCMSettings.Instance;
            if (Globals.Settings is null)
                return;

            Globals.Settings!.XpGift = new(Globals.DifficultyXpMap.Keys.SelectQ(k => k.ToString()), 1);
            Globals.Settings!.GoldReward = new(Globals.GoldMap.Keys.SelectQ(k => k.ToString()), 1);
            AdjustForLoadOrder();
        }

        private static void AdjustForLoadOrder()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var BM = assemblies.First(a => a.FullName.StartsWith("BanditMilitias"));
            var CEK = assemblies.FirstOrDefaultQ(x => x.FullName.StartsWith("CalradiaExpandedKingdoms"));
            if (CEK is not null)
                if (assemblies.FindIndex(a => a == BM) > assemblies.FindIndex(a => a == CEK))
                    Globals.Settings.RandomBanners = false;
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            VoicePatches.ApplyManualPatch(harmony);
            if (gameStarterObject is CampaignGameStarter gameStarter)
                gameStarter.AddBehavior(new MilitiaBehavior());
            if (MCMSettings.Instance != null)
                MCMSettings.OnSettingsChanged += OnSettingsChanged;
        }

        private static readonly MethodInfo _resetCached = AccessTools.Method(typeof(MobileParty), "ResetCached");

        private static void OnSettingsChanged()
        {
            foreach (ModBanditMilitiaPartyComponent bm in PowerCalculationService.GetCachedBMs(true).ToArrayQ())
            {
                bm.ClearCachedName();
                if (bm.MobileParty is null) continue;
                _resetCached.Invoke(bm.MobileParty, null);
            }
        }

        public override void OnGameEnd(Game game)
        {
            Globals.ClearGlobals();
            Globals.Banners.Clear();
            Globals.Heroes.Clear();
            MilitiaPartyFactory.ResetUpgraderBehavior();
            MCMSettings.OnSettingsChanged -= OnSettingsChanged;
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            CacheBanners();
        }

        private static void RunManualPatches()
        {
            try
            {
                var original = AccessTools.Method("ServeAsSoldier.ExtortionByDesertersEvent:CreateDeserterParty");
                if (original is not null)
                    harmony.Patch(original, postfix: new HarmonyMethod(AccessTools.Method(typeof(MilitiaPatches), nameof(MilitiaPatches.PatchSaSDeserters))));
            }
            catch (Exception) { }
        }
    }
}