using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared.Civ14.CivResearch;

public sealed partial class CivResearchSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    private ISawmill _sawmill = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MapCreatedEvent>(OnMapCreated);
        _sawmill = _logManager.GetSawmill("research");
    }


    private void OnMapCreated(MapCreatedEvent ev)
    {
        var mapUid = _mapManager.GetMapEntityId(ev.MapId);
        EnsureComp<CivResearchComponent>(mapUid);
        _sawmill.Info("research", $"Ensured ResearchComponent on new map {ev.MapId} (Entity: {mapUid})");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Iterate through all active maps
        foreach (var mapId in _mapManager.GetAllMapIds())
        {
            // Get the entity UID associated with the map
            var mapUid = _mapManager.GetMapEntityId(mapId);

            // Try to get the ResearchComponent from the map's entity UID
            if (TryComp<CivResearchComponent>(mapUid, out var comp))
            {
                // Now run your logic
                if (!comp.ResearchEnabled)
                    continue;

                // Use frameTime for frame-rate independent accumulation
                // comp.ResearchLevel += comp.ResearchSpeed * frameTime * Timing.TickRate; // More robust way
                // Or keep the original logic if ResearchSpeed is per-tick
                if (comp.ResearchLevel >= comp.MaxResearch)
                {
                    continue;
                }
                comp.ResearchLevel += comp.ResearchSpeed;

                // Mark component dirty if necessary (often handled automatically for networked components)
                Dirty(mapUid, comp);
            }
        }
    }
}
