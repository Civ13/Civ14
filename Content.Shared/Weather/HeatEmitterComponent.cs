using Content.Shared.Atmos;

namespace Content.Server.Weather;

[RegisterComponent]
public sealed partial class HeatEmitterComponent : Component
{
    /// <summary>
    /// Heating rate in Kelvin per second.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float HeatingRate = 0.5f;

    /// <summary>
    /// Radius in tiles around the entity where heat is applied.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Radius = 1;
}