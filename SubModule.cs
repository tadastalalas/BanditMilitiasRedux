using System;
using System.Linq;
using System.Reflection;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux;

public class SubModule : MBSubModuleBase
{
    private const string OwnAssemblyPrefix = "BanditMilitias";
    private const string CalradiaExpandedKingdomsAssemblyPrefix = "CalradiaExpandedKingdoms";
    private const InputKey NukeHotkey = InputKey.N;

    private static readonly Harmony Harmony = new(ModInfo.ModuleId);
    private static readonly MethodInfo ResetCached = AccessTools.Method("TaleWorlds.CampaignSystem.Party.MobileParty:ResetCached");

    // Ctrl+Alt+N removes every militia, militia hero and militia map event (uninstall helper).
    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);

        if (Campaign.Current is null)
            return;

        if (Input.IsKeyPressed(NukeHotkey) && Input.IsKeyDown(InputKey.LeftControl) && Input.IsKeyDown(InputKey.LeftAlt))
            Helper.NukeEverything();
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        ModSettings.LocalizeMcmDropdowns();
        AdjustForLoadOrder();
    }

    // Unique random banners are turned off when this mod loads after Calradia Expanded Kingdoms.
    private static void AdjustForLoadOrder()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Assembly ownAssembly = assemblies.First(a => a.FullName.StartsWith(OwnAssemblyPrefix));
        Assembly? calradiaExpandedKingdoms = assemblies.FirstOrDefaultQ(x => x.FullName.StartsWith(CalradiaExpandedKingdomsAssemblyPrefix));
        if (calradiaExpandedKingdoms is null)
            return;
        if (assemblies.FindIndex(a => a == ownAssembly) > assemblies.FindIndex(a => a == calradiaExpandedKingdoms))
            ModSettings.ForceRandomBannersOff();
    }

    // PatchAll runs here (every game start; Harmony skips patches it already applied).
    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        ModSettings.Reload();
        Harmony.PatchAll(typeof(SubModule).Assembly);

        if (gameStarterObject is CampaignGameStarter gameStarter)
        {
            gameStarter.AddBehavior(new MilitiaBehavior());
            gameStarter.AddBehavior(new ReusableHeroesBehavior());
            gameStarter.AddBehavior(new NotorietyBehavior());
            gameStarter.AddBehavior(new DialogsBehavior());
            gameStarter.AddBehavior(new HeroDefectionBehavior());
        }

        ModSettings.Changed -= OnSettingsChanged;
        ModSettings.Changed += OnSettingsChanged;
    }

    // MCM only: settings changed mid-game.
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
            MilitiaTrackerManager.Refresh(banditMilitiaPartyComponent.MobileParty);
        }
    }

    public override void OnGameEnd(Game game)
    {
        base.OnGameEnd(game);

        AllAliveBanditMilitiaHeroes.Clear();
        ReusableHeroesBehavior.Reset();
        ClearGlobals();
        MilitiaTrackerManager.Reset();
        BanditMilitiaManager.ResetUpgraderBehavior();
        ModSettings.Changed -= OnSettingsChanged;
        IsGlobalModStateInitialized = false;
    }
}
