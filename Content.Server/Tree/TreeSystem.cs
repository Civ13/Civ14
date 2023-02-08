using Content.Server.Tree;
using Content.Shared.Interaction;

namespace Content.Server.Tree;

public sealed class TreeSystem : EntitySystem
{
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<TreeComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnInteractHand(EntityUid uid, TreeComponent component, InteractHandEvent args)
    {
        if (!_interactionSystem.InRangeUnobstructed(args.User, args.Target))
            return;
        var pos = Transform(uid).MapPosition;
        EntityManager.SpawnEntity("SharpenedStick", pos);
        _audio.PlayPvs(component.Sound, uid);
    }
}

