using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Mortar;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedMortarSystem))]
public sealed partial class MortarShellComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan LoadDelay = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public TimeSpan TravelDelay = TimeSpan.FromSeconds(8);

    [DataField, AutoNetworkedField]
    public TimeSpan ImpactWarningDelay = TimeSpan.FromSeconds(1.3);

    [DataField, AutoNetworkedField]
    public TimeSpan ImpactDelay = TimeSpan.FromSeconds(1.3);
}
