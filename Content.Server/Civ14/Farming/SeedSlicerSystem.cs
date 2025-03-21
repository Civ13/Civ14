using Content.Server.Botany.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Botany;

namespace Content.Server.Botany.Systems;

public sealed class SeedSlicerSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly BotanySystem _botanySystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SeedSlicerComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(EntityUid uid, SeedSlicerComponent slicer, AfterInteractEvent args)
    {
        var target = args.Target;
        if (target == null)
            return;

        var user = args.User;

        Log.Debug("OnAfterInteract 1");

        if (!TryComp<ProduceComponent>(target.Value, out var produce))
            return;

        Log.Debug("OnAfterInteract 2");

        if (!_botanySystem.TryGetSeed(produce, out var seed) || seed.Seedless)
        {
            return;
        }

        // Obtém o nome da entidade do MetaDataComponent
        string entityName = "unknown entity";
        if (TryComp<MetaDataComponent>(target.Value, out var metaData))
        {
            entityName = metaData.EntityName;
        }

        _popup.PopupCursor($"You extract a seed from the {entityName}.", user, PopupType.Medium);

        QueueDel(target.Value);

        var coords = Transform(uid).Coordinates;
        _botanySystem.SpawnSeedPacket(seed, coords, user);

        args.Handled = true;
    }
}