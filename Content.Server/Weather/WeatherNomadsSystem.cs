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
using Content.Shared.Maps;
using Robust.Shared.Map.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Light.Components;
using System;

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
        public string? PrototypeId { get; set; } // ID of the weather prototype, null for "Clear"
        public int Weight { get; set; }          // Weight for weather transition order (unused now, kept for compatibility)
        public float MinTemperature { get; set; } // Minimum temperature in Kelvin
        public float MaxTemperature { get; set; } // Maximum temperature in Kelvin
    }

    /// <summary>
    /// Dictionary defining available weather types and their properties.
    /// </summary>
    private readonly Dictionary<string, WeatherType> _weatherTypes = new()
    {
        { "Clear", new WeatherType { PrototypeId = "", Weight = 0, MinTemperature = 293.15f, MaxTemperature = 293.15f } },
        { "Rain", new WeatherType { PrototypeId = "Rain", Weight = 1, MinTemperature = 278.15f, MaxTemperature = 288.15f } },
        { "Storm", new WeatherType { PrototypeId = "Storm", Weight = 3, MinTemperature = 273.15f, MaxTemperature = 278.15f } },
        { "SnowfallLight", new WeatherType { PrototypeId = "SnowfallLight", Weight = 4, MinTemperature = 268.15f, MaxTemperature = 273.15f } },
        { "SnowfallMedium", new WeatherType { PrototypeId = "SnowfallMedium", Weight = 5, MinTemperature = 258.15f, MaxTemperature = 268.15f } },
        { "SnowfallHeavy", new WeatherType { PrototypeId = "SnowfallHeavy", Weight = 6, MinTemperature = 243.15f, MaxTemperature = 258.15f } },
        { "Hail", new WeatherType { PrototypeId = "Hail", Weight = 7, MinTemperature = 273.15f, MaxTemperature = 278.15f } },
        { "Sandstorm", new WeatherType { PrototypeId = "Sandstorm", Weight = 9, MinTemperature = 293.15f, MaxTemperature = 313.15f } },
        { "SandstormHeavy", new WeatherType { PrototypeId = "SandstormHeavy", Weight = 10, MinTemperature = 293.15f, MaxTemperature = 313.15f } },
    };

    /// <summary>
    /// Dictionary mapping (Biome, Season, Precipitation) to specific weather types.
    /// </summary>
    private static readonly Dictionary<(Biome, string, Precipitation), string> _weatherTransitionMap = new()
    {
        // Summer
        { (Biome.Tundra, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Tundra, "Summer", Precipitation.LightWet), "SnowfallLight" },
        { (Biome.Tundra, "Summer", Precipitation.HeavyWet), "SnowfallMedium" },
        { (Biome.Tundra, "Summer", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Taiga, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Taiga, "Summer", Precipitation.LightWet), "Rain" },
        { (Biome.Taiga, "Summer", Precipitation.HeavyWet), "Rain" },
        { (Biome.Taiga, "Summer", Precipitation.Storm), "Hail" },
        { (Biome.Temperate, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Temperate, "Summer", Precipitation.LightWet), "Rain" },
        { (Biome.Temperate, "Summer", Precipitation.HeavyWet), "Rain" },
        { (Biome.Temperate, "Summer", Precipitation.Storm), "Storm" },
        { (Biome.Sea, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Sea, "Summer", Precipitation.LightWet), "Rain" },
        { (Biome.Sea, "Summer", Precipitation.HeavyWet), "Rain" },
        { (Biome.Sea, "Summer", Precipitation.Storm), "Storm" },
        { (Biome.SemiArid, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.SemiArid, "Summer", Precipitation.LightWet), "Clear" },
        { (Biome.SemiArid, "Summer", Precipitation.HeavyWet), "Rain" },
        { (Biome.SemiArid, "Summer", Precipitation.Storm), "Rain" },
        { (Biome.Desert, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Desert, "Summer", Precipitation.LightWet), "Clear" },
        { (Biome.Desert, "Summer", Precipitation.HeavyWet), "Sandstorm" },
        { (Biome.Desert, "Summer", Precipitation.Storm), "SandstormHeavy" },
        { (Biome.Savanna, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Savanna, "Summer", Precipitation.LightWet), "Clear" },
        { (Biome.Savanna, "Summer", Precipitation.HeavyWet), "Rain" },
        { (Biome.Savanna, "Summer", Precipitation.Storm), "Storm" },
        { (Biome.Jungle, "Summer", Precipitation.Dry), "Clear" },
        { (Biome.Jungle, "Summer", Precipitation.LightWet), "Rain" },
        { (Biome.Jungle, "Summer", Precipitation.HeavyWet), "Storm" },
        { (Biome.Jungle, "Summer", Precipitation.Storm), "Storm" },

        // Spring
        { (Biome.Tundra, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Tundra, "Spring", Precipitation.LightWet), "SnowfallLight" },
        { (Biome.Tundra, "Spring", Precipitation.HeavyWet), "SnowfallMedium" },
        { (Biome.Tundra, "Spring", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Taiga, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Taiga, "Spring", Precipitation.LightWet), "Rain" },
        { (Biome.Taiga, "Spring", Precipitation.HeavyWet), "SnowfallLight" },
        { (Biome.Taiga, "Spring", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Temperate, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Temperate, "Spring", Precipitation.LightWet), "Rain" },
        { (Biome.Temperate, "Spring", Precipitation.HeavyWet), "Storm" },
        { (Biome.Temperate, "Spring", Precipitation.Storm), "SnowfallMedium" },
        { (Biome.Sea, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Sea, "Spring", Precipitation.LightWet), "Rain" },
        { (Biome.Sea, "Spring", Precipitation.HeavyWet), "Rain" },
        { (Biome.Sea, "Spring", Precipitation.Storm), "Storm" },
        { (Biome.SemiArid, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.SemiArid, "Spring", Precipitation.LightWet), "Clear" },
        { (Biome.SemiArid, "Spring", Precipitation.HeavyWet), "Rain" },
        { (Biome.SemiArid, "Spring", Precipitation.Storm), "Rain" },
        { (Biome.Desert, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Desert, "Spring", Precipitation.LightWet), "Clear" },
        { (Biome.Desert, "Spring", Precipitation.HeavyWet), "Rain" },
        { (Biome.Desert, "Spring", Precipitation.Storm), "Sandstorm" },
        { (Biome.Savanna, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Savanna, "Spring", Precipitation.LightWet), "Clear" },
        { (Biome.Savanna, "Spring", Precipitation.HeavyWet), "Rain" },
        { (Biome.Savanna, "Spring", Precipitation.Storm), "Storm" },
        { (Biome.Jungle, "Spring", Precipitation.Dry), "Clear" },
        { (Biome.Jungle, "Spring", Precipitation.LightWet), "Rain" },
        { (Biome.Jungle, "Spring", Precipitation.HeavyWet), "Rain" },
        { (Biome.Jungle, "Spring", Precipitation.Storm), "Storm" },

        // Autumn
        { (Biome.Tundra, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Tundra, "Autumn", Precipitation.LightWet), "SnowfallLight" },
        { (Biome.Tundra, "Autumn", Precipitation.HeavyWet), "SnowfallMedium" },
        { (Biome.Tundra, "Autumn", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Taiga, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Taiga, "Autumn", Precipitation.LightWet), "Rain" },
        { (Biome.Taiga, "Autumn", Precipitation.HeavyWet), "SnowfallLight" },
        { (Biome.Taiga, "Autumn", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Temperate, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Temperate, "Autumn", Precipitation.LightWet), "Rain" },
        { (Biome.Temperate, "Autumn", Precipitation.HeavyWet), "Storm" },
        { (Biome.Temperate, "Autumn", Precipitation.Storm), "SnowfallMedium" },
        { (Biome.Sea, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Sea, "Autumn", Precipitation.LightWet), "Rain" },
        { (Biome.Sea, "Autumn", Precipitation.HeavyWet), "Rain" },
        { (Biome.Sea, "Autumn", Precipitation.Storm), "Storm" },
        { (Biome.SemiArid, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.SemiArid, "Autumn", Precipitation.LightWet), "Clear" },
        { (Biome.SemiArid, "Autumn", Precipitation.HeavyWet), "Rain" },
        { (Biome.SemiArid, "Autumn", Precipitation.Storm), "Rain" },
        { (Biome.Desert, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Desert, "Autumn", Precipitation.LightWet), "Clear" },
        { (Biome.Desert, "Autumn", Precipitation.HeavyWet), "Rain" },
        { (Biome.Desert, "Autumn", Precipitation.Storm), "Sandstorm" },
        { (Biome.Savanna, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Savanna, "Autumn", Precipitation.LightWet), "Clear" },
        { (Biome.Savanna, "Autumn", Precipitation.HeavyWet), "Rain" },
        { (Biome.Savanna, "Autumn", Precipitation.Storm), "Storm" },
        { (Biome.Jungle, "Autumn", Precipitation.Dry), "Clear" },
        { (Biome.Jungle, "Autumn", Precipitation.LightWet), "Rain" },
        { (Biome.Jungle, "Autumn", Precipitation.HeavyWet), "Rain" },
        { (Biome.Jungle, "Autumn", Precipitation.Storm), "Storm" },

        // Winter
        { (Biome.Tundra, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Tundra, "Winter", Precipitation.LightWet), "SnowfallMedium" },
        { (Biome.Tundra, "Winter", Precipitation.HeavyWet), "SnowfallHeavy" },
        { (Biome.Tundra, "Winter", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Taiga, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Taiga, "Winter", Precipitation.LightWet), "SnowfallLight" },
        { (Biome.Taiga, "Winter", Precipitation.HeavyWet), "SnowfallHeavy" },
        { (Biome.Taiga, "Winter", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Temperate, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Temperate, "Winter", Precipitation.LightWet), "SnowfallLight" },
        { (Biome.Temperate, "Winter", Precipitation.HeavyWet), "SnowfallMedium" },
        { (Biome.Temperate, "Winter", Precipitation.Storm), "SnowfallHeavy" },
        { (Biome.Sea, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Sea, "Winter", Precipitation.LightWet), "Rain" },
        { (Biome.Sea, "Winter", Precipitation.HeavyWet), "Storm" },
        { (Biome.Sea, "Winter", Precipitation.Storm), "Storm" },
        { (Biome.SemiArid, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.SemiArid, "Winter", Precipitation.LightWet), "Rain" },
        { (Biome.SemiArid, "Winter", Precipitation.HeavyWet), "Rain" },
        { (Biome.SemiArid, "Winter", Precipitation.Storm), "Storm" },
        { (Biome.Desert, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Desert, "Winter", Precipitation.LightWet), "Clear" },
        { (Biome.Desert, "Winter", Precipitation.HeavyWet), "Rain" },
        { (Biome.Desert, "Winter", Precipitation.Storm), "Rain" },
        { (Biome.Savanna, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Savanna, "Winter", Precipitation.LightWet), "Rain" },
        { (Biome.Savanna, "Winter", Precipitation.HeavyWet), "Storm" },
        { (Biome.Savanna, "Winter", Precipitation.Storm), "Storm" },
        { (Biome.Jungle, "Winter", Precipitation.Dry), "Clear" },
        { (Biome.Jungle, "Winter", Precipitation.LightWet), "Rain" },
        { (Biome.Jungle, "Winter", Precipitation.HeavyWet), "Storm" },
        { (Biome.Jungle, "Winter", Precipitation.Storm), "Storm" },
    };

    /// <summary>
    /// Initializes the system and subscribes to relevant events.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeatherNomadsComponent, MapInitEvent>(OnMapInit);
    }

    /// <summary>
    /// Handles the initialization of weather for a map when it is first created.
    /// </summary>
    private void OnMapInit(EntityUid uid, WeatherNomadsComponent component, MapInitEvent args)
    {
        component.CurrentPrecipitation = Precipitation.Dry;
        component.CurrentWeather = "Clear";
        component.NextSwitchTime = _timing.CurTime + TimeSpan.FromMinutes(GetRandomPrecipitationDuration(component));
        component.NextSeasonChange = _timing.CurTime + TimeSpan.FromMinutes(GetRandomSeasonDuration(component));

        Dirty(uid, component);
        UpdateTileWeathers(uid, component);
        _chat.DispatchGlobalAnnouncement($"Current season: {component.CurrentSeason}", "World", false, null, null);
    }

    /// <summary>
    /// Updates the weather system periodically, switching precipitation and season states as needed.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WeatherNomadsComponent>();
        while (query.MoveNext(out var uid, out var nomads))
        {
            // Handle season changes
            if (_timing.CurTime >= nomads.NextSeasonChange)
            {
                var oldSeason = nomads.CurrentSeason;
                nomads.CurrentSeason = GetNextSeason(nomads.CurrentSeason);
                nomads.NextSeasonChange = _timing.CurTime + TimeSpan.FromMinutes(GetRandomSeasonDuration(nomads));
                Dirty(uid, nomads);
                _chat.DispatchGlobalAnnouncement($"Changed season to {nomads.CurrentSeason}", null, false, null, null);
                UpdateTileWeathers(uid, nomads);
            }

            // Handle precipitation changes
            if (_timing.CurTime < nomads.NextSwitchTime)
            {
                continue;
            }

            var oldPrecipitation = nomads.CurrentPrecipitation;
            nomads.CurrentPrecipitation = GetNextPrecipitation(nomads.CurrentPrecipitation);
            nomads.NextSwitchTime = _timing.CurTime + TimeSpan.FromMinutes(GetRandomPrecipitationDuration(nomads));
            Dirty(uid, nomads);
            UpdateTileWeathers(uid, nomads);
        }

    }

    /// <summary>
    /// Updates weather effects for each tile based on biome, season, and global precipitation.
    /// </summary>
    private void UpdateTileWeathers(EntityUid uid, WeatherNomadsComponent nomads)
    {
        var mapId = Transform(uid).MapID;
        var gridUid = GetGridUidForMap(mapId);
        if (gridUid == null)
        {
            Log.Warning($"No grid found for map {mapId}");
            return;
        }

        if (!TryComp<MapGridComponent>(gridUid.Value, out var grid))
            return;

        if (!TryComp<GridAtmosphereComponent>(gridUid.Value, out var gridAtmosphere))
            return;

        RoofComponent? roofComp = TryComp<RoofComponent>(gridUid.Value, out var rc) ? rc : null;

        foreach (var tile in gridAtmosphere.Tiles.Values)
        {
            var tileRef = grid.GetTileRef(tile.GridIndices);
            if (tileRef.Tile.IsEmpty)
                continue; // Skip empty tiles

            // Get biome from tile definition
            var tileDef = (ContentTileDefinition)_tileDefManager[tileRef.Tile.TypeId];
            if (!Enum.TryParse<Biome>(tileDef.Biome, true, out var biome))
            {
                biome = Biome.Temperate; // Fallback to Temperate if biome string is invalid
                Log.Warning($"Invalid biome '{tileDef.Biome}' for tile at {tileRef.GridIndices}, defaulting to Temperate");
            }

            if (_weatherTransitionMap.TryGetValue((biome, nomads.CurrentSeason, nomads.CurrentPrecipitation), out var weatherType))
            {
                ApplyWeatherToTile(uid, nomads, gridUid.Value, tileRef, weatherType, grid, gridAtmosphere, roofComp);
            }
            else
            {
                Log.Warning($"No weather mapping found for Biome: {biome}, Season: {nomads.CurrentSeason}, Precipitation: {nomads.CurrentPrecipitation}");
            }
        }
    }

    /// <summary>
    /// Applies weather effects and temperature to a specific tile.
    /// </summary>
    private void ApplyWeatherToTile(EntityUid weatherUid, WeatherNomadsComponent nomads, EntityUid gridUid, TileRef tileRef, string weatherType, MapGridComponent grid,
        GridAtmosphereComponent gridAtmosphere, RoofComponent? roofComp)
    {
        if (!CanWeatherAffect(gridUid, grid, tileRef, roofComp))
            return;

        var tile = gridAtmosphere.Tiles[tileRef.GridIndices];
        if (tile.Air == null)
            return;

        if (!_weatherTypes.TryGetValue(weatherType, out var weatherData))
        {
            Log.Warning($"Weather type {weatherType} not found in _weatherTypes");
            return;
        }

        // Update CurrentWeather if it has changed
        if (nomads.CurrentWeather != weatherType)
        {
            nomads.CurrentWeather = weatherType;
            Dirty(weatherUid, nomads);
        }

        // Apply weather visuals globally
        var mapId = Transform(gridUid).MapID;
        if (!string.IsNullOrEmpty(weatherData.PrototypeId) &&
            _prototypeManager.TryIndex<WeatherPrototype>(weatherData.PrototypeId, out var proto))
        {
            _weatherSystem.SetWeather(mapId, proto, null);
        }
        else
        {
            _weatherSystem.SetWeather(mapId, null, null);
        }

        // Adjust temperature
        var temperature = (float)(weatherData.MinTemperature +
            (weatherData.MaxTemperature - weatherData.MinTemperature) * Random.Shared.NextDouble());
        var air = tile.Air;
        if (air.Immutable)
        {
            var newAir = new GasMixture();
            newAir.CopyFrom(air);
            air = newAir;
        }
        air.Temperature = temperature;

    }

    /// <summary>
    /// Gets the next precipitation state in the cycle.
    /// </summary>
    private Precipitation GetNextPrecipitation(Precipitation current)
    {
        return current switch
        {
            Precipitation.Dry => Precipitation.LightWet,
            Precipitation.LightWet => Precipitation.HeavyWet,
            Precipitation.HeavyWet => Precipitation.Storm,
            Precipitation.Storm => Precipitation.Dry,
            _ => Precipitation.Dry // Default to Dry if something goes wrong
        };
    }

    /// <summary>
    /// Generates a random duration for a season based on component settings.
    /// </summary>
    private double GetRandomSeasonDuration(WeatherNomadsComponent component)
    {
        var duration = Random.Shared.Next(component.MinSeasonMinutes, component.MaxSeasonMinutes + 1);
        return duration;
    }

    /// <summary>
    /// Generates a random duration for a precipitation change based on component settings.
    /// </summary>
    private double GetRandomPrecipitationDuration(WeatherNomadsComponent component)
    {
        var duration = Random.Shared.Next(component.MinPrecipitationDurationMinutes, component.MaxPrecipitationDurationMinutes + 1);
        return duration;
    }

    /// <summary>
    /// Determines if weather can affect a specific tile, based on roof coverage, tile type, and blocking entities.
    /// </summary>
    private bool CanWeatherAffect(EntityUid gridUid, MapGridComponent grid, TileRef tileRef, RoofComponent? roofComp)
    {
        if (tileRef.Tile.IsEmpty)
            return true;

        if (roofComp != null && _roofSystem.IsRooved((gridUid, grid, roofComp), tileRef.GridIndices))
            return false;

        var tileDef = (ContentTileDefinition)_tileDefManager[tileRef.Tile.TypeId];
        if (!tileDef.Weather)
            return false;

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
        return grids.Any() ? grids.First().Owner : null;
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
            _ => "Spring" // Default to Spring if something goes wrong
        };
    }
}