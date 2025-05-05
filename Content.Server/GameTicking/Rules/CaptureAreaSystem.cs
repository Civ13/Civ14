
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Robust.Shared.Timing;
using Content.Server.Chat.Systems;
using Content.Server.RoundEnd;

namespace Content.Server.GameTicking.Rules;

public sealed class CaptureAreaSystem : GameRuleSystem<CaptureAreaRuleComponent>
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CaptureAreaComponent>();
        while (query.MoveNext(out var uid, out var area))
        {
            ProcessArea(uid, area, frameTime);
        }
    }
    private void ProcessArea(EntityUid uid, CaptureAreaComponent area, float frameTime)
    {
        var areaXform = _transform.GetMapCoordinates(uid);
        var factionCounts = new Dictionary<string, int>();

        // Initialize counts for all capturable factions to 0
        foreach (var faction in area.CapturableFactions)
        {
            factionCounts[faction] = 0;
        }

        // Find entities in range and count factions
        var entitiesInRange = _lookup.GetEntitiesInRange(areaXform, area.CaptureRadius, LookupFlags.Dynamic | LookupFlags.Sundries); // Include dynamic entities and items/mobs etc.
        foreach (var entity in entitiesInRange)
        {
            // Check if the entity has a faction and if it's one we care about
            if (_entityManager.TryGetComponent<NpcFactionMemberComponent>(entity, out var factionMember))
            {
                foreach (var faction in factionMember.Factions)
                {
                    if (area.CapturableFactions.Contains(faction))
                        factionCounts[faction]++;
                }
            }
        }

        // Determine the controlling faction
        string currentController = "";
        int controllerCount = 0;
        foreach (var (faction, count) in factionCounts)
        {
            if (count > 0)
            {
                currentController = faction;
                controllerCount++;
            }
        }

        // If more than one faction is present, it's contested
        if (controllerCount > 1)
            currentController = ""; // Reset controller if contested

        // Update component state
        area.Occupied = controllerCount > 0;

        if (currentController != area.Controller)
        {
            // Controller changed (or became contested/empty)
            area.Controller = currentController;
            area.CaptureTimer = 0f; // Reset timer on change
            if (currentController == "")
            {
                _chat.DispatchGlobalAnnouncement($"{area.PreviousController} has lost control of {area.Name}!", "Objective", false, null, Color.Red);
            }
            else
            {
                _chat.DispatchGlobalAnnouncement($"{currentController} has gained control of {area.Name}!", "Objective", false, null, Color.DodgerBlue);
            }
        }
        else if (!string.IsNullOrEmpty(currentController))
        {
            // Controller remains the same, increment timer
            area.CaptureTimer += frameTime;

            //Check for capture completion
            if (area.CaptureTimer >= area.CaptureDuration)
            {
                if (_gameTicker.RunLevel == GameRunLevel.InRound)
                {
                    _chat.DispatchGlobalAnnouncement($"{currentController} has captured {area.Name} and is victorious!", "Round", false, null, Color.Green);
                    _roundEndSystem.EndRound();
                }
            }

        }
        else
        {
            // Area is empty or contested, and wasn't previously controlled by a single faction
            area.CaptureTimer = 0f; // Ensure timer is reset/stays reset
        }
        area.PreviousController = currentController;
    }

}
