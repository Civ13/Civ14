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
    /// <summary>
    /// If this role is part of one of the squads
    /// </summary>
    [DataField("assignSquad"), AutoNetworkedField]
    public bool AssignSquad { get; set; } = false;
    /// <summary>
    /// The specific squad icon (e.g., "JobIconSquadAlphaSergeant") assigned by the server.
    /// </summary>
    [DataField("squadIcon", customTypeSerializer: typeof(PrototypeIdSerializer<JobIconPrototype>)), AutoNetworkedField]
    public string? SquadIcon { get; set; }

    /// <summary>
    /// The key/name of the squad the entity is assigned to (e.g., "Alpha").
    /// </summary>
    [DataField("assignedSquadNameKey"), AutoNetworkedField]
    public string? AssignedSquadNameKey { get; set; }

    [DataField("isSergeantInSquad"), AutoNetworkedField]
    public bool IsSergeantInSquad { get; set; }

    /// <summary>
    /// Identifier for the major CivTeamDeathmatch Faction this entity belongs to (e.g., Faction1Id or Faction2Id from CivTDMFactionsComponent).
    /// </summary>
    [DataField("belongsToCivFactionId"), AutoNetworkedField]
    public string? BelongsToCivFactionId { get; set; }
}
