using System.Linq;
using Robust.Shared.Map;

namespace Content.Shared._Stalker.Teleport;

public abstract class SharedTeleportSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IMapManager _mapMan = default!;
    protected void TeleportEntity(EntityUid entity, EntityCoordinates coords, bool reParent = true)
    {
        // raise before event so other systems can handle this
        var originMapId = Transform(entity).MapID;
        var destinationMapId = coords.GetMapId(EntityManager);
        var beforeEvent = new BeforeEntityTeleportedEvent(entity, originMapId, destinationMapId);
        RaiseLocalEvent(ref beforeEvent);

        _xform.SetCoordinates(entity, coords);

        var map = coords.GetMapUid(EntityManager);
        if (map != null && reParent)
            _xform.SetParent(entity, map.Value);

        // raise after event for other systems to perform cleanup
        var ev = new AfterEntityTeleportedEvent(entity, originMapId, destinationMapId);
        RaiseLocalEvent(ref ev);
    }
}

[ByRefEvent]
public record struct AfterEntityTeleportedEvent(EntityUid EntityUid, MapId Origin, MapId Destination);
[ByRefEvent]
public record struct BeforeEntityTeleportedEvent(EntityUid EntityUid, MapId Origin, MapId Destination);
