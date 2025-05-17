using Content.Server.Weapons.Ranged.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly ITimerManager _timerManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    private ISawmill _sawmill = default!;
    protected override void InitializeCartridge()
    {
        _sawmill = _logManager.GetSawmill("cartridge");
        base.InitializeCartridge();
        SubscribeLocalEvent<CartridgeAmmoComponent, ExaminedEvent>(OnCartridgeExamine);
        SubscribeLocalEvent<CartridgeAmmoComponent, DamageExamineEvent>(OnCartridgeDamageExamine);

        // Handle cartridges that are already spent when they are initialized
        SubscribeLocalEvent<CartridgeAmmoComponent, ComponentStartup>(OnCartridgeStartupForDeletionCheck);
    }
    private void OnCartridgeDamageExamine(EntityUid uid, CartridgeAmmoComponent component, ref DamageExamineEvent args)
    {
        var damageSpec = GetProjectileDamage(component.Prototype);

        if (damageSpec == null)
            return;

        _damageExamine.AddDamageExamine(args.Message, Damageable.ApplyUniversalAllModifiers(damageSpec), Loc.GetString("damage-projectile"));
    }

    private DamageSpecifier? GetProjectileDamage(string proto)
    {
        if (!ProtoManager.TryIndex<EntityPrototype>(proto, out var entityProto))
            return null;

        if (entityProto.Components
            .TryGetValue(_factory.GetComponentName(typeof(ProjectileComponent)), out var projectile))
        {
            var p = (ProjectileComponent)projectile.Component;

            if (!p.Damage.Empty)
            {
                return p.Damage * Damageable.UniversalProjectileDamageModifier;
            }
        }

        return null;
    }

    private void OnCartridgeExamine(EntityUid uid, CartridgeAmmoComponent component, ExaminedEvent args)
    {
        if (component.Spent)
        {
            args.PushMarkup(Loc.GetString("gun-cartridge-spent"));
        }
        else
        {
            args.PushMarkup(Loc.GetString("gun-cartridge-unspent"));
        }
    }

    protected override void SetCartridgeSpent(EntityUid uid, CartridgeAmmoComponent cartridge, bool spent)
    {
        base.SetCartridgeSpent(uid, cartridge, spent);

        // Now call the server-specific logic after the spent status is updated
        CheckAndScheduleSpentCartridgeDeletion(uid, cartridge);
    }

    private void OnCartridgeStartupForDeletionCheck(EntityUid uid, CartridgeAmmoComponent component, ComponentStartup args)
    {
        CheckAndScheduleSpentCartridgeDeletion(uid, component);
    }

    /// <summary>
    /// Checks if a cartridge is spent and, if so, schedules it for deletion after a delay.
    /// This function can be called when a cartridge's state might have changed to spent.
    /// </summary>
    public void CheckAndScheduleSpentCartridgeDeletion(EntityUid cartridgeUid, CartridgeAmmoComponent cartridge)
    {
        if (cartridge.Spent)
        {
            // If not already marked for deletion
            if (!EntityManager.HasComponent<DeletingSpentCartridgeComponent>(cartridgeUid))
            {
                EntityManager.AddComponent<DeletingSpentCartridgeComponent>(cartridgeUid);
                //_sawmill.Info($"Scheduling spent cartridge {ToPrettyString(cartridgeUid)} for deletion in 5 minutes.");

                _timerManager.AddTimer(new Timer((int)TimeSpan.FromMinutes(5).TotalMilliseconds, false, () =>
                {
                    // Re-check conditions before deleting, as state might have changed or entity might be gone.
                    if (EntityManager.Deleted(cartridgeUid))
                        return;

                    if (EntityManager.TryGetComponent<CartridgeAmmoComponent>(cartridgeUid, out var currentCartridge) && currentCartridge.Spent)
                    {
                        _sawmill.Info($"Deleting spent cartridge {ToPrettyString(cartridgeUid)} after 5 minutes.");
                        EntityManager.QueueDeleteEntity(cartridgeUid);
                    }
                    else
                    {
                        // Condition no longer met (e.g., no longer spent, or component removed), so just remove the marker.
                        EntityManager.RemoveComponent<DeletingSpentCartridgeComponent>(cartridgeUid);
                    }
                }));
            }
        }
        else if (EntityManager.HasComponent<DeletingSpentCartridgeComponent>(cartridgeUid))
        {
            // If it's not spent but was somehow marked, remove the marker.
            // Note: This doesn't cancel the timer here, but the timer's callback will handle it.
            EntityManager.RemoveComponent<DeletingSpentCartridgeComponent>(cartridgeUid);
        }
    }
}
