using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;
using System.Collections.Generic;
using Content.Shared.Tag;

namespace Content.Shared.Barricade;

[RegisterComponent]
public sealed partial class BarricadeComponent : Component
{
    /// <summary>
    /// % chance of blocking a projectile passing overhead
    /// </summary>
    [DataField("blocking")]
    public int Blocking = 66;

    /// <summary>
    /// Can it be used bidirectionally (e.g. sandbags) or only from behind (e.g. crenelated walls)?
    /// </summary>
    [DataField("bidirectional")]
    public bool Bidirectional = true;



}
