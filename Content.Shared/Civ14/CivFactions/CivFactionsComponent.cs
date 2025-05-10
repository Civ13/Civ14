using System;
using Content.Shared.Clothing.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Civ14.CivFactions;

[RegisterComponent]
[NetworkedComponent]
public sealed partial class CivFactionsComponent : Component
{
    /// <summary>
    /// The list of current factions in the game.
    /// </summary>
    [DataField("factionList")]
    public List<CivFactionComponent> FactionList { get; set; } = new();

}

