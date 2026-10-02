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
using SF6_Plugin_Core.UI.TrainingPauseMenu.CustomElements;
using SF6_Plugin_Core.UI.TrainingPauseMenu.DispatchRequests;
using SF6_Plugin_Core.UI.TrainingPauseMenu.ModificationRequests;

// TrainingModePlus usings
using System.Reflection;
using SF6_MIH.InputHistory;

namespace SF6_MIH;

/// <summary>
/// The main entry point for the InMatchInputHistory plugin. This class is responsible for initializing the plugin, managing its modules, and handling the lifecycle of the TrainingManager.
/// </summary>
public static class InMatchInputHistory
{

    static InputHistoryClass? s_inputHistoryModule;

    private static FlowTransitionDispatcher.FlowTransitionHook? _flowTransitionHook;

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializePlugin()
    {
        // Logging stuff
        API.LogLevel = 0;
        API.LogWarning("Loading InMatchInputHistory C# plugin...");

        GameSingletonRegistry.RegisterUIAgentManager(onReady: RegisterFlowCallback);

        GameSingletonRegistry.RegisterResourceManager(onReady: RegisterFlowCallback);
    }

    private static void RegisterFlowCallback()
    {
        if (GameSingletonRegistry.UIAgentManager != null && GameSingletonRegistry.ResourceManager != null)
        {
            GameSingletonRegistry.RegisterBFlowManager(onReady: () =>
            {
                // create the hook for the flow transition dispatcher
                List<FlowTransitionDispatcher.FlowTransitionStates> targetStates =
                [
                    new() {
                        GameMode = app.AppDefine.GameMode.FightingGround,
                        SceneIndex = app.constant.scn.Index.eBattleMain,
                        FlowMapID = app.constant.FlowMap.eID.TRAINING
                    },
                    new() {
                        GameMode = app.AppDefine.GameMode.FightingGround,
                        SceneIndex = app.constant.scn.Index.eBattleMain,
                        FlowMapID = app.constant.FlowMap.eID.VERSUS_CPU
                    },
                    new() {
                        GameMode = app.AppDefine.GameMode.FightingGround,
                        SceneIndex = app.constant.scn.Index.eBattleMain,
                        FlowMapID = app.constant.FlowMap.eID.VERSUS
                    }
                ];

                _flowTransitionHook = new FlowTransitionDispatcher.FlowTransitionHook(
                    targetStates,
                    onEnterState: EnteringMatch,
                    onExitState: ExitingMatch
                );

                FlowTransitionDispatcher.RegisterFlowTransition(_flowTransitionHook);
            });
        }
    }

    private static void EnteringMatch(FlowTransitionDispatcher.FlowTransitionStates state)
    {
        API.LogInfo($"Entering match state: GameMode={state.GameMode}, SceneIndex={state.SceneIndex}, FlowMapID={state.FlowMapID}");
        s_inputHistoryModule?.Dispose();

        s_inputHistoryModule = new InputHistoryClass();
    }

    private static void ExitingMatch(FlowTransitionDispatcher.FlowTransitionStates _)
    {
        API.LogInfo($"Exiting match state");
        s_inputHistoryModule?.Dispose();
        s_inputHistoryModule = null;
    }

    [PluginExitPoint]
    public static void PluginExitPoint()
    {
        // Clean up static states

        try
        {
            API.LogInfo("Unloading InMatchInputHistory C# plugin...");

            if (_flowTransitionHook != null)
            {
                FlowTransitionDispatcher.UnregisterFlowTransition(_flowTransitionHook);
                _flowTransitionHook = null;
            }

            s_inputHistoryModule?.Dispose();
            s_inputHistoryModule = null;

            GameSingletonRegistry.Clear();

            var currentALC = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
            if (currentALC != null)
            {
                currentALC.Resolving -= OnResolvingCore;
            }
        }
        catch (Exception ex)
        {
            API.LogError($"Error during PluginExitPoint cleanup: {ex}");
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
                        return loadedAssembly; // We found it in memory! Hand it back to the runtime.
                    }
                }
            }
        }
#pragma warning disable CS8603 // Possible null reference return.
        return null; // Let default resolution fail if not found
#pragma warning restore CS8603 // Possible null reference return.
    }

}