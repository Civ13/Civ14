using System.Threading;
using Content.Server.DoAfter;
using Content.Shared.Verbs;

namespace Content.Server.Branch;

public sealed class BranchSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;

    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BranchComponent, GetVerbsEvent<AlternativeVerb>>(AddSharpenVerb);
        SubscribeLocalEvent<BranchComponent, SharpenDoAfterComplete>(OnSharpenComplete);
        SubscribeLocalEvent<BranchComponent, SharpenDoAfterCancel>(OnSharpenCancel);
    }

    private void TrySharpen(EntityUid uid, BranchComponent component, GetVerbsEvent<AlternativeVerb> args)
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
            TargetFinishedEvent = new SharpenDoAfterComplete(uid),
            TargetCancelledEvent = new SharpenDoAfterCancel(),
        };
        _doAfter.DoAfter(doAfterArgs);
    }

    private void OnSharpenComplete(EntityUid uid, BranchComponent component, SharpenDoAfterComplete ev)
    {
        component.CancelToken = null;
        var newEntity = component.Entity;
        var pos = Transform(uid).MapPosition;
        EntityManager.SpawnEntity(newEntity, pos);
        _audio.PlayPvs(component.Sound, uid);
        EntityManager.DeleteEntity(uid);
    }

    private void OnSharpenCancel(EntityUid uid, BranchComponent component, SharpenDoAfterCancel args)
    {
        component.CancelToken = null;
    }

    private sealed class SharpenDoAfterComplete : EntityEventArgs
    {
        public readonly EntityUid User;

        public SharpenDoAfterComplete(EntityUid uid)
        {
            User = uid;
        }
    }

    private sealed class SharpenDoAfterCancel : EntityEventArgs { }

    private void AddSharpenVerb(EntityUid uid, BranchComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract || args.Hands == null)
                return;

            AlternativeVerb verb = new()
            {
                Act = () => TrySharpen(uid, component, args),
                Text = Loc.GetString("sharpen-verb"),
            };

            args.Verbs.Add(verb);
        }
}

