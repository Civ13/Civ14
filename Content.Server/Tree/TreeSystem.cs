using System.Threading;
using Content.Server.DoAfter;
using Content.Shared.Verbs;

namespace Content.Server.Tree;

public sealed class TreeSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TreeComponent, GetVerbsEvent<AlternativeVerb>>(AddBreakVerb);
        SubscribeLocalEvent<TreeComponent, BreakDoAfterComplete>(OnBreakComplete);
        SubscribeLocalEvent<TreeComponent, BreakDoAfterCancel>(OnBreakCancel);
    }

    private void TryBreak(EntityUid uid, TreeComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (component.CancelToken != null) return;

        component.CancelToken = new CancellationTokenSource();

        if (component.Amount > 0f)
        {
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
    }

    private void OnBreakComplete(EntityUid uid, TreeComponent component, BreakDoAfterComplete ev)
    {
        component.CancelToken = null;
        component.Amount--;
        var newEntity = component.Entity;
        var pos = Transform(uid).MapPosition;
        EntityManager.SpawnEntity(newEntity, pos);
        _audio.PlayPvs(component.Sound, uid);
    }

    private void OnBreakCancel(EntityUid uid, TreeComponent component, BreakDoAfterCancel args)
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

    private void AddBreakVerb(EntityUid uid, TreeComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract || args.Hands == null)
                return;

            AlternativeVerb verb = new()
            {
                Act = () => TryBreak(uid, component, args),
                Text = Loc.GetString("break-verb")
            };

            args.Verbs.Add(verb);
        }

}

