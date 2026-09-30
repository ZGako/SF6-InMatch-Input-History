global using System;
global using System.Collections.Generic;
global using System.Runtime.InteropServices;
global using System.Linq;

global using REFrameworkNET;
global using REFrameworkNET.Attributes;
global using REFrameworkNET.Collections;
global using REFrameworkNET.Callbacks;


using System.Runtime.CompilerServices;
using System.Runtime.Loader;
// Core usings
using SF6_Plugin_Core;
using SF6_Plugin_Core.UI;
using SF6_Plugin_Core.UI.TrainingPauseMenu;

// TrainingModePlus usings
using System.Reflection;

namespace SF6_MIH;

/// <summary>
/// The main entry point for the InMatchInputHistory plugin. This class is responsible for initializing the plugin, managing its modules, and handling the lifecycle of the TrainingManager.
/// </summary>
public static class InMatchInputHistory
{
    [PluginEntryPoint]
    private static void PluginEntryPoint()
    {
        var currentALC = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
        if (currentALC == null)
        {
            API.LogError("Failed to get the current AssemblyLoadContext. Plugin initialization aborted.");
            return;
        }

        currentALC.Resolving += OnResolvingCore;

        InitializePlugin();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializePlugin()
    {
        // Logging stuff
        API.LogLevel = 0;
        API.LogWarning("Loading InMatchInputHistory C# plugin...");

        GameSingletonRegistry.RegisterUIAgentManager(
            onReady: () => { API.LogInfo("UIAgentManager is ready."); }
        );

        // Modules.Add(TrainingParametersAndRandomizer.Instance);
    }

    [PluginExitPoint]
    private static void PluginExitPoint()
    {
        // Clean up static states

        API.LogInfo("Unloading InMatchInputHistory C# plugin...");
    }

    private static Assembly OnResolvingCore(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (assemblyName.Name == "00_SF6PluginCore")
        {
            // Search all AssemblyLoadContexts to find where REFramework loaded the Core plugin
            foreach (var alc in AssemblyLoadContext.All)
            {
                foreach (var loadedAssembly in alc.Assemblies)
                {
                    if (loadedAssembly.GetName().Name == "00_SF6PluginCore")
                    {
                        return loadedAssembly; // We found it in memory! Hand it back to the runtime.
                    }
                }
            }
        }
        return null; // Let default resolution fail if not found
    }

}
