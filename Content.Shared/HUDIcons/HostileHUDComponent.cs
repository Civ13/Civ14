using Robust.Shared.Audio;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;


/// <summary>
/// This is used for tagging a mob as hostile.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HostileHUDComponent : Component
{

    /// <summary>
    ///
    /// </summary>
    [DataField("statusIcon", customTypeSerializer: typeof(PrototypeIdSerializer<FactionIconPrototype>))]
    public string StatusIcon = "HostileFaction";
}
