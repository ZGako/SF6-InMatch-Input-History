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
using SF6_Plugin_Core.UI;
using SF6_Plugin_Core.UI.TrainingPauseMenu;
using SF6_Plugin_Core.UI.TrainingPauseMenu.CustomElements;
using SF6_Plugin_Core.UI.TrainingPauseMenu.DispatchRequests;
using SF6_Plugin_Core.UI.TrainingPauseMenu.ModificationRequests;

// TrainingModePlus usings
using System.Reflection;
using SF6_MIH.InputHistory;
using SF6_Plugin_Core.Services;

namespace SF6_MIH;

public static class InMatchInputHistory
{
    // actual class statics for the plugin
    private static InputHistoryClass? s_inputHistoryModule;

    // NoInlining necessary for dynamic assembly resolution
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializePlugin()
    {
        // Logging stuff
        API.LogLevel = 0;
        API.LogWarning("Loading InMatchInputHistory C# plugin...");

        GameSingletonRegistry.RegisterUIAgentManager(onReady: RegisterFlowCallback);

        GameSingletonRegistry.RegisterResourceManager(onReady: RegisterFlowCallback);
    }

    // NoInlining necessary for dynamic assembly resolution
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadPlugin()
    {
        API.LogInfo("Unloading InMatchInputHistory C# plugin...");

        s_inputHistoryModule?.Dispose();
        s_inputHistoryModule = null;

        FlowTransitionDispatcher.UnregisterGameModeFlowTransition(app.EGameMode.TRAINING, EnteringMatch, ExitingMatch);
    }

    private static void RegisterFlowCallback()
    {
        if (GameSingletonRegistry.UIAgentManager != null && GameSingletonRegistry.ResourceManager != null)
        {
            GameSingletonRegistry.RegisterBFlowManager(onReady: () => FlowTransitionDispatcher.RegisterGameModeFlowTransition(app.EGameMode.TRAINING, EnteringMatch, ExitingMatch));
        }
    }

    private static void EnteringMatch()
    {
        API.LogInfo("Entering match state");
        s_inputHistoryModule?.Dispose();
        s_inputHistoryModule = new InputHistoryClass();
    }

    private static void ExitingMatch()
    {
        API.LogInfo("Exiting match state");
        s_inputHistoryModule?.Dispose();
        s_inputHistoryModule = null;
    }

    ///
    ///  Default plugin entry and exit points
    /// 

    [PluginEntryPoint]
    public static void PluginEntryPoint()
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

    [PluginExitPoint]
    public static void PluginExitPoint()
    {
        try
        {
            UnloadPlugin();
        }
        catch (Exception ex)
        {
            API.LogError($"Exception during PluginExitPoint: {ex}");
        }

        try
        {
            var currentALC = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());

            currentALC?.Resolving -= OnResolvingCore;
        }
        catch (Exception ex)
        {
            API.LogError($"Exception during AssemblyLoadContext cleanup: {ex}");
        }
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
                        return loadedAssembly;
                    }
                }
            }
        }
        API.LogError($"Failed to resolve assembly: {assemblyName.Name}. Ensure that the SF6_Plugin_Core is loaded and accessible.");
#pragma warning disable CS8603 // Possible null reference return.
        return null; // Let default resolution fail if not found
#pragma warning restore CS8603 // Possible null reference return.
    }

}