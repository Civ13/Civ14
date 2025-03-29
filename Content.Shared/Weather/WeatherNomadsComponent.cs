using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Weather;

[RegisterComponent, NetworkedComponent]
public sealed partial class WeatherNomadsComponent : Component
{
    /// <summary>
    /// ID of the WeatherPrototype to be used when weather is active.
    /// </summary>
    [DataField("weatherPrototype")]
    public string WeatherPrototypeId { get; set; } = "SnowfallHeavy";

    /// <summary>
    /// Interval between weather switches in seconds.
    /// </summary>
    [DataField("interval")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Indicates whether the weather is currently active.
    /// </summary>
    [DataField("isWeatherActive")]
    public bool IsWeatherActive { get; set; } = false;

    /// <summary>
    /// Time of the last weather switch.
    /// </summary>
    [DataField("lastSwitchTime")]
    public TimeSpan LastSwitchTime { get; set; } = TimeSpan.Zero;
}
