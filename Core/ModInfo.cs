using System;
using System.IO;

namespace BanditMilitiasRedux.Core;

internal static class ModInfo
{
    internal const string ModuleId = "BanditMilitiasRedux";
    internal const string ConfigurationFileName = "ModConfiguration.xml";
    internal const string LogFolderName = "logs";

    private static string? _moduleRoot;

    internal static string ModuleRoot => _moduleRoot ??= ResolveModuleRoot();

    private static string ResolveModuleRoot()
    {
        // <Module>\bin\<Platform>\*.dll -> <Module>
        string location = typeof(ModInfo).Assembly.Location;
        string? binFolder = string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
        if (binFolder is { Length: > 0 }) return Path.GetFullPath(Path.Combine(binFolder, "..", ".."));

        // The DLL was loaded without a file path: <game>\bin\<Platform>\ -> <game>\Modules\<ModuleId>
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Modules", ModuleId));
    }
}
