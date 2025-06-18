using System.Linq;
using Content.Shared._Stalker.Teleport;
using Content.Shared.Access.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Server._Stalker.Teleports.LocalTeleport;
/// <summary>
/// Use to teleport entities between maps/grids/tiles. Just spawn two portals with the same name.
/// </summary>
public sealed class LocalTeleportSystem : SharedTeleportSystem
{
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _xformSystem = default!;
    [Dependency] private readonly LinkedEntitySystem _linkedEntitySystem = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LocalTeleportComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<LocalTeleportComponent, EndCollideEvent>(OnEndCollide);
    }

    private void OnStartCollide(EntityUid uid, LocalTeleportComponent component, ref StartCollideEvent args)
    {
        var subject = args.OtherEntity;

        // Check for access
        if (!component.AllowAll)
        {
            if (!_accessReaderSystem.IsAllowed(args.OtherEntity, args.OurEntity))
                return;
        }

        // Asked to remove, testing now
        // if (TryComp<SharedPullableComponent>(subject, out var pullable) && pullable.BeingPulled)
        //     _pulling.TryStopPull(pullable);
        //
        // if (TryComp<SharedPullerComponent>(subject, out var pulling)
        //     && pulling.Pulling != null && TryComp<SharedPullableComponent>(pulling.Pulling.Value, out var subjectPulling))
        //     _pulling.TryStopPull(subjectPulling);

        // Remove Timeout from other portal
        if (HasComp<PortalTimeoutComponent>(subject))
            return;

        // If there are no linked entity - link one
        if (!TryComp<LinkedEntityComponent>(uid, out var link))
        {
            var ents = _entMan.GetEntities();
            foreach (var ent in ents)
            {
                if (!TryComp<LocalTeleportComponent>(ent, out var local))
                    continue;

                if (local.PortalName != component.PortalName || uid == ent)
                    continue;

                _linkedEntitySystem.TryLink(uid, ent, true);
            }
        }
        if (link == null)
            return;

        if (!link.LinkedEntities.Any())
            return;

        var target = _random.Pick(link.LinkedEntities);

        // Setup Timeout for an entity to not teleport it after teleport...
        var timeout = EnsureComp<PortalTimeoutComponent>(subject);
        timeout.EnteredPortal = uid;
        Dirty(subject, timeout);

        var xform = Transform(target);
        TeleportEntity(subject, xform.Coordinates);
    }

    private void OnEndCollide(EntityUid uid, LocalTeleportComponent component, ref EndCollideEvent args)
    {
        var subject = args.OtherEntity;

        // Remove Timeout set by other portal.
        if (TryComp<PortalTimeoutComponent>(subject, out var timeout) && timeout.EnteredPortal != uid)
        {
            RemCompDeferred<PortalTimeoutComponent>(subject);
        }
    }
}
