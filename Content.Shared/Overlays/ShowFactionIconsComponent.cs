using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Overlays;

/// <summary>
///     This component allows you to see faction icons above mobs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShowFactionIconsComponent : Component
{

    /// <summary>
    /// The faction icon to display
    /// </summary>
    [DataField("factionIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>)), AutoNetworkedField]
    public string FactionIcon { get; set; } = "HostileFaction";
    /// <summary>
    /// The job icon to display (if any)
    /// </summary>
    [DataField("jobIcon", customTypeSerializer: typeof(PrototypeIdSerializer<JobIconPrototype>)), AutoNetworkedField]
    public string JobIcon { get; set; } = "JobIconSoldier";
}
