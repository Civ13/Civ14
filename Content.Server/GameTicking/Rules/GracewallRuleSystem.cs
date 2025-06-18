using Content.Server.GameTicking.Rules.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Content.Server.Chat.Systems;
using Robust.Shared.Physics;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Physics.Components;
using System.Collections.Generic;

namespace Content.Server.GameTicking.Rules;

public sealed class GracewallRuleSystem : GameRuleSystem<GracewallRuleComponent>
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private const int GraceWallCollisionGroup = (int)CollisionGroup.MidImpassable;

    // Cache for entity passability checks
    private Dictionary<EntityUid, bool> _passabilityCache = new();
    private TimeSpan _lastCacheClear;
    private const float CacheClearInterval = 5.0f; // Clear cache every 5 seconds

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GracewallAreaComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<GracewallAreaComponent, PreventCollideEvent>(OnPreventCollide);
    }

    protected override void Started(EntityUid uid, GracewallRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Timer = (float)component.GracewallDuration.TotalSeconds;
        component.GracewallActive = true;
        _lastCacheClear = _gameTiming.CurTime;

        // Schedule the announcement for 15 seconds later
        var announcementMessage = $"The grace wall is up for {component.GracewallDuration.TotalMinutes} minutes!";
        Timer.Spawn(TimeSpan.FromSeconds(15), () =>
        {
            _chat.DispatchGlobalAnnouncement(announcementMessage, "Round", false, null, Color.Yellow);
        });
        Log.Info($"Grace wall active for {component.GracewallDuration.TotalMinutes} minutes.");

        // Activate all grace wall areas
        var query = EntityQueryEnumerator<GracewallAreaComponent, TransformComponent, FixturesComponent>();
        while (query.MoveNext(out var wallUid, out var area, out var xform, out var fixtures))
        {
            area.GracewallActive = true;
            UpdateGracewallPhysics(wallUid, area, fixtures, true);
        }
    }

    protected override void Ended(EntityUid uid, GracewallRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        // Ensure walls are deactivated if the rule ends unexpectedly
        DeactivateAllGraceWalls(component);
        _chat.DispatchGlobalAnnouncement("The grace wall is now down!", "Round", false, null, Color.Yellow);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Clear cache periodically to avoid memory leaks
        var currentTime = _gameTiming.CurTime;
        if (currentTime - _lastCacheClear > TimeSpan.FromSeconds(CacheClearInterval))
        {
            _passabilityCache.Clear();
            _lastCacheClear = currentTime;
        }

        var query = EntityQueryEnumerator<GracewallRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var ruleUid, out var gracewall, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(ruleUid, gameRule) || !gracewall.GracewallActive)
                continue;

            gracewall.Timer -= frameTime;

            if (gracewall.Timer <= 0)
            {
                Log.Info("Grace wall duration ended.");
                DeactivateAllGraceWalls(gracewall);
                _chat.DispatchGlobalAnnouncement("The grace wall is now down!", "Round", false, null, Color.Yellow);
            }
        }
    }

    private void DeactivateAllGraceWalls(GracewallRuleComponent component)
    {
        component.GracewallActive = false;

        // Deactivate all grace wall areas
        var query = EntityQueryEnumerator<GracewallAreaComponent, FixturesComponent>();
        while (query.MoveNext(out var wallUid, out var area, out var fixtures))
        {
            if (area.Permanent == false)
            {
                area.GracewallActive = false;
                UpdateGracewallPhysics(wallUid, area, fixtures, false);
            }
        }
    }

    private void UpdateGracewallPhysics(EntityUid uid, GracewallAreaComponent component, FixturesComponent fixtures, bool active)
    {
        // Check if the specific fixture we defined in the prototype exists
        if (!fixtures.Fixtures.TryGetValue("gracewall", out var fixture))
        {
            Log.Warning($"Gracewall entity {ToPrettyString(uid)} is missing the 'gracewall' fixture!");
            return;
        }

        // Modify the fixture's collision properties
        _physics.SetCollisionLayer(uid, "gracewall", fixture, active ? GraceWallCollisionGroup : (int)CollisionGroup.None);

        // Ensure the change takes effect immediately
        if (TryComp<PhysicsComponent>(uid, out var physics))
            _physics.WakeBody(uid, body: physics);
    }

    private void OnStartCollide(EntityUid uid, GracewallAreaComponent component, ref StartCollideEvent args)
    {
        // This event is kept minimal to reduce lag
        if (!component.GracewallActive)
            return;
    }

    private bool CheckPassable(EntityUid entityTryingToPass, GracewallAreaComponent component)
    {
        // Check cache first
        if (_passabilityCache.TryGetValue(entityTryingToPass, out var result))
            return result;

        // Original logic
        if (TryComp<NpcFactionMemberComponent>(entityTryingToPass, out var factions))
        {
            foreach (var faction in component.BlockingFactions)
            {
                if (faction == "All")
                {
                    _passabilityCache[entityTryingToPass] = true;
                    return true;
                }
                foreach (var member in factions.Factions)
                {
                    if (member.ToString() == faction)
                    {
                        _passabilityCache[entityTryingToPass] = true;
                        return true;
                    }
                }
            }
        }

        _passabilityCache[entityTryingToPass] = false;
        return false;
    }

    private void OnPreventCollide(EntityUid uid, GracewallAreaComponent component, ref PreventCollideEvent args)
    {
        // Only handle collisions when the wall is active
        if (!component.GracewallActive)
            return;

        // Get the entity trying to pass through
        var otherEntity = args.OtherEntity;

        // Skip processing for entities we've already determined can pass
        if (_passabilityCache.TryGetValue(otherEntity, out var canPass) && canPass)
            return;

        // Check if the entity trying to pass through should be blocked
        if (!CheckPassable(otherEntity, component))
        {
            args.Cancelled = true;
        }
    }
}
