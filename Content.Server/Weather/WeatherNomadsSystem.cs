using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Weather;

public sealed class WeatherNomadsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedWeatherSystem _weatherSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Subscribe to MapInitEvent to start weather when the map loads
        Log.Debug("WeatherNomadsSystem initialized successfully");
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        Log.Debug("WeatherNomadsSystem Update called");

        var query = EntityQueryEnumerator<WeatherNomadsComponent>();
        while (query.MoveNext(out var uid, out var nomads))
        {
            if (!HasComp<WeatherComponent>(uid))
            {
                Log.Warning($"Entity {uid} has WeatherNomadsComponent but lacks WeatherComponent");
                continue;
            }

            var currentTime = _timing.CurTime;
            Log.Debug($"Checking weather for entity {uid}: currentTime={currentTime}, lastSwitch={nomads.LastSwitchTime}, interval={nomads.Interval}, IsWeatherActive={nomads.IsWeatherActive}");

            if (currentTime - nomads.LastSwitchTime >= nomads.Interval)
            {
                Log.Debug($"Switching weather for entity {uid}, current IsWeatherActive={nomads.IsWeatherActive}");
                if (nomads.IsWeatherActive)
                {
                    _weatherSystem.SetWeather(Transform(uid).MapID, null, null);
                    nomads.IsWeatherActive = false;
                    Log.Debug($"Weather turned off for entity {uid}, new IsWeatherActive={nomads.IsWeatherActive}");
                }
                else
                {
                    if (_prototypeManager.TryIndex<WeatherPrototype>(nomads.WeatherPrototypeId, out var proto))
                    {
                        _weatherSystem.SetWeather(Transform(uid).MapID, proto, null);
                        nomads.IsWeatherActive = true;
                        Log.Debug($"Weather turned on for entity {uid} with prototype {nomads.WeatherPrototypeId}, new IsWeatherActive={nomads.IsWeatherActive}");
                    }
                    else
                    {
                        Log.Warning($"Failed to load weather prototype {nomads.WeatherPrototypeId} for entity {uid}");
                    }
                }
                nomads.LastSwitchTime = currentTime;
                Dirty(uid, nomads);
                Log.Debug($"Updated lastSwitchTime to {nomads.LastSwitchTime}");
            }
        }
    }
}