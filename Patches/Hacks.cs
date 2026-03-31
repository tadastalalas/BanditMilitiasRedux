using System;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem.Roster;

// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public sealed class Hacks
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<Hacks>();
    }
}
