using Content.Shared.Coordinates;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.Log; // Added for ILogManager and ISawmill
using Robust.Shared.IoC; // Added for Dependency attribute
using Robust.Shared.GameStates; // Needed for [RegisterComponent] if SleepZoneComponent wasn't partial
using Robust.Shared.Serialization.Manager.Attributes; // Needed for [DataField]
using System.Numerics;

namespace Content.Shared.Civ14.SleepZone;
public sealed partial class SleepZoneSystem : EntitySystem
{
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
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

        // More efficient: directly query for the prototype we want
        var query = _entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out _))
        {
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

        // Store the original absolute world position
        sleepZone.Origin = Transform(entity).Coordinates;
        _sawmill.Info($"Saved origin {sleepZone.Origin} for entity {entity}.");


        if (TryTeleportToBed(entity))
        {
            sleepZone.IsSleeping = true;
            _sawmill.Info($"Entity {entity} started sleeping successfully.");
        }
        else
        {
            _sawmill.Warning($"Entity {entity} failed to start sleeping because teleportation to bed failed.");
            // Reset origin if teleport fails, as the entity hasn't moved.
            sleepZone.Origin = EntityCoordinates.Invalid;
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

            var targetCoords = Transform(bedEntity).Coordinates;

            _sawmill.Info($"Found bed {bedEntity}, teleporting {entityToTeleport} into it at {targetCoords}.");

            // Use _xform for SetCoordinates
            _xform.SetCoordinates(entityToTeleport, targetCoords);
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

            // Check if the origin is valid before teleporting
            if (!sleepZone.Origin.HasValue) // Use .HasValue for nullable types

            {
                // Use ToPrettyString for better entity logging if available, otherwise fallback
                var entityString = _entities.ToPrettyString(entity);
                _sawmill.Warning($"Entity {entityString} has no Origin coordinates stored, cannot teleport back.");
                // Decide what to do here - maybe leave them in bed? Or teleport to a default spot?
                // For now, just mark as not sleeping.
                sleepZone.IsSleeping = false;
                return;
            }

            _sawmill.Info($"Waking up entity {_entities.ToPrettyString(entity)}, returning to {sleepZone.Origin.Value}."); // Log the .Value

            _xform.SetCoordinates(entity, sleepZone.Origin.Value);


            sleepZone.IsSleeping = false;
            // Clear the origin after use
            sleepZone.Origin = null;
        }
        else
        {
            _sawmill.Debug($"Entity {entity} does not have a SleepZoneComponent, cannot wake up.");
        }
    }
}
