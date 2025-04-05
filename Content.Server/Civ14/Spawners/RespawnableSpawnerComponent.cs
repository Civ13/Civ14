using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Server.Spawners;

[RegisterComponent]
public sealed partial class RespawnableSpawnerComponent : Component
{
    [DataField("prototypes")]
    public List<string> Prototypes = new(); //Enities proto to be spawned

    [DataField("minDelay"), ViewVariables(VVAccess.ReadWrite)]
    public float MinDelay = 60f; // Minimum delay in seconds

    [DataField("maxDelay"), ViewVariables(VVAccess.ReadWrite)]
    public float MaxDelay = 300f; // Maximum delay in seconds

    [ViewVariables]
    public Dictionary<EntityUid, float> RespawnTimers = new();
}