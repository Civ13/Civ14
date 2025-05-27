using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Gravity;
using Content.Shared.Physics;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Content.Shared.Barricade;
using Robust.Shared.Random;

namespace Content.Shared.Throwing
{
    /// <summary>
    ///     Handles throwing landing and collisions.
    /// </summary>
    public sealed class ThrownItemSystem : EntitySystem
    {
        [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly SharedBroadphaseSystem _broadphase = default!;
        [Dependency] private readonly FixtureSystem _fixtures = default!;
        [Dependency] private readonly SharedPhysicsSystem _physics = default!;
        [Dependency] private readonly SharedGravitySystem _gravity = default!;
        [Dependency] private readonly SharedTransformSystem _transform = default!; private const string ThrowingFixture = "throw-fixture";
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly ILogManager _log = default!;
        private ISawmill _sawmill = default!;
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ThrownItemComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<ThrownItemComponent, PhysicsSleepEvent>(OnSleep);
            SubscribeLocalEvent<ThrownItemComponent, StartCollideEvent>(HandleCollision);
            SubscribeLocalEvent<ThrownItemComponent, PreventCollideEvent>(PreventCollision);
            SubscribeLocalEvent<ThrownItemComponent, ThrownEvent>(ThrowItem);

            SubscribeLocalEvent<PullStartedMessage>(HandlePullStarted);
            _sawmill = _log.GetSawmill("throwing");
        }

        private void OnMapInit(EntityUid uid, ThrownItemComponent component, MapInitEvent args)
        {
            component.ThrownTime ??= _gameTiming.CurTime;
        }

        private void ThrowItem(EntityUid uid, ThrownItemComponent component, ref ThrownEvent @event)
        {
            if (!EntityManager.TryGetComponent(uid, out FixturesComponent? fixturesComponent) ||
                fixturesComponent.Fixtures.Count != 1 ||
                !TryComp<PhysicsComponent>(uid, out var body))
            {
                return;
            }

            var fixture = fixturesComponent.Fixtures.Values.First();
            var shape = fixture.Shape;
            _fixtures.TryCreateFixture(uid, shape, ThrowingFixture, hard: false, collisionMask: (int)CollisionGroup.ThrownItem, manager: fixturesComponent, body: body);
        }

        private void HandleCollision(EntityUid uid, ThrownItemComponent component, ref StartCollideEvent args)
        {
            if (!args.OtherFixture.Hard)
                return;

            if (args.OtherEntity == component.Thrower)
                return;

            ThrowCollideInteraction(component, args.OurEntity, args.OtherEntity);
        }

        private void PreventCollision(EntityUid uid, ThrownItemComponent component, ref PreventCollideEvent args)
        {
            if (args.OtherEntity == component.Thrower)
            {
                args.Cancelled = true;
            }
            //check for barricade component (percentage of chance to hit/pass over)
            if (TryComp(args.OtherEntity, out BarricadeComponent? barricade))
            {
                var alwaysPassThrough = false;
                //_sawmill.Info("Checking barricade...");
                if (component.Thrower is { } shooterUid && Exists(shooterUid))
                {
                    // Condition 1: Directions are the same (using cardinal directions).
                    // Or, if bidirectional, directions can be opposite.
                    var shooterWorldRotation = _transform.GetWorldRotation(shooterUid);
                    var barricadeWorldRotation = _transform.GetWorldRotation(args.OtherEntity);

                    var shooterDir = shooterWorldRotation.GetCardinalDir();
                    var barricadeDir = barricadeWorldRotation.GetCardinalDir();

                    bool directionallyAllowed = false;
                    if (shooterDir == barricadeDir)
                    {
                        directionallyAllowed = true;
                        //_sawmill.Debug("Shooter and barricade facing same cardinal direction.");
                    }
                    else if (barricade.Bidirectional)
                    {
                        var oppositeBarricadeDir = (Direction)(((int)barricadeDir + 4) % 8);
                        if (shooterDir == oppositeBarricadeDir)
                        {
                            directionallyAllowed = true;
                            //_sawmill.Debug("Shooter and barricade facing opposite cardinal directions (bidirectional pass).");
                        }
                    }

                    if (directionallyAllowed)
                    {
                        // Condition 2: Firer is within 1 tile of the barricade.
                        var shooterCoords = Transform(shooterUid).Coordinates;
                        var barricadeCoords = Transform(args.OtherEntity).Coordinates;

                        if (shooterCoords.TryDistance(EntityManager, barricadeCoords, out var distance) &&
                            distance <= 1.5f)
                        {
                            alwaysPassThrough = true;
                        }
                    }
                }

                if (alwaysPassThrough)
                {
                    args.Cancelled = true;
                }
                else
                {
                    //_sawmill.Debug("Barricade direction/distance check failed or shooter not valid.");
                    // Standard barricade blocking logic if the special conditions are not met.
                    var rando = _random.NextFloat(0.0f, 100.0f);
                    if (rando >= 12)
                    {
                        args.Cancelled = true;
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }

        private void OnSleep(EntityUid uid, ThrownItemComponent thrownItem, ref PhysicsSleepEvent @event)
        {
            StopThrow(uid, thrownItem);
        }

        private void HandlePullStarted(PullStartedMessage message)
        {
            // TODO: this isn't directed so things have to be done the bad way
            if (EntityManager.TryGetComponent(message.PulledUid, out ThrownItemComponent? thrownItemComponent))
                StopThrow(message.PulledUid, thrownItemComponent);
        }

        public void StopThrow(EntityUid uid, ThrownItemComponent thrownItemComponent)
        {
            if (TryComp<PhysicsComponent>(uid, out var physics))
            {
                _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);

                if (physics.Awake)
                    _broadphase.RegenerateContacts((uid, physics));
            }

            if (EntityManager.TryGetComponent(uid, out FixturesComponent? manager))
            {
                var fixture = _fixtures.GetFixtureOrNull(uid, ThrowingFixture, manager: manager);

                if (fixture != null)
                {
                    _fixtures.DestroyFixture(uid, ThrowingFixture, fixture, manager: manager);
                }
            }

            EntityManager.EventBus.RaiseLocalEvent(uid, new StopThrowEvent { User = thrownItemComponent.Thrower }, true);
            EntityManager.RemoveComponent<ThrownItemComponent>(uid);
        }

        public void LandComponent(EntityUid uid, ThrownItemComponent thrownItem, PhysicsComponent physics, bool playSound)
        {
            if (thrownItem.Landed || thrownItem.Deleted || _gravity.IsWeightless(uid) || Deleted(uid))
                return;

            thrownItem.Landed = true;

            // Assume it's uninteresting if it has no thrower. For now anyway.
            if (thrownItem.Thrower is not null)
                _adminLogger.Add(LogType.Landed, LogImpact.Low, $"{ToPrettyString(uid):entity} thrown by {ToPrettyString(thrownItem.Thrower.Value):thrower} landed.");

            _broadphase.RegenerateContacts((uid, physics));
            var landEvent = new LandEvent(thrownItem.Thrower, playSound);
            RaiseLocalEvent(uid, ref landEvent);
        }

        /// <summary>
        ///     Raises collision events on the thrown and target entities.
        /// </summary>
        public void ThrowCollideInteraction(ThrownItemComponent component, EntityUid thrown, EntityUid target)
        {
            if (component.Thrower is not null)
                _adminLogger.Add(LogType.ThrowHit, LogImpact.Low,
                    $"{ToPrettyString(thrown):thrown} thrown by {ToPrettyString(component.Thrower.Value):thrower} hit {ToPrettyString(target):target}.");

            RaiseLocalEvent(target, new ThrowHitByEvent(thrown, target, component), true);
            RaiseLocalEvent(thrown, new ThrowDoHitEvent(thrown, target, component), true);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // TODO predicted throwing - remove this check
            // We don't want to predict landing or stopping, since throwing isn't actually predicted.
            // If we do, the landing/stop will occur prematurely on the client.
            if (_gameTiming.InPrediction)
                return;

            var query = EntityQueryEnumerator<ThrownItemComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var thrown, out var physics))
            {
                if (thrown.LandTime <= _gameTiming.CurTime)
                {
                    LandComponent(uid, thrown, physics, thrown.PlayLandSound);
                }

                var stopThrowTime = thrown.LandTime ?? thrown.ThrownTime;
                if (stopThrowTime <= _gameTiming.CurTime)
                {
                    StopThrow(uid, thrown);
                }
            }
        }
    }
}
