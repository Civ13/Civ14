using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.Teleport;

[Prototype("mapLoader"), Serializable, NetSerializable]
public sealed class MapLoaderPrototype : IPrototype
{
    [IdDataField, ViewVariables]
    public string ID { get; private set; } = default!;

    [DataField]
    public Dictionary<string, string> MapPaths = new();
}
