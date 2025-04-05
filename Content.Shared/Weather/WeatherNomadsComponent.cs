using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Weather;

[RegisterComponent, NetworkedComponent]
public sealed partial class WeatherNomadsComponent : Component
{
    [DataField("enabledWeathers")]
    public List<string> EnabledWeathers { get; set; } = new();

    [DataField("minSeasonMinutes")]
    public int MinSeasonMinutes { get; set; } = 10;

    [DataField("maxSeasonMinutes")]
    public int MaxSeasonMinutes { get; set; } = 30;

    [DataField("currentWeather")]
    public string CurrentWeather { get; set; } = "None";

    [DataField("nextSwitchTime")]
    public TimeSpan NextSwitchTime { get; set; } = TimeSpan.Zero;

    [DataField("nextSeasonChange")]
    public TimeSpan NextSeasonChange { get; set; } = TimeSpan.Zero;

    [DataField("currentSeason")]
    public string CurrentSeason { get; set; } = "Spring";
}
