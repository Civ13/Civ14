using System.Collections.Generic;
using System.Linq;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.GameObjects;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Robust.Shared.Map.Components;
using Content.Shared.Light.EntitySystems;
using Content.Server.Chat.Systems;

namespace Content.Server.Weather;

/// <summary>
/// System responsible for managing dynamic weather changes and temperature adjustments for exposed tiles in a grid.
/// </summary>
public sealed class WeatherNomadsSystem : EntitySystem
{
    // Dependencies injected via IoC
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedWeatherSystem _weatherSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedRoofSystem _roofSystem = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    /// <summary>
    /// Structure representing properties of a weather type.
    /// </summary>
    private class WeatherType
    {
        public string? PrototypeId { get; set; } // ID of the weather prototype, null for "None"
        public int Weight { get; set; }          // Weight for weather transition order
        public float MinTemperature { get; set; } // Minimum temperature in Kelvin
        public float MaxTemperature { get; set; } // Maximum temperature in Kelvin
    }

    /// <summary>
    /// Dictionary defining available weather types and their properties.
    /// </summary>
    private readonly Dictionary<string, WeatherType> _weatherTypes = new()
    {
        { "None", new WeatherType { PrototypeId = "", Weight = 0, MinTemperature = 293.15f, MaxTemperature = 293.15f } },
        { "Rain", new WeatherType { PrototypeId = "Rain", Weight = 1, MinTemperature = 278.15f, MaxTemperature = 288.15f } },
        { "Storm", new WeatherType { PrototypeId = "Storm", Weight = 3, MinTemperature = 273.15f, MaxTemperature = 278.15f } },
        { "SnowfallLight", new WeatherType { PrototypeId = "SnowfallLight", Weight = 4, MinTemperature = 268.15f, MaxTemperature = 273.15f } },
        { "SnowfallMedium", new WeatherType { PrototypeId = "SnowfallMedium", Weight = 5, MinTemperature = 258.15f, MaxTemperature = 268.15f } },
        { "SnowfallHeavy", new WeatherType { PrototypeId = "SnowfallHeavy", Weight = 6, MinTemperature = 243.15f, MaxTemperature = 258.15f } },
        { "Hail", new WeatherType { PrototypeId = "Hail", Weight = 7, MinTemperature = 273.15f, MaxTemperature = 278.15f } },
        { "Sandstorm", new WeatherType { PrototypeId = "Sandstorm", Weight = 9, MinTemperature = 293.15f, MaxTemperature = 313.15f } },
        { "SandstormHeavy", new WeatherType { PrototypeId = "SandstormHeavy", Weight = 10, MinTemperature = 293.15f, MaxTemperature = 313.15f } },
    };

    public enum Biome
    {
        Tundra,
        Taiga,
        Temperate,
        Sea,
        SemiArid,
        Desert,
        Savanna,
        Jungle
    }

    public enum Precipitation
    {
        Dry,
        LightWet,
        HeavyWet,
        Storm
    }


    public class WeatherTransition
    {
        public Biome Biome { get; set; }
        public Precipitation Precipitation { get; set; }
        public string Season { get; set; } = "Spring";
        public string WeatherType { get; set; } = "None";
    }

    private readonly List<WeatherTransition> _weatherTransitions = new()
    {
        // Summer
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "Hail" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Sandstorm" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "SandstormHeavy" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Dry, Season = "Summer", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.LightWet, Season = "Summer", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.HeavyWet, Season = "Summer", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Storm, Season = "Summer", WeatherType = "Storm" },

        // Spring
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "Sandstorm" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Dry, Season = "Spring", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.LightWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.HeavyWet, Season = "Spring", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Storm, Season = "Spring", WeatherType = "Storm" },

        // Autumn
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "Sandstorm" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Dry, Season = "Autumn", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.LightWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.HeavyWet, Season = "Autumn", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Storm, Season = "Autumn", WeatherType = "Storm" },

        // Winter
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Tundra, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Taiga, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "SnowfallLight" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "SnowfallMedium" },
        new WeatherTransition { Biome = Biome.Temperate, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "SnowfallHeavy" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Sea, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.SemiArid, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Desert, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Savanna, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Dry, Season = "Winter", WeatherType = "Clear" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.LightWet, Season = "Winter", WeatherType = "Rain" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.HeavyWet, Season = "Winter", WeatherType = "Storm" },
        new WeatherTransition { Biome = Biome.Jungle, Precipitation = Precipitation.Storm, Season = "Winter", WeatherType = "Storm" },
    };

    /// <summary>
    /// Initializes the system and subscribes to relevant events.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeatherNomadsComponent, MapInitEvent>(OnMapInit);
        Log.Debug("WeatherNomadsSystem initialized successfully");
    }

    /// <summary>
    /// Handles the initialization of weather for a map when it is first created.
    /// </summary>
    private void OnMapInit(EntityUid uid, WeatherNomadsComponent component, MapInitEvent args)
    {
        var enabledTypes = _weatherTypes.Values
            .Where(w => component.EnabledWeathers.Contains(w.PrototypeId ?? string.Empty) || w.PrototypeId == "")
            .OrderBy(w => w.Weight)
            .ToList();

        if (enabledTypes.Any())
        {
            component.CurrentWeather = enabledTypes.First().PrototypeId ?? "";
            SetWeatherAndTemperature(uid, component);
            component.NextSwitchTime = _timing.CurTime + TimeSpan.FromMinutes(GetRandomSeasonDuration(component));
            component.NextSeasonChange = _timing.CurTime + TimeSpan.FromMinutes(45); // Initialize season change time
            Dirty(uid, component);
            Log.Debug($"Weather started for entity {uid} with {component.CurrentWeather}");
            Log.Debug($"Seasons started for entity {uid} with {component.CurrentSeason}");
            _chat.DispatchGlobalAnnouncement($"Current season: {component.CurrentSeason}", "World",
                false,
                null,
                null);
        }
        else
        {
            Log.Warning($"No valid weather types enabled for entity {uid}");
        }
    }

    /// <summary>
    /// Updates the weather system periodically, switching weather states as needed.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WeatherNomadsComponent>();
        while (query.MoveNext(out var uid, out var nomads))
        {
            if (_timing.CurTime >= nomads.NextSeasonChange)
            {
                // Change the season
                nomads.CurrentSeason = GetNextSeason(nomads.CurrentSeason);
                nomads.NextSeasonChange = _timing.CurTime + TimeSpan.FromMinutes(45);
                Dirty(uid, nomads);
                Log.Debug($"Changed season to {nomads.CurrentSeason}");
                _chat.DispatchGlobalAnnouncement($"Changed season to {nomads.CurrentSeason}",
                null,
                false,
                null,
                null);
            }

            if (_timing.CurTime < nomads.NextSwitchTime)
                continue;

            //This is where we would determine the biome, precipitation and season.
            //For now, we will just use a random weather type.
            var enabledTypes = _weatherTypes.Values
                .Where(w => nomads.EnabledWeathers.Contains(w.PrototypeId ?? string.Empty) || w.PrototypeId == "")
                .OrderBy(w => w.Weight)
                .ToList();

            if (!enabledTypes.Any())
                continue;

            var currentWeatherType = _weatherTypes.Values.FirstOrDefault(w => w.PrototypeId == nomads.CurrentWeather);
            if (currentWeatherType == null)
            {
                Log.Warning($"Current weather {nomads.CurrentWeather} not found in weather types");
                continue;
            }

            var currentIndex = enabledTypes.IndexOf(currentWeatherType);
            if (currentIndex == -1)
            {
                Log.Warning($"Current weather {nomads.CurrentWeather} not found in enabled types");
                continue;
            }

            var nextIndex = (currentIndex + 1) % enabledTypes.Count;
            nomads.CurrentWeather = enabledTypes[nextIndex].PrototypeId ?? "";
            SetWeatherAndTemperature(uid, nomads);
            nomads.NextSwitchTime = _timing.CurTime + TimeSpan.FromMinutes(GetRandomSeasonDuration(nomads));
            Dirty(uid, nomads);
            Log.Debug($"Switched weather for entity {uid} to {nomads.CurrentWeather}");
        }
    }

    /// <summary>
    /// Sets the weather for a map and adjusts the temperature for exposed tiles in the grid.
    /// </summary>
    private void SetWeatherAndTemperature(EntityUid uid, WeatherNomadsComponent component)
    {
        var weatherType = _weatherTypes.Values.FirstOrDefault(w => w.PrototypeId == component.CurrentWeather);
        if (weatherType == null)
        {
            Log.Warning($"Weather type for {component.CurrentWeather} not found");
            return;
        }

        var mapId = Transform(uid).MapID;
        var gridUid = GetGridUidForMap(mapId); // Get the grid for the map

        if (gridUid == null)
        {
            Log.Warning($"No grid found for map {mapId}");
            return;
        }

        // Apply the weather to the map
        if (!string.IsNullOrEmpty(weatherType.PrototypeId) && _prototypeManager.TryIndex<WeatherPrototype>(weatherType.PrototypeId, out var proto))
        {
            _weatherSystem.SetWeather(mapId, proto, null);
            Log.Debug($"Set weather {weatherType.PrototypeId} for map {mapId}");
        }
        else
        {
            _weatherSystem.SetWeather(mapId, null, null);
            Log.Debug($"Set no weather for map {mapId}");
        }

        // Randomize and apply temperature only to exposed tiles
        var temperature = (float)(weatherType.MinTemperature + (weatherType.MaxTemperature - weatherType.MinTemperature) * Random.Shared.NextDouble());
        SetGridTemperature(gridUid.Value, temperature);
    }

    /// <summary>
    /// Generates a random duration for a weather season based on component settings.
    /// </summary>
    private double GetRandomSeasonDuration(WeatherNomadsComponent component)
    {
        return Random.Shared.Next(component.MinSeasonMinutes, component.MaxSeasonMinutes + 1);
    }

    /// <summary>
    /// Adjusts the temperature of exposed tiles in a grid based on weather conditions.
    /// </summary>
    private void SetGridTemperature(EntityUid gridUid, float temperature)
    {
        // Verifica se o grid tem um GridAtmosphereComponent
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var gridAtmosphere))
        {
            Log.Warning($"Grid {gridUid} does not have a GridAtmosphereComponent");
            return;
        }

        // Obtém o componente MapGridComponent
        var grid = Comp<MapGridComponent>(gridUid);

        // Tenta obter o RoofComponent, se existir
        RoofComponent? roofComp = null;
        if (TryComp<RoofComponent>(gridUid, out var roofComponent))
        {
            roofComp = roofComponent;
        }

        // Itera sobre os tiles do grid
        foreach (var tile in gridAtmosphere.Tiles.Values)
        {
            var index = tile.GridIndices;
            var tileRef = grid.GetTileRef(index);

            // Verifica se o clima pode afetar este tile
            if (CanWeatherAffect(gridUid, grid, tileRef, roofComp))
            {
                if (tile.Air != null)
                {
                    var air = tile.Air;
                    // Se a mistura de gás é imutável, cria uma cópia mutável
                    if (air.Immutable)
                    {
                        var newAir = new GasMixture();
                        newAir.CopyFrom(air);
                        air = newAir;
                    }
                    air.Temperature = temperature;
                    // Atualiza a atmosfera do tile, se necessário
                    //_atmosphere.UpdateTile(gridUid, gridAtmosphere, tile);
                }
            }
        }
        Log.Debug($"Adjusted temperature for exposed tiles in grid {gridUid} to {temperature} K");
    }

    /// <summary>
    /// Determines if weather can affect a specific tile, based on roof coverage, tile type, and blocking entities.
    /// </summary>
    private bool CanWeatherAffect(EntityUid gridUid, MapGridComponent grid, TileRef tileRef, RoofComponent? roofComp)
    {
        // Se o tile está vazio, o clima pode afetá-lo
        if (tileRef.Tile.IsEmpty)
            return true;

        // Se há um RoofComponent e o tile está coberto, o clima não pode afetá-lo
        if (roofComp != null && _roofSystem.IsRooved((gridUid, grid, roofComp), tileRef.GridIndices))
            return false;

        // Verifica se o tipo de tile permite clima
        var tileDef = (ContentTileDefinition)_tileDefManager[tileRef.Tile.TypeId];
        if (!tileDef.Weather)
            return false;

        // Verifica se há entidades ancoradas que bloqueiam o clima
        var anchoredEntities = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tileRef.GridIndices);
        while (anchoredEntities.MoveNext(out var ent))
        {
            if (HasComp<BlockWeatherComponent>(ent.Value))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Retrieves the EntityUid of the grid associated with a given map ID.
    /// Assumes one grid per map for simplicity.
    /// </summary>
    private EntityUid? GetGridUidForMap(MapId mapId)
    {
        var grids = _mapManager.GetAllMapGrids(mapId);
        if (grids.Any())
        {
            return grids.First().Owner;
        }
        return null;
    }

    /// <summary>
    /// Gets the next season in the cycle.
    /// </summary>
    private string GetNextSeason(string current)
    {
        return current switch
        {
            "Spring" => "Summer",
            "Summer" => "Autumn",
            "Autumn" => "Winter",
            "Winter" => "Spring",
            _ => "Spring", // Default to Spring if something goes wrong
        };
    }
}
