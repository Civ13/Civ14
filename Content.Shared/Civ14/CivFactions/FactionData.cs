using System;
using Content.Shared.Clothing.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using System.Collections.Generic; // Required for List

namespace Content.Shared.Civ14.CivFactions;

// Changed from Component to a DataDefinition for use in lists.
[DataDefinition, Serializable, NetSerializable]
public sealed partial class FactionData
{
    /// <summary>
    /// The name of the faction.
    /// </summary>
    [DataField("factionName")]
    public string FactionName { get; set; } = "Unnamed Faction";
    /// <summary>
    /// The list of members, using the ckeys.
    /// </summary>
    [DataField("factionMembers")]
    public List<string> FactionMembers { get; set; } = new List<string>();
    /// <summary>
    /// The current research level of the faction.
    /// </summary>
    [DataField("factionResearch")]
    public float FactionResearch { get; set; } = 0f;
    /// <summary>
    /// The score of the faction.
    /// </summary>
    [DataField("factionPoints")]
    public int FactionPoints { get; set; } = 0;
    /// <summary>
    /// The ammount of money in the faction's treasury.
    /// </summary>
    [DataField("factionTreasury")]
    public float FactionTreasury { get; set; } = 0f;
    /// <summary>
    /// People registered as leaders of the faction (can invite others)
    /// </summary>
    [DataField("factionLeaders")]
    public List<string> FactionLeaders { get; set; } = new List<string>();
}

