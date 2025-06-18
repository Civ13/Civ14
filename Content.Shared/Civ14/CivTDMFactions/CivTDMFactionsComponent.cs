using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization; // Added this line
using System; // Added for [Serializable]

namespace Content.Shared.Civ14.CivTDMFactions;

[AutoGenerateComponentState]
[RegisterComponent]
[NetworkedComponent]
public sealed partial class CivTDMFactionsComponent : Component
{
    //TODO: Consider making FactionId a prototype for more structured data (color, sprite, etc.)
    /// <summary>
    /// The name of faction1
    /// </summary>
    [DataField("faction1Id", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>)), AutoNetworkedField]
    public string? Faction1Id { get; set; }

    /// <summary>
    /// The name of faction2
    /// </summary>
    [DataField("faction2Id", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>)), AutoNetworkedField]
    public string? Faction2Id { get; set; }

    /// <summary>
    /// Squads belonging to faction 1. Key is squad name (e.g., "Alpha").
    /// </summary>
    [DataField("faction1Squads"), AutoNetworkedField]
    public Dictionary<string, SquadData> Faction1Squads { get; set; } = new()
    {
        { "Alpha", new SquadData() },
        { "Bravo", new SquadData() },
        { "Charlie", new SquadData() }
    };

    /// <summary>
    /// Squads belonging to faction 2. Key is squad name (e.g., "Alpha").
    /// </summary>
    [DataField("faction2Squads"), AutoNetworkedField]
    public Dictionary<string, SquadData> Faction2Squads { get; set; } = new()
    {
        { "Alpha", new SquadData() },
        { "Bravo", new SquadData() },
        { "Charlie", new SquadData() }
    };
}

/// <summary>
/// Holds data for a single squad, like member counts.
/// </summary>
[DataDefinition, NetSerializable, Serializable]
public sealed partial class SquadData
{
    [DataField("sergeantCount")] // Removed AutoNetworkedField
    public int SergeantCount { get; set; } = 0;

    [DataField("memberCount")] // Removed AutoNetworkedField
    public int MemberCount { get; set; } = 0;

    // You could add MaxSize here if it's per-squad rather than global from ShowFactionIconsSystem
    // [DataField("maxSize")]
    // public int MaxSize { get; set; } = 3;
}
