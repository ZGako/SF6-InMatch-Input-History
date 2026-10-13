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
        API.LogInfo("Loading InMatchInputHistory C# plugin...");

        ModConfig.Load();

        GameSingletonManager.RegisterBFlowManager(onReady: RegisterGuiHudCallback);

        GameSingletonManager.RegisterUIAgentManager(onReady: RegisterGuiHudCallback);

        GameSingletonManager.RegisterResourceManager(onReady: RegisterGuiHudCallback);

        GameSingletonManager.RegisterNetworkManager(onReady: () => { });
    }

    // NoInlining necessary for dynamic assembly resolution
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadPlugin()
    {
        API.LogInfo("Unloading InMatchInputHistory C# plugin...");
        try
        {
            BattleHudHooks.UnregisterBattleHudCallbacks(onSetup: OnHudSetup, onUnload: UnloadingHud);
        }
        catch (Exception ex)
        {
            API.LogError($"Exception during UnloadPlugin: {ex}");
        }

        s_inputHistoryModule?.Dispose();
        s_inputHistoryModule = null;

        ModConfig.Clear();
    }

    private static void RegisterGuiHudCallback()
    {
        if (GameSingletonManager.UIAgentManager != null && GameSingletonManager.ResourceManager != null && GameSingletonManager.BFlowManager != null)
        {
            GameSingletonManager.RegisterGuiManager(onReady: () =>
            {
                //TODO: add part of the config here maybe?
                BattleHudHooks.RegisterBattleHudSetupCallbacks(OnHudSetup, UnloadingHud);
            });
        }
    }

    private static void OnHudSetup(app.BattleHudManager _)
    {
        // verify that we're in a valid game mode
        if (GameSingletonManager.BFlowManager == null)
        {
            return; // BFlowManager is not ready yet, exit early
        }

        try
        {
            var flowWork = GameSingletonManager.BFlowManager.m_flow_work;

            var flowMap = flowWork.FlowMap;

            if (flowMap is not IObject flowObj || !flowObj.GetTypeDefinition().IsDerivedFrom(app.battle.BattleFlowMap.REFType))
            {
                return; // FlowMap is not a BattleFlowMap, exit early
            }

            var battleFlowMap = ManagedProxy<app.battle.BattleFlowMap>.Create(flowMap);

            if (battleFlowMap == null)
            {
                return; // Failed to create ManagedProxy for BattleFlowMap, exit early
            }

            var gameMode = battleFlowMap.m_desc.Rule.GameMode;

            if (!ModConfig.Instance.AppliedGameModes.IsEnabled(gameMode))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            API.LogError($"Exception during OnHudSetup: {ex}");
            return; // Exit early on exception
        }

        s_inputHistoryModule?.Dispose();
        s_inputHistoryModule = new InputHistoryClass();
    }

    private static void UnloadingHud()
    {
        // always dispose of the input history regardless when unloading the "HUD"
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