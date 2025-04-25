using Content.Shared.Coordinates;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.Log; // Added for ILogManager and ISawmill
using Robust.Shared.IoC; // Added for Dependency attribute
using Robust.Shared.GameStates; // Needed for [RegisterComponent] if SleepZoneComponent wasn't partial
using Robust.Shared.Serialization.Manager.Attributes; // Needed for [DataField]

namespace Content.Shared.Civ14.SleepZone;
public sealed partial class SleepZoneSystem : EntitySystem
{
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!; // Keep this for SetCoordinates
    [Dependency] private readonly IEntityManager _entities = default!;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _log.GetSawmill("sleepzone");
    }

    /// <summary>
    /// Tries to find the first entity with the prototype "SleepZoneBed".
    /// </summary>
    /// <param name="bedId">The EntityUid of the found bed, or EntityUid.Invalid if none was found.</param>
    /// <returns>True if a bed was found, false otherwise.</returns>
    public bool TryFindSleepZoneBed(out EntityUid bedId)
    {
        const string bedPrototypeId = "SleepZoneBed";
        // Use EntityQuery with MetaDataComponent for prototype checking
        var query = EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            // Check if the prototype exists and its ID matches
            if (meta.EntityPrototype?.ID == bedPrototypeId)
            {
                bedId = uid;
                return true;
            }
        }
        bedId = EntityUid.Invalid;
        return false;
    }


    public void StartSleep(EntityUid entity)
    {
        // Use TryComp for cleaner component checking
        if (!_entities.TryGetComponent<SleepZoneComponent>(entity, out var sleepZone))
        {
            _sawmill.Debug($"Entity {entity} does not have a SleepZoneComponent, cannot start sleep.");
            return;
        }

        if (sleepZone.IsSleeping)
        {
            _sawmill.Debug($"Entity {entity} is already sleeping.");
            return;
        }

        // --- FIX: Use Transform(uid).Coordinates to get EntityCoordinates ---
        // The Transform() helper method is provided by EntitySystem.
        sleepZone.Origin = _xform.GetMapCoordinates(entity);
        // ---

        _sawmill.Info($"Saved origin {sleepZone.Origin} for entity {entity}.");


        if (TryTeleportToBed(entity))
        {
            sleepZone.IsSleeping = true;
            _sawmill.Info($"Entity {entity} started sleeping successfully.");
        }
        else
        {
            _sawmill.Warning($"Entity {entity} failed to start sleeping because teleportation to bed failed.");
            // Reset origin if teleport fails
        }
    }

    private bool TryTeleportToBed(EntityUid entityToTeleport)
    {
        if (TryFindSleepZoneBed(out var bedEntity))
        {
            // Use EntityExists for clarity
            if (!_entities.EntityExists(bedEntity))
            {
                _sawmill.Warning($"Found bed {bedEntity} but it no longer exists.");
                return false;
            }

            // --- FIX: Use Transform(uid).Coordinates to get EntityCoordinates ---
            var bedCoords = _xform.GetMapCoordinates(bedEntity);
            // ---

            _sawmill.Info($"Found bed {bedEntity} at {bedCoords}, teleporting {entityToTeleport}.");
            var parsedBedCoords = _xform.ToCoordinates(bedCoords);
            // Use _xform for SetCoordinates as it's the dedicated system for transform manipulation
            _xform.SetCoordinates(entityToTeleport, parsedBedCoords);
            return true; // Teleport successful
        }
        else
        {
            _sawmill.Warning($"Could not find any entity with prototype 'SleepZoneBed' to teleport {entityToTeleport} to.");
            return false;
        }
    }

    public void WakeUp(EntityUid entity)
    {
        // Use TryComp for cleaner component checking
        if (_entities.TryGetComponent<SleepZoneComponent>(entity, out var sleepZone))
        {
            if (!sleepZone.IsSleeping)
            {
                _sawmill.Debug($"Entity {entity} is not sleeping, cannot wake up.");
                return;
            }


            _sawmill.Info($"Waking up entity {entity}, returning to {sleepZone.Origin}.");

            // Use _xform for SetCoordinates
            _xform.SetCoordinates(entity, _xform.ToCoordinates(sleepZone.Origin));

            sleepZone.IsSleeping = false;
        }
        else
        {
            _sawmill.Debug($"Entity {entity} does not have a SleepZoneComponent, cannot wake up.");
        }
    }
}
