using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Overlays;

/// <summary>
///     This component allows you to see faction icons above mobs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShowFactionIconsComponent : Component
{

    /// <summary>
    /// The faction icon to display
    /// </summary>
    [DataField("factionIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>))]
    public string FactionIcon = "HostileFaction";
}
[RegisterComponent, NetworkedComponent]
public sealed partial class ShowEnglishFactionIconsComponent : Component
{

    /// <summary>
    /// The faction icon to display
    /// </summary>
    [DataField("factionIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>))]
    public string FactionIcon = "EnglishFaction";
}
[RegisterComponent, NetworkedComponent]
public sealed partial class ShowFrenchFactionIconsComponent : Component
{

    /// <summary>
    /// The faction icon to display
    /// </summary>
    [DataField("factionIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>))]
    public string FactionIcon = "FrenchFaction";
}
[RegisterComponent, NetworkedComponent]
public sealed partial class ShowGermanFactionIconsComponent : Component
{

    /// <summary>
    /// The faction icon to display
    /// </summary>
    [DataField("factionIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>))]
    public string FactionIcon = "GermanFaction";
}
[RegisterComponent, NetworkedComponent]
public sealed partial class ShowSovietFactionIconsComponent : Component
{

    /// <summary>
    /// The faction icon to display
    /// </summary>
    [DataField("factionIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>))]
    public string FactionIcon = "SovietFaction";
}
