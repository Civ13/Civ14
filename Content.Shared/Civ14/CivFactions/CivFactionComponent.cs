using Robust.Shared.GameStates;

namespace Content.Shared.Civ14.CivFactions;

[RegisterComponent, NetworkedComponent]
public sealed partial class CivFactionComponent : Component
{
    /// <summary>
    /// The total weight of the entity, which is calculated
    /// by recursive passes over all children with this component
    /// </summary>
    [ViewVariables]
    public string FactionName { get; set; } = "";

    public void SetFaction(string factionName)
    {
        FactionName = factionName;

    }
}
