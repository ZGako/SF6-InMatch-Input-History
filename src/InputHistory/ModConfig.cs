using System.Text.Json;
using System.Text.Json.Serialization;

namespace SF6_MIH.InputHistory;

[JsonConverter(typeof(JsonStringEnumConverter<OnlinePosition>))]
public enum OnlinePosition
{
    FOLLOWPLAYER,
    ALWAYSP1,
    ALWAYSP2
}

public class PanelTransformConfig
{
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1f;
    public int MaxVisibleRows { get; set; } = 19;
    public float Transparency { get; set; } = 1f;
}

public class AppliedGameModesConfig
{
    public bool LocalVS { get; set; } = true;
    public bool SimSimBattle { get; set; } = true;
    public bool RankedMatch { get; set; } = true;
    public bool CasualMatch { get; set; } = true;
    public bool BattleHubMatch { get; set; } = true;
    public bool CustomRoomMatch { get; set; } = true;

    public bool IsEnabled(app.EGameMode mode)
    {
        return mode switch
        {
            app.EGameMode.VERSUS_2P => LocalVS,
            app.EGameMode.LEARNING_AI_MATCH => SimSimBattle,
            app.EGameMode.RANKED_MATCH => RankedMatch,
            app.EGameMode.PLAYER_MATCH => CasualMatch,
            app.EGameMode.CABINET_MATCH => BattleHubMatch,
            app.EGameMode.CUSTOM_ROOM_MATCH => CustomRoomMatch,
            _ => false
        };
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(ModConfig))]
[JsonSerializable(typeof(PanelTransformConfig))]
[JsonSerializable(typeof(AppliedGameModesConfig))]
[JsonSerializable(typeof(OnlinePosition))]
internal partial class ModConfigJsonContext : JsonSerializerContext
{
}

public class ModConfig
{
    private static readonly string ConfigFilePath =
        Path.Combine("reframework", "data", "SF6_InMatchInputHistory_Config.json");

    public static ModConfig Instance { get; private set; } = new();

    // Settings
    public OnlinePosition DisplayPosition { get; set; } = OnlinePosition.FOLLOWPLAYER;

    // apply the positioning not only online but also in matches against the CPU
    public bool ApplyPositionAgainstCPU { get; set; } = true;
    public bool OfflineShowCPU { get; set; }
    public bool ShowBackgroundTexture { get; set; } = true;
    public AppliedGameModesConfig AppliedGameModes { get; set; } = new();
    public PanelTransformConfig LeftPanelTransform { get; set; } = new();
    public PanelTransformConfig RightPanelTransform { get; set; } = new();

    // Event fired whenever a setting is changed in your live setup screen
    public static event Action<ModConfig>? OnConfigChanged;

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                Instance = JsonSerializer.Deserialize(json, ModConfigJsonContext.Default.ModConfig) ?? new ModConfig();
            }
            else
            {
                Instance = new ModConfig();
            }

            // save in case of new fields or no config present
            Save();
        }
        catch (Exception ex)
        {
            API.LogError($"Failed to load ModConfig, using defaults: {ex.Message}");
            Instance = new ModConfig();
        }
    }

    public static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(Instance, ModConfigJsonContext.Default.ModConfig);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            API.LogError($"Failed to save ModConfig: {ex.Message}");
        }
    }

    /// <summary>
    ///     Call this from your live setup menu whenever a value is tweaked mid-match.
    /// </summary>
    public static void NotifyChanged()
    {
        OnConfigChanged?.Invoke(Instance);
    }

    /// <summary>
    ///     Call this inside your [PluginExitPoint] method to release static delegates and references.
    /// </summary>
    public static void Clear()
    {
        OnConfigChanged = null;
        Instance = null!;
    }
}