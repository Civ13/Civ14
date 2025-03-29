using System.Linq; // Necessário para os métodos LINQ
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;

namespace Content.Server.Weather;

public sealed class WeatherNomadsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedWeatherSystem _weatherSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    private class WeatherType
    {
        public string? PrototypeId { get; set; }
        public int Weight { get; set; }
        public float MinTemperature { get; set; }
        public float MaxTemperature { get; set; }
    }

    private readonly Dictionary<string, WeatherType> _weatherTypes = new()
    {
        { "None", new WeatherType { PrototypeId = "", Weight = 0, MinTemperature = 293.15f, MaxTemperature = 293.15f } },
        { "Rain", new WeatherType { PrototypeId = "Rain", Weight = 1, MinTemperature = 278.15f, MaxTemperature = 288.15f } },
        { "Storm", new WeatherType { PrototypeId = "Storm", Weight = 2, MinTemperature = 273.15f, MaxTemperature = 278.15f } },
        { "SnowfallLight", new WeatherType { PrototypeId = "SnowfallLight", Weight = 3, MinTemperature = 268.15f, MaxTemperature = 273.15f } },
        { "SnowfallMedium", new WeatherType { PrototypeId = "SnowfallMedium", Weight = 4, MinTemperature = 258.15f, MaxTemperature = 268.15f } },
        { "SnowfallHeavy", new WeatherType { PrototypeId = "SnowfallHeavy", Weight = 5, MinTemperature = 243.15f, MaxTemperature = 258.15f } }
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeatherNomadsComponent, MapInitEvent>(OnMapInit);
        Log.Debug("WeatherNomadsSystem initialized successfully");
    }

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
            Dirty(uid, component);
            Log.Debug($"Weather started for entity {uid} with {component.CurrentWeather}");
        }
        else
        {
            Log.Warning($"No valid weather types enabled for entity {uid}");
        }
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WeatherNomadsComponent>();
        while (query.MoveNext(out var uid, out var nomads))
        {
            if (_timing.CurTime < nomads.NextSwitchTime)
                continue;

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

    private void SetWeatherAndTemperature(EntityUid uid, WeatherNomadsComponent component)
    {
        var weatherType = _weatherTypes.Values.FirstOrDefault(w => w.PrototypeId == component.CurrentWeather);
        if (weatherType == null)
        {
            Log.Warning($"Weather type for {component.CurrentWeather} not found");
            return;
        }

        var mapId = Transform(uid).MapID;

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

        var temperature = (float)(weatherType.MinTemperature + (weatherType.MaxTemperature - weatherType.MinTemperature) * Random.Shared.NextDouble());
        SetMapTemperature(mapId, temperature);
    }

    private double GetRandomSeasonDuration(WeatherNomadsComponent component)
    {
        return Random.Shared.Next(component.MinSeasonMinutes, component.MaxSeasonMinutes + 1);
    }

    private void SetMapTemperature(MapId mapId, float temperature)
    {
        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (mapUid != EntityUid.Invalid)
        {
            if (TryComp<MapAtmosphereComponent>(mapUid, out var mapAtmosphere))
            {
                var currentGas = mapAtmosphere.Mixture;
                if (currentGas.Immutable)
                {
                    var newGas = new GasMixture();
                    newGas.CopyFrom(currentGas);
                    currentGas = newGas;
                }
                currentGas.Temperature = temperature;
                _atmosphere.SetMapAtmosphere(mapUid, false, currentGas);
                Log.Debug($"Set map {mapId} temperature to {temperature} K");
            }
            else
            {
                Log.Warning($"Map {mapId} does not have a MapAtmosphereComponent");
            }
        }
        else
        {
            Log.Warning($"Failed to get map entity for MapId {mapId}");
        }
    }
}