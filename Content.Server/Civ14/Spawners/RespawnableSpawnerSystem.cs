using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared;

namespace Content.Server.Spawners;

public sealed class RespawnableSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RespawnableSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SpawnedByComponent, EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnMapInit(EntityUid uid, RespawnableSpawnerComponent component, MapInitEvent args)
    {
        if (component.Prototypes.Count > 0)
        {
            var prototype = component.Prototypes[_random.Next(component.Prototypes.Count)];
            var newEntity = _entityManager.SpawnEntity(prototype, Transform(uid).Coordinates);
            var spawnedBy = _entityManager.AddComponent<SpawnedByComponent>(newEntity);
            spawnedBy.Spawner = uid;
        }
    }

    private void OnEntityTerminating(EntityUid uid, SpawnedByComponent spawnedBy, EntityTerminatingEvent args)
    {
        if (_entityManager.TryGetComponent<RespawnableSpawnerComponent>(spawnedBy.Spawner, out var spawner))
        {
            if (spawner != null)
            {
                var delay = _random.NextFloat(spawner.MinDelay, spawner.MaxDelay);
                var respawnTime = (float)_gameTiming.CurTime.TotalSeconds + delay;
                spawner.RespawnTimers[uid] = respawnTime;
            }
        }
        else
        {
            Log.Warning($"Spawner {spawnedBy.Spawner} not found or lacks RespawnableSpawnerComponent for entity {uid}");
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<RespawnableSpawnerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            var toRemove = new List<EntityUid>();
            foreach (var (entity, respawnTime) in component.RespawnTimers)
            {
                var currentTime = (float)_gameTiming.CurTime.TotalSeconds;
                if (currentTime >= respawnTime)
                {
                    try
                    {
                        var prototype = component.Prototypes[_random.Next(component.Prototypes.Count)];
                        var newEntity = _entityManager.SpawnEntity(prototype, Transform(uid).Coordinates);
                        var spawnedBy = _entityManager.AddComponent<SpawnedByComponent>(newEntity);
                        spawnedBy.Spawner = uid;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to spawn entity for spawner {uid}: {ex.Message}");
                    }
                    finally
                    {
                        toRemove.Add(entity); // Marks it to remove from RespawnTimers, even if it fails
                    }
                }
            }
            foreach (var remove in toRemove)
            {
                component.RespawnTimers.Remove(remove);
            }
        }
    }
}