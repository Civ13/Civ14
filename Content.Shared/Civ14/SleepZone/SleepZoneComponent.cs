using Robust.Shared.Map;

namespace Content.Shared.Civ14.SleepZone;

[RegisterComponent]
public sealed partial class SleepZoneComponent : Component
{
    /// <summary>
    /// The original coordinates the entity was teleported from.
    /// </summary>
    [DataField("origin")]
    public MapCoordinates Origin = MapCoordinates.Nullspace;
    /// <summary>
    /// Is the entity currently in the sleep zone?
    /// </summary>
    [DataField("isSleeping")]
    public bool IsSleeping = false;
}
