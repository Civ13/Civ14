using System;
using Content.Shared.Clothing.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Civ14.CivFactions;

[AutoGenerateComponentState] // Add this attribute
[RegisterComponent]
[NetworkedComponent]
public sealed partial class CivFactionsComponent : Component
{
    /// <summary>
    /// The list of current factions in the game.
    /// </summary>
    [DataField("factionList"), AutoNetworkedField]
    public List<FactionData> FactionList { get; set; } = new(); // <-- Use FactionData
    /// <summary>
    /// Check if the faction rule is enabled.
    /// </summary>
    [DataField("factionsActive")]
    public bool FactionsActive { get; set; } = true;
}
