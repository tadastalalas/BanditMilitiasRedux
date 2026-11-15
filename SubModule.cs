using System;
using System.Linq;
using System.Reflection;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
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

    // The game the patches were applied for; null while nothing is patched.
    private static Game? _patchedGame;

    // Ctrl+Alt+N on the campaign map: asks, then removes every militia, militia hero and militia map event (uninstall helper).
    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);

        if (Campaign.Current is null)
            return;

        if (Input.IsKeyPressed(NukeHotkey) && Input.IsKeyDown(InputKey.LeftControl) && Input.IsKeyDown(InputKey.LeftAlt)
            && Game.Current?.GameStateManager?.ActiveState is MapState)
            Helper.AskToNukeEverything();
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

    // Patches are applied here and removed in OnGameEnd, never in OnSubModuleLoad
    // (too early for patches near agents: http://bannerlordmodding.lt/guides/human_bullet/).
    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        ModSettings.Reload();

        // Behaviors first: a failing patch must not leave the campaign without them.
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

        ApplyPatches(game);
    }

    // Harmony never skips patches it already applied: a second PatchAll would run every patch twice.
    // So they are applied once and stay until the game they were applied for ends.
    private static void ApplyPatches(Game game)
    {
        bool alreadyPatched = _patchedGame is not null;
        _patchedGame = game;
        if (alreadyPatched)
            return;

        try
        {
            Harmony.PatchAll(typeof(SubModule).Assembly);
        }
        catch (HarmonyException ex)
        {
            BmrLog.Error("Harmony.PatchAll failed: some patches are missing.", ex);
            throw;
        }
    }

    // Only the game the patches belong to removes them (an older game may end after a newer one started).
    private static void RemovePatches(Game game)
    {
        if (_patchedGame != game)
            return;

        _patchedGame = null;
        Harmony.UnpatchAll(Harmony.Id);
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
        RemovePatches(game);
    }
}
