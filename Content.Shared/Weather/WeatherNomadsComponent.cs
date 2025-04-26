using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared.Weather;

[RegisterComponent, NetworkedComponent]
public sealed partial class WeatherNomadsComponent : Component
{
    [DataField("enabledWeathers")]
    public HashSet<string> EnabledWeathers { get; set; } = new();

    [DataField("minSeasonMinutes")]
    public int MinSeasonMinutes { get; set; } = 30;

    [DataField("maxSeasonMinutes")]
    public int MaxSeasonMinutes { get; set; } = 45;

    [DataField("minPrecipitationDurationMinutes")]
    public int MinPrecipitationDurationMinutes { get; set; } = 5;

    [DataField("maxPrecipitationDurationMinutes")]
    public int MaxPrecipitationDurationMinutes { get; set; } = 10;

    [DataField("currentPrecipitation")]
    public Precipitation CurrentPrecipitation { get; set; } = Precipitation.LightWet;

    [DataField("currentWeather")]
    public string CurrentWeather { get; set; } = "Clear";

    [DataField("nextSwitchTime", customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan NextSwitchTime { get; set; } = TimeSpan.Zero;

    [DataField("nextSeasonChange", customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan NextSeasonChange { get; set; } = TimeSpan.Zero;

    [DataField("currentSeason")]
    public string CurrentSeason { get; set; } = "Spring";
}
