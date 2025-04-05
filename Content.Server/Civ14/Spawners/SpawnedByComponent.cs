using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Spawners;

[RegisterComponent]
public sealed partial class SpawnedByComponent : Component
{
    [DataField("spawner")]
    public EntityUid Spawner; // UID to keep tracker of it's spawner
}