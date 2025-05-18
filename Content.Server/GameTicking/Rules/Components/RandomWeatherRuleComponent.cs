using Content.Shared.FixedPoint;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Gamerule that sets a random weather effect for this map.
/// </summary>
[RegisterComponent, Access(typeof(RandomWeatherRuleSystem))]
public sealed partial class RandomWeatherRuleComponent : Component
{

    [DataField("allowedWeathers")]
    public List<string> AllowedWeathers = ["Clear"];

    [DataField("currentWeather")]
    public string CurrentWeather = "Clear";

}