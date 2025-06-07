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

        // Attempt to get the rule component.
        // The standard way in GameRuleSystem<T> is: var ruleComp = RuleConfiguration;
        // If 'RuleConfiguration' is not recognized by the compiler in your environment,
        // you can query for the component directly as a workaround.
        CaptureAreaRuleComponent? ruleComp = null;
        var ruleQuery = EntityQueryEnumerator<CaptureAreaRuleComponent>();
        if (ruleQuery.MoveNext(out _, out var activeRuleComp)) // Assumes one active rule component
        {
            ruleComp = activeRuleComp;
        }

        if (ruleComp == null) // No active CaptureAreaRuleComponent found
        {
            return;
        }

        // Handle Asymmetric mode timer and defender victory
        if (ruleComp.Mode == "Asymmetric")
        {
            // If the round has already ended (e.g., by a capture in ProcessArea earlier this frame), do nothing.
            if (_gameTicker.RunLevel != GameRunLevel.InRound)
                return;

            ruleComp.AsymmetricGameTimeElapsed += frameTime;
            if (ruleComp.AsymmetricGameTimeElapsed >= ruleComp.Timer * 60f) // ruleComp.Timer is in minutes
            {
                var defenderDisplayName = Faction2String(ruleComp.DefenderFactionName);
                if (string.IsNullOrEmpty(ruleComp.DefenderFactionName) || string.IsNullOrEmpty(defenderDisplayName))
                {
                    Logger.ErrorS("capturearea", $"Asymmetric mode: DefenderFactionName is not set or Faction2String returned empty for '{ruleComp.DefenderFactionName}'. Defaulting defender display name.");
                    defenderDisplayName = "The Defenders"; // Fallback display name
                }

                _chat.DispatchGlobalAnnouncement(
                    $"{defenderDisplayName} ha(s) successfully defended for {ruleComp.Timer:F0} minutes and win(s) the round!",
                    "Round", false, null, Color.Green);
                _roundEndSystem.EndRound();
                return; // Round ended, no need to process areas further for capture victories
            }
        }

        // Process individual capture areas
        var query = EntityQueryEnumerator<CaptureAreaComponent>();
        while (query.MoveNext(out var uid, out var area))
        {
            // If the round ended due to asymmetric timer, stop processing areas.
            if (_gameTicker.RunLevel != GameRunLevel.InRound)
                break;
            ProcessArea(uid, area, frameTime, ruleComp);
        }
    }
    /// <summary>
    /// Processes a capture area, determining faction control based on the presence of alive faction members, updating control status, managing capture timers, and dispatching global announcements for control changes, timed warnings, and victory.
    /// </summary>
    private void ProcessArea(EntityUid uid, CaptureAreaComponent area, float frameTime, CaptureAreaRuleComponent ruleComp)
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
                currentController = Faction2String(faction);
            }
            else if (maxCount != 0 && count == maxCount)
            {
                currentController = ""; // Contested
            }
        }

        // Update component state
        area.Occupied = maxCount > 0 && !string.IsNullOrEmpty(currentController);

        if (currentController != area.Controller)
        {
            // Controller changed (or became contested/empty)
            if (currentController == "")
            {
                // Area became contested or empty
                if (area.ContestedTimer == 0f)
                {
                    // Store the last controller when we first enter contested state
                    area.LastController = area.Controller;
                }

                // Increment contested timer
                area.ContestedTimer += frameTime;

                // Only reset the capture timer if contested for long enough
                if (area.ContestedTimer >= area.ContestedResetTime)
                {
                    // Reset capture progress after contested threshold is reached
                    area.CaptureTimer = 0f;
                    area.CaptureTimerAnnouncement1 = false;
                    area.CaptureTimerAnnouncement2 = false;

                    // Only announce loss of control once the timer has fully reset
                    if (!string.IsNullOrEmpty(area.LastController))
                    {
                        _chat.DispatchGlobalAnnouncement($"{area.LastController} has lost control of {area.Name}!", "Objective", false, null, Color.Red);
                        area.LastController = ""; // Clear last controller after announcement
                    }
                }
            }
            else if (area.Controller == "")
            {
                // Area was contested/empty but now has a controller
                if (currentController == area.LastController && area.ContestedTimer < area.ContestedResetTime)
                {
                    // The previous controller regained control before the reset threshold
                    // Don't reset the timer or make announcements
                    area.Controller = currentController;
                    area.ContestedTimer = 0f;
                }
                else
                {
                    // New controller or contested long enough to reset
                    area.Controller = currentController;
                    area.ContestedTimer = 0f;
                    _chat.DispatchGlobalAnnouncement($"{currentController} has gained control of {area.Name}!", "Objective", false, null, Color.DodgerBlue);
                }
            }
            else
            {
                // Direct change from one faction to another
                var oldController = area.Controller; // This is the display name of the old controller

                // Announce loss for the old controller
                // oldController is guaranteed to be non-empty here because:
                // 1. currentController != area.Controller (outer condition)
                // 2. currentController != "" (otherwise this branch wouldn't be hit, it'd be currentController == "")
                // 3. area.Controller != "" (otherwise this branch wouldn't be hit, it'd be area.Controller == "")
                _chat.DispatchGlobalAnnouncement($"{oldController} has lost control of {area.Name}!", "Objective", false, null, Color.Red);

                // Announce gain for the new controller
                _chat.DispatchGlobalAnnouncement($"{currentController} has gained control of {area.Name}!", "Objective", false, null, Color.DodgerBlue);

                // Update to the new controller
                area.Controller = currentController;

                // Reset capture progress for the new controller
                area.CaptureTimer = 0f;
                area.CaptureTimerAnnouncement1 = false;
                area.CaptureTimerAnnouncement2 = false;

                // Reset contested state as it's now firmly controlled by a new faction
                area.ContestedTimer = 0f;
                area.LastController = ""; // Previous "last controller" during a contested phase is no longer relevant
            }
        }
        else if (!string.IsNullOrEmpty(currentController))
        {
            // Controller remains the same, reset contested timer and increment capture timer
            area.ContestedTimer = 0f;
            area.CaptureTimer += frameTime;

            //announce when theres 2 and 1 minutes left.
            if (ruleComp.Mode != "Points")
            {
                var timeleft = area.CaptureDuration - area.CaptureTimer;
                if (currentController != Faction2String(ruleComp.DefenderFactionName))
                {
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
                }
                //Check for capture completion
                if (area.CaptureTimer >= area.CaptureDuration)
                {
                    if (_gameTicker.RunLevel == GameRunLevel.InRound)
                    {
                        bool canWinByCapture = true;
                        // area.Controller is the display name of the faction that has held the point
                        string winningControllerDisplay = area.Controller;

                        if (ruleComp.Mode == "Asymmetric")
                        {
                            // In Asymmetric mode, only non-defenders (attackers) can win by capturing a point.
                            // The defender wins by timeout.
                            if (winningControllerDisplay == Faction2String(ruleComp.DefenderFactionName))
                            {
                                canWinByCapture = false;
                            }
                        }

                        if (canWinByCapture && !string.IsNullOrEmpty(winningControllerDisplay))
                        {
                            _chat.DispatchGlobalAnnouncement($"{winningControllerDisplay} has captured {area.Name} and is victorious!", "Round", false, null, Color.Green);
                            _roundEndSystem.EndRound();
                            return; // Round ended, no further processing for this area needed.
                        }
                    }
                }
            }
        }
        else
        {
            // Area is empty or contested, and wasn't previously controlled by a single faction
            // Increment contested timer
            area.ContestedTimer += frameTime;

            if (area.ContestedTimer >= area.ContestedResetTime)
            {
                // Reset capture progress after contested threshold is reached
                area.CaptureTimer = 0f;
                area.CaptureTimerAnnouncement1 = false;
                area.CaptureTimerAnnouncement2 = false;
            }
        }
        area.PreviousController = currentController;
    }
    private static string Faction2String(string faction)
    {
        switch (faction)
        {
            case "SovietCW":
                return "Soviet Union";
            case "Soviet":
                return "Soviet Union";
            default:
                return faction;
        }

    }
}
