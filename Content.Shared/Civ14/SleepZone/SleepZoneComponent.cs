using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared.Civ14.SleepZone;
/// <summary>
/// Enables an entity to go to sleep in the safezone.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SleepZoneComponent : Component
{
    /// <summary>
    /// The original coordinates the entity was teleported from.
    /// </summary>
    [DataField("origin"), NeverPushInheritance] // Prevent prototype system from copying this during creation
    public EntityCoordinates? Origin; // Needs to be nullable
    /// <summary>
    /// Is the entity currently in the sleep zone?
    /// </summary>
    [DataField("isSleeping")]
    public bool IsSleeping = false;
}
