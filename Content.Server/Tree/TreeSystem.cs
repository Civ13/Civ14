using Content.Server.Tree;
using Content.Server.DoAfter;
using Content.Shared.Interaction;

namespace Content.Server.Tree;

public sealed class TreeSystem : EntitySystem
{
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<TreeComponent, AfterInteractEvent>(AfterInteractOn);
        SubscribeLocalEvent<TreeComponent>(OnDoAfterComplete);
    }

    private void OnInteractHand(EntityUid uid, TreeComponent component, InteractHandEvent args)
    {
        if (!_interactionSystem.InRangeUnobstructed(args.User, args.Target))
            return;
        var pos = Transform(uid).MapPosition;
        EntityManager.SpawnEntity("SharpenedStick", pos);
        _audio.PlayPvs(component.Sound, uid);
    }

    private void OnDoAfterComplete(EntityUid uid, TreeComponent component)
        {
            var pos = Transform(uid).MapPosition;
            EntityManager.SpawnEntity("SharpenedStick", pos);
            _audio.PlayPvs(component.Sound, uid);
            }
        }

    private void AfterInteractOn(EntityUid uid, TreeComponent component, AfterInteractEvent args)
    {
        var doAfterEventArgs = new DoAfterEventArgs(args.User, component.BreakTime, default, target)
        {
            BreakOnTargetMove = true,
            BreakOnUserMove = true,
            BreakOnDamage = true,
            BreakOnStun = true,
            NeedHand = true,
            BroadcastFinishedEvent = new TreeBranchDoAfterComplete(uid, target, component, args.User),
        };
        _doAfterSystem.DoAfter(doAfterEventArgs);
    }


    private sealed class TreeBranchDoAfterComplete : EntityEventArgs
    {
        public readonly EntityUid User;
        public readonly EntityUid UsedTool;
        public readonly EntityUid Target;
        public readonly TreeComponent Component;

        public TreeBranchDoAfterComplete(EntityUid usedTool, EntityUid target, string sprite,
            TreeComponent component, EntityUid user)
        {
            User = user;
            UsedTool = usedTool;
            Target = target;
            Component = component;
        }
    }
}

