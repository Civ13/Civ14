using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Random;
using Content.Shared.Weather;
using Robust.Shared.Timing;
using System; // For TimeSpan
using Robust.Shared.Prototypes;
using Robust.Shared.Map;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// This handles the weather for a specific map
/// </summary>
public sealed class RandomWeatherRuleSystem : GameRuleSystem<RandomWeatherRuleComponent>
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    private ISawmill _sawmill = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("random-weather-rule");
        // The base GameRuleSystem<T> already subscribes to GameRuleStartedEvent
        // and calls the virtual Started() method. No need to subscribe manually here.
    }

    protected override void Started(EntityUid uid, RandomWeatherRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args); // It's good practice to call the base method.
        PickRandomWeather(uid, component);
    }

    /// <summary>
    /// Picks a random weather from the component's AllowedWeathers list and sets it as the CurrentWeather.
    /// </summary>
    public void PickRandomWeather(EntityUid uid, RandomWeatherRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.AllowedWeathers.Count == 0)
        {
            _sawmill.Warning($"AllowedWeathers list is empty for RandomWeatherRule on {ToPrettyString(uid)}. Rule will not pick a new weather.");
            return;
        }

        component.CurrentWeather = _random.Pick(component.AllowedWeathers);
        // Get the MapId from the entity's transform

        _sawmill.Info($"Selected weather: {component.CurrentWeather}");
        var endTime = _gameTiming.CurTime + TimeSpan.FromSeconds(3600); // Weather duration of 1 hour
        WeatherPrototype? weather = null;
        if (component.CurrentWeather != "Clear")
        {
            if (!_protoManager.TryIndex(component.CurrentWeather, out weather))
            {
                _sawmill.Error($"Unknown weather {component.CurrentWeather}!");
                return;
            }
        }
        foreach (var mapId in _mapManager.GetAllMapIds())
        {
            _weather.SetWeather(mapId, weather, endTime);
        }
    }
}
