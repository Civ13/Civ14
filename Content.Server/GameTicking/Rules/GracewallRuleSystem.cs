
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

namespace Content.Server.GameTicking.Rules;

public sealed class GracewallRuleSystem : GameRuleSystem<GracewallRuleComponent>
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    // Define a specific collision group for the grace wall.
    // Make sure this group is defined in CollisionGroups.yml and NPCs are set to not collide with it.
    [Dependency] private readonly ChatSystem _chat = default!;
    private const int GraceWallCollisionGroup = (int)CollisionGroup.MidImpassable; // Example: Use MidImpassable or define a custom one

    private FixturesComponent? _fixtures;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GracewallAreaComponent, StartCollideEvent>(OnStartCollide);
        // Potentially subscribe to PreventCollideEvent if more fine-grained control is needed
    }

    protected override void Started(EntityUid uid, GracewallRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.Timer = (float)component.GracewallDuration.TotalSeconds;
        component.GracewallActive = true;
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

        var query = EntityQueryEnumerator<GracewallRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var gracewall, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule) || !gracewall.GracewallActive)
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
            area.GracewallActive = false;
            UpdateGracewallPhysics(wallUid, area, fixtures, false);
        }
    }

    private void UpdateGracewallPhysics(EntityUid uid, GracewallAreaComponent component, FixturesComponent fixtures, bool active)
    {
        // Check if the specific fixture we defined in the prototype exists
        if (!fixtures.Fixtures.TryGetValue("gracewall", out var fixture))

        {
            Log.Warning($"Gracewall entity {ToPrettyString(uid)} is missing the 'gracewall' fixture!");
            // Attempt to create it dynamically if missing? Or just rely on the prototype.
            // For now, we'll just log a warning. If it's consistently missing, the prototype needs fixing.
            return;
        }

        // Modify the fixture's collision properties
        // This assumes NPCs have a mask that *doesn't* include GraceWallCollisionGroup
        _physics.SetCollisionLayer(uid, "gracewall", fixture, active ? GraceWallCollisionGroup : (int)CollisionGroup.None);
        // Setting the layer might require refreshing contacts or waking the body if it's asleep
        // to ensure the change takes effect immediately.
        if (TryComp<PhysicsComponent>(uid, out var physics))
            _physics.WakeBody(uid, body: physics);
        // Ensure the radius is correct (it might change dynamically later)
        // This requires getting the specific fixture and modifying its shape.
        // For simplicity, we assume the prototype radius is sufficient for now.
        // If dynamic radius is needed, we'd need more complex logic here.
    }

    private void OnStartCollide(EntityUid uid, GracewallAreaComponent component, ref StartCollideEvent args)
    {
        // This event triggers when something *starts* colliding with the grace wall fixture.
        // We only care when the wall is active.
        if (!component.GracewallActive)
            return;

        // Check if the other entity is an NPC
        var otherUid = args.OtherEntity;
        if (HasComp<NpcFactionMemberComponent>(otherUid))
        {
            // The collision system *should* prevent entry based on layers/masks.
            // However, if an NPC somehow starts colliding (e.g., spawned inside, teleported),
            // we could potentially push them out here.
            // For now, we rely on the collision group preventing entry.
            // Log.Debug($"NPC {ToPrettyString(otherUid)} collided with active grace wall {ToPrettyString(uid)}.");
        }
    }

}
