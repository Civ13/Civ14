using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes; // Added for PrototypeId - Although not strictly needed here anymore
using Robust.Shared.IoC; // For Dependency attribute
using Robust.Shared.Log; // For ILogManager and ISawmill

namespace Content.Shared.Civ14.SleepZone;

public sealed partial class SleepZoneSystem : EntitySystem
{
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IEntityManager _entities = default!; // Use IEntityManager
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
    public bool TryFindSleepZoneBed(out EntityUid bedId) // Renamed for clarity
    {
        const string bedPrototypeId = "SleepZoneBed";
        // Use EntityManager's EntityQuery to find entities with the specific prototype.
        // This is generally more efficient than GetEntitiesByPrototype for simple cases.
        var query = EntityQueryEnumerator<MetaDataComponent>(); // Query any component, MetaData is universal
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == bedPrototypeId)
            {
                bedId = uid;
                return true; // Found the first one
            }
        }

        // If the loop finishes without finding a bed
        bedId = EntityUid.Invalid;
        return false;
    }

    /// <summary>
    /// Initiates the sleep state for an entity if it has a SleepZoneComponent,
    /// saving its original position and attempting to teleport it to a bed.
    /// </summary>
    /// <param name="entity">The entity attempting to sleep.</param>
    public void StartSleep(EntityUid entity)
    {
        // Use EnsureComponent to add the component if it doesn't exist,
        // or TryGetComponent if you only want to act if it already exists.
        // Let's assume it should exist or be added.
        // If it *must* exist beforehand, use TryGetComponent.
        if (!_entities.TryGetComponent<SleepZoneComponent>(entity, out var sleepZoneComponent))
        {
            _sawmill.Debug($"Entity {entity} does not have a SleepZoneComponent, cannot start sleep.");
            return; // Exit if component is missing
        }

        // Prevent starting sleep if already sleeping
        if (sleepZoneComponent.IsSleeping)
        {
            _sawmill.Debug($"Entity {entity} is already sleeping.");
            return;
        }

        // Save the current coordinates BEFORE teleporting
        sleepZoneComponent.Origin = _xform.GetCoordinates(entity);
        _sawmill.Info($"Saved origin {sleepZoneComponent.Origin} for entity {entity}.");

        // Attempt to teleport the entity to a bed
        if (TryTeleportToBed(entity))
        {
            // Only set IsSleeping to true if teleport succeeds
            sleepZoneComponent.IsSleeping = true;
            _sawmill.Info($"Entity {entity} started sleeping successfully.");
            // Make sure the component state is saved if networking is involved
            Dirty(entity, sleepZoneComponent);
        }
        else
        {
            // Teleport failed, don't set IsSleeping to true
            _sawmill.Warning($"Entity {entity} failed to start sleeping because no bed was found.");
            // Optional: Reset Origin if you want to be strict
            // sleepZoneComponent.Origin = EntityCoordinates.Invalid;
        }
    }

    /// <summary>
    /// Attempts to teleport an entity to the first available SleepZoneBed.
    /// </summary>
    /// <param name="entityToTeleport">The entity to teleport.</param>
    /// <returns>True if teleportation was successful, false otherwise.</returns>
    private bool TryTeleportToBed(EntityUid entityToTeleport) // Made private as it's internal logic for StartSleep
    {
        if (TryFindSleepZoneBed(out var bedEntity))
        {
            // Found a bed, now teleport the entity there
            // Check if the bed still exists before getting coordinates
            if (!_entities.EntityExists(bedEntity))
            {
                _sawmill.Warning($"Found bed {bedEntity} but it no longer exists.");
                return false;
            }

            var bedCoords = _xform.GetCoordinates(bedEntity);
            _sawmill.Info($"Found bed {bedEntity} at {bedCoords}, teleporting {entityToTeleport}.");
            _xform.SetCoordinates(entityToTeleport, bedCoords);
            return true; // Teleport successful
        }
        else
        {
            // No bed found
            _sawmill.Warning($"Could not find any entity with prototype 'SleepZoneBed' to teleport {entityToTeleport} to.");
            return false; // Teleport failed
        }
    }

    /// <summary>
    /// Wakes up an entity if it's sleeping, returning it to its original location.
    /// </summary>
    /// <param name="entity">The entity to wake up.</param>
    public void WakeUp(EntityUid entity)
    {
        if (_entities.TryGetComponent<SleepZoneComponent>(entity, out var sleepZoneComponent))
        {
            if (!sleepZoneComponent.IsSleeping)
            {
                _sawmill.Debug($"Entity {entity} is not sleeping, cannot wake up.");
                return;
            }

            // Check if the origin coordinates are valid
            if (sleepZoneComponent.Origin.IsValid(_entities)) // Use IsValid extension method
            {
                _sawmill.Info($"Waking up entity {entity}, returning to {sleepZoneComponent.Origin}.");
                _xform.SetCoordinates(entity, sleepZoneComponent.Origin);
            }
            else
            {
                // Origin is invalid (e.g., map deleted, parent entity deleted).
                // What should happen? Log a warning? Teleport to a default spawn?
                // For now, just log and leave the entity where it is.
                _sawmill.Warning($"Entity {entity} woke up, but its origin {sleepZoneComponent.Origin} is invalid. Leaving entity in place.");
                // Optionally, you could try teleporting to a default location here.
            }

            // Reset sleep state and origin
            sleepZoneComponent.IsSleeping = false;
            sleepZoneComponent.Origin = EntityCoordinates.Invalid; // Reset origin after use
            Dirty(entity, sleepZoneComponent); // Mark component as dirty for networking
        }
        else
        {
            _sawmill.Debug($"Entity {entity} does not have a SleepZoneComponent, cannot wake up.");
        }
    }


    // This Teleport method seems redundant if the main goal is sleep zones.
    // Consider removing it unless it serves a separate purpose.
    // If kept, ensure its purpose is clear.
    // public void Teleport(EntityUid teleporter, EntityUid target)
    // {
    //     var targetCoords = _xform.GetCoordinates(target);
    //     _xform.SetCoordinates(teleporter, targetCoords);
    // }
}
