using Content.Server.GameTicking.Rules.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Robust.Shared.Timing;
using Content.Server.Chat.Systems;
using Content.Server.RoundEnd;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
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
    /// <summary>
    /// Processes a capture area, determining faction control based on the presence of alive faction members, updating control status, managing capture timers, and dispatching global announcements for control changes, timed warnings, and victory.
    /// </summary>
    /// <param name="uid">The entity identifier of the capture area.</param>
    /// <param name="area">The capture area component to process.</param>
    /// <param name="frameTime">The elapsed time since the last update, in seconds.</param>
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
            if (EntityManager.TryGetComponent<MobStateComponent>(entity, out var mobState))
            {
                //do not count dead and crit mobs
                if (mobState.CurrentState == MobState.Alive)
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
        }

        // Determine the controlling faction
        var currentController = "";
        var maxCount = 0;
        foreach (var (faction, count) in factionCounts)
        {
            if (count > maxCount)
            {
                maxCount = count;
                currentController = faction;
            }
            else if (maxCount != 0 && count == maxCount)
            {
                currentController = ""; // Contested
            }
        }

        // Update component state
        if (maxCount > 0 && currentController != "")
        {
            area.Occupied = true;
        }

        if (currentController != area.Controller)
        {
            // Controller changed (or became contested/empty)
            area.Controller = currentController;
            area.CaptureTimer = 0f; // Reset timer on change
            area.CaptureTimerAnnouncement1 = false;
            area.CaptureTimerAnnouncement2 = false;
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

            //announce when theres 2 and 1 minutes left.
            var timeleft = area.CaptureDuration - area.CaptureTimer;
            if (timeleft <= 120 && area.CaptureTimerAnnouncement2 == false)
            {
                _chat.DispatchGlobalAnnouncement($"Two minutes until {currentController} captures {area.Name}!", "Round", false, null, Color.Blue);
                area.CaptureTimerAnnouncement2 = true;
            }
            else if (timeleft < 60 && area.CaptureTimerAnnouncement1 == false)
            {
                _chat.DispatchGlobalAnnouncement($"One minute until {currentController} captures {area.Name}!", "Round", false, null, Color.Blue);
                area.CaptureTimerAnnouncement1 = true;
            }
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
            area.CaptureTimerAnnouncement1 = false;
            area.CaptureTimerAnnouncement2 = false;
        }
        area.PreviousController = currentController;
    }

}
