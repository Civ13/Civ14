using System.Threading;
using Content.Server.DoAfter;
using Content.Shared.Interaction;

namespace Content.Server.Branch;

public sealed class BranchSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;

    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BranchComponent, InteractHandEvent>(TryBreak);
        SubscribeLocalEvent<BranchComponent, BreakDoAfterComplete>(OnBreakComplete);
        SubscribeLocalEvent<BranchComponent, BreakDoAfterCancel>(OnBreakCancel);
    }

    private void TryBreak(EntityUid uid, BranchComponent component, InteractHandEvent args)
    {
        if (component.CancelToken != null) return;

        component.CancelToken = new CancellationTokenSource();

        var doAfterArgs = new DoAfterEventArgs(args.User, component.BreakTime, default, uid)
        {
            BreakOnTargetMove = true,
            BreakOnUserMove = true,
            BreakOnDamage = false,
            BreakOnStun = true,
            NeedHand = true,
            TargetFinishedEvent = new BreakDoAfterComplete(uid),
            TargetCancelledEvent = new BreakDoAfterCancel(),
        };
        _doAfter.DoAfter(doAfterArgs);
    }

    private void OnBreakComplete(EntityUid uid, BranchComponent component, BreakDoAfterComplete ev)
    {
        component.CancelToken = null;
        var newEntity = component.Entity;
        var pos = Transform(uid).MapPosition;
        EntityManager.SpawnEntity(newEntity, pos);
        _audio.PlayPvs(component.Sound, uid);
        EntityManager.DeleteEntity(uid);
    }

    private void OnBreakCancel(EntityUid uid, BranchComponent component, BreakDoAfterCancel args)
    {
        component.CancelToken = null;
    }

    private sealed class BreakDoAfterComplete : EntityEventArgs
    {
        public readonly EntityUid User;

        public BreakDoAfterComplete(EntityUid uid)
        {
            User = uid;
        }
    }

    private sealed class BreakDoAfterCancel : EntityEventArgs { }
}

