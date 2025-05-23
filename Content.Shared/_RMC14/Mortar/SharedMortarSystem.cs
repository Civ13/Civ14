using Content.Shared._RMC14.Extensions;
using Content.Shared.Administration.Logs;
using Content.Shared.Construction.Components;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Explosion.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Mortar;

public abstract class SharedMortarSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogs = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly FixtureSystem _fixture = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedExplosionSystem _explosion = default!;

    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<MortarComponent, UseInHandEvent>(OnMortarUseInHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<MortarComponent, DeployMortarDoAfterEvent>(OnMortarDeployDoAfter);
        SubscribeLocalEvent<MortarComponent, TargetMortarDoAfterEvent>(OnMortarTargetDoAfter);
        SubscribeLocalEvent<MortarComponent, DialMortarDoAfterEvent>(OnMortarDialDoAfter);
        SubscribeLocalEvent<MortarComponent, InteractUsingEvent>(OnMortarInteractUsing);
        SubscribeLocalEvent<MortarComponent, LoadMortarShellDoAfterEvent>(OnMortarLoadDoAfter);
        SubscribeLocalEvent<MortarComponent, UnanchorAttemptEvent>(OnMortarUnanchorAttempt);
        SubscribeLocalEvent<MortarComponent, AnchorStateChangedEvent>(OnMortarAnchorStateChanged);
        SubscribeLocalEvent<MortarComponent, ExaminedEvent>(OnMortarExamined);
        SubscribeLocalEvent<MortarComponent, ActivatableUIOpenAttemptEvent>(OnMortarActivatableUIOpenAttempt);
        SubscribeLocalEvent<MortarComponent, CombatModeShouldHandInteractEvent>(OnMortarShouldInteract);
        SubscribeLocalEvent<MortarComponent, DestructionEventArgs>(OnMortarDestruction);
        SubscribeLocalEvent<MortarComponent, BeforeDamageChangedEvent>(OnMortarBeforeDamageChanged);

        Subs.BuiEvents<MortarComponent>(MortarUiKey.Key,
            subs =>
            {
                subs.Event<MortarTargetBuiMsg>(OnMortarTargetBui);
                subs.Event<MortarDialBuiMsg>(OnMortarDialBui);
            });
    }

    private void OnMortarBeforeDamageChanged(Entity<MortarComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!ent.Comp.Deployed) // cannot destroy in item form
            args.Cancelled = true;
    }

    private void OnMortarDestruction(Entity<MortarComponent> mortar, ref DestructionEventArgs args)
    {
        if (!mortar.Comp.Deployed || _net.IsClient)
            return;

        SpawnAtPosition(mortar.Comp.Drop, mortar.Owner.ToCoordinates());
    }

    private void OnMortarUseInHand(Entity<MortarComponent> mortar, ref UseInHandEvent args)
    {
        args.Handled = true;
        DeployMortar(mortar, args.User);
    }

    private void OnMortarDeployDoAfter(Entity<MortarComponent> mortar, ref DeployMortarDoAfterEvent args)
    {
        var user = args.User;
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (mortar.Comp.Deployed)
            return;

        if (!CanDeployPopup(mortar, user))
            return;

        mortar.Comp.Deployed = true;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar, fixture, true);

        _appearance.SetData(mortar, MortarVisualLayers.State, MortarVisuals.Deployed);

        var xform = Transform(mortar);
        var coordinates = _transform.GetMoverCoordinates(mortar, xform);
        var rotation = Transform(user).LocalRotation.GetCardinalDir().ToAngle();
        _transform.SetCoordinates(mortar, xform, coordinates, rotation);
        _transform.AnchorEntity((mortar, xform));

        _audio.PlayPredicted(mortar.Comp.DeploySound, mortar, user);
    }

    private void OnMortarTargetDoAfter(Entity<MortarComponent> mortar, ref TargetMortarDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var user = args.User;
        var selfMsg = Loc.GetString("rmc-mortar-target-finish-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-target-finish-others", ("user", user), ("mortar", mortar));
        _popup.PopupPredicted(selfMsg, othersMsg, user, user);
        if (_net.IsClient)
            return;

        var target = args.Vector;
        var position = _transform.GetMapCoordinates(mortar).Position;
        var offset = target;
        //if (_rmcPlanet.TryGetOffset(_transform.GetMapCoordinates(mortar.Owner), out var planetOffset))
        //    offset -= planetOffset;
        // RMC uses a system to offset based on set values per map

        mortar.Comp.Target = target;

        var tilesPer = mortar.Comp.TilesPerOffset;
        var xOffset = (int) Math.Floor(Math.Abs(offset.X - position.X) / tilesPer);
        var yOffset = (int) Math.Floor(Math.Abs(offset.Y - position.Y) / tilesPer);
        mortar.Comp.Offset = (_random.Next(-xOffset, xOffset + 1), _random.Next(-yOffset, yOffset + 1));

        Dirty(mortar);
    }

    private void OnMortarDialDoAfter(Entity<MortarComponent> mortar, ref DialMortarDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        mortar.Comp.Dial = args.Vector;
        Dirty(mortar);

        var user = args.User;
        var selfMsg = Loc.GetString("rmc-mortar-dial-finish-self", ("mortar", mortar));
        var othersMsg = Loc.GetString("rmc-mortar-dial-finish-others", ("user", user), ("mortar", mortar));
        _popup.PopupPredicted(selfMsg, othersMsg, user, user);
    }

    private void OnMortarInteractUsing(Entity<MortarComponent> mortar, ref InteractUsingEvent args)
    {
        var shellId = args.Used;
        if (!TryComp(shellId, out MortarShellComponent? shell))
            return;

        args.Handled = true;
        var user = args.User;

        if (!CanLoadPopup(mortar, (shellId, shell), user, out _, out _))
            return;

        var ev = new LoadMortarShellDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, user, shell.LoadDelay, ev, mortar, mortar, shellId)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
            //ForceVisible = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            var selfMsg = Loc.GetString("rmc-mortar-shell-load-start-self", ("mortar", mortar), ("shell", shellId));
            var othersMsg = Loc.GetString("rmc-mortar-shell-load-start-others",
                ("user", user),
                ("mortar", mortar),
                ("shell", shellId));
            _popup.PopupPredicted(selfMsg, othersMsg, mortar, user);

            _audio.PlayPredicted(mortar.Comp.ReloadSound, mortar, user);
        }
    }

    private void OnMortarLoadDoAfter(Entity<MortarComponent> mortar, ref LoadMortarShellDoAfterEvent args)
    {
        var user = args.User;
        if (args.Cancelled || args.Handled || args.Used is not { } shellId)
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        if (!TryComp(shellId, out MortarShellComponent? shell))
            return;

        if (!mortar.Comp.Deployed)
            return;

        if (HasComp<ActiveMortarShellComponent>(shellId))
            return;

        if (!CanLoadPopup(mortar, (shellId, shell), user, out var travelTime, out var coordinates))
            return;

        var container = _container.EnsureContainer<Container>(mortar, mortar.Comp.ContainerId);
        if (!_container.Insert(shellId, container))
            return;

        var time = _timing.CurTime;
        mortar.Comp.LastFiredAt = time;

        var active = new ActiveMortarShellComponent
        {
            Coordinates = _transform.ToCoordinates(coordinates),
            WarnAt = time + travelTime,
            ImpactWarnAt = time + travelTime + shell.ImpactWarningDelay,
            LandAt = time + travelTime + shell.ImpactDelay,
        };

        AddComp(shellId, active, true);

        var selfMsg = Loc.GetString("rmc-mortar-shell-load-finish-self", ("mortar", mortar), ("shell", shellId));
        var othersMsg = Loc.GetString("rmc-mortar-shell-load-finish-others", ("user", user), ("mortar", mortar), ("shell", shellId));
        _popup.PopupPredicted(selfMsg, othersMsg, user, user);

        othersMsg = Loc.GetString("rmc-mortar-shell-fire", ("mortar", mortar));
        _popup.PopupEntity(othersMsg, mortar, PopupType.MediumCaution);

        var filter = Filter.Pvs(mortar);
        _audio.PlayPvs(mortar.Comp.FireSound, mortar);

        var ev = new MortarFiredEvent(GetNetEntity(mortar));
        if (_net.IsServer)
            RaiseNetworkEvent(ev, filter);
    }

    private void OnMortarUnanchorAttempt(Entity<MortarComponent> mortar, ref UnanchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;
    }

    private void OnMortarAnchorStateChanged(Entity<MortarComponent> mortar, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return;

        mortar.Comp.Deployed = false;
        Dirty(mortar);

        if (_fixture.GetFixtureOrNull(mortar, mortar.Comp.FixtureId) is { } fixture)
            _physics.SetHard(mortar, fixture, false);

        _appearance.SetData(mortar, MortarVisualLayers.State, MortarVisuals.Item);
    }

    private void OnMortarExamined(Entity<MortarComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(MortarComponent)))
        {
            args.PushMarkup(Loc.GetString("rmc-mortar-less-accurate-with-range"));
        }
    }

    private void OnMortarActivatableUIOpenAttempt(Entity<MortarComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!ent.Comp.Deployed)
            args.Cancel();
    }

    private void OnMortarShouldInteract(Entity<MortarComponent> ent, ref CombatModeShouldHandInteractEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMortarTargetBui(Entity<MortarComponent> mortar, ref MortarTargetBuiMsg args)
    {
        args.Target.X.Cap(mortar.Comp.MaxTarget);
        args.Target.Y.Cap(mortar.Comp.MaxTarget);

        var user = args.Actor;
        var ev = new TargetMortarDoAfterEvent(args.Target);
        var doAfter = new DoAfterArgs(EntityManager, user, mortar.Comp.TargetDelay, ev, mortar)
        {
            BreakOnMove = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            var selfMsg = Loc.GetString("rmc-mortar-target-start-self", ("mortar", mortar));
            var othersMsg = Loc.GetString("rmc-mortar-target-start-others", ("user", user), ("mortar", mortar));
            _popup.PopupPredicted(selfMsg, othersMsg, user, user);
        }
    }

    private void OnMortarDialBui(Entity<MortarComponent> mortar, ref MortarDialBuiMsg args)
    {
        args.Target.X.Cap(mortar.Comp.MaxDial);
        args.Target.Y.Cap(mortar.Comp.MaxDial);

        var user = args.Actor;
        var ev = new DialMortarDoAfterEvent(args.Target);
        var doAfter = new DoAfterArgs(EntityManager, user, mortar.Comp.TargetDelay, ev, mortar)
        {
            BreakOnMove = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            var selfMsg = Loc.GetString("rmc-mortar-dial-start-self", ("mortar", mortar));
            var othersMsg = Loc.GetString("rmc-mortar-dial-start-others", ("user", user), ("mortar", mortar));
            _popup.PopupPredicted(selfMsg, othersMsg, user, user);
        }
    }

    private void DeployMortar(Entity<MortarComponent> mortar, EntityUid user)
    {
        if (mortar.Comp.Deployed)
            return;

        if (!CanDeployPopup(mortar, user))
            return;

        var ev = new DeployMortarDoAfterEvent();
        var args = new DoAfterArgs(EntityManager, user, mortar.Comp.DeployDelay, ev, mortar)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupClient(Loc.GetString("rmc-mortar-deploy-start", ("mortar", mortar)), user, user);
    }

    private bool CanDeployPopup(Entity<MortarComponent> mortar, EntityUid user)
    {
        //if (!_area.CanMortarPlacement(user.ToCoordinates()))
        //{
        //    _popup.PopupClient(Loc.GetString("rmc-mortar-covered", ("mortar", mortar)), user, user, PopupType.SmallCaution);
        //    return false;
        //}
        // Need a system to check for a roof

        return true;
    }

    protected virtual bool CanLoadPopup(
        Entity<MortarComponent> mortar,
        Entity<MortarShellComponent> shell,
        EntityUid user,
        out TimeSpan travelTime,
        out MapCoordinates coordinates)
    {
        travelTime = default;
        coordinates = default;
        return false;
    }

    public void PopupWarning(MapCoordinates coordinates, float range, LocId warning, LocId warningAbove)
    {
        foreach (var session in _player.NetworkedSessions)
        {
            if (session.AttachedEntity is not { } recipient ||
                !_transformQuery.TryComp(recipient, out var xform) ||
                xform.MapID != coordinates.MapId)
            {
                continue;
            }

            var sessionCoordinates = _transform.GetMapCoordinates(xform);
            var distanceVec = (coordinates.Position - sessionCoordinates.Position);
            var distance = distanceVec.Length();
            if (distance > range)
                continue;

            var direction = distanceVec.GetDir().ToString().ToUpperInvariant();
            var msg = distance < 1
                ? Loc.GetString(warningAbove)
                : Loc.GetString(warning, ("direction", direction));
            _popup.PopupEntity(msg, recipient, recipient, PopupType.LargeCaution);
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var shells = EntityQueryEnumerator<ActiveMortarShellComponent>();
        while (shells.MoveNext(out var uid, out var active))
        {
            if (!active.Warned && time >= active.WarnAt)
            {
                active.Warned = true;
                var coordinates = _transform.ToMapCoordinates(active.Coordinates);
                //PopupWarning(coordinates,
                //    active.WarnRange,
                //    "rmc-mortar-shell-warning",
                //    "rmc-mortar-shell-warning-above");
                _audio.PlayPvs(active.WarnSound, active.Coordinates);
            }

            if (!active.ImpactWarned && time >= active.ImpactWarnAt)
            {
                active.ImpactWarned = true;
                var coordinates = _transform.ToMapCoordinates(active.Coordinates);
                //PopupWarning(coordinates,
                //    active.WarnRange,
                //    "rmc-mortar-shell-impact-warning",
                //    "rmc-mortar-shell-impact-warning-above");
            }

            if (time >= active.LandAt)
            {
                _transform.SetCoordinates(uid, active.Coordinates);

                var ev = new MortarShellLandEvent(active.Coordinates);
                RaiseLocalEvent(uid, ref ev);

                _explosion.TriggerExplosive(uid);
            }
        }
    }
}
