using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Standing;
using Robust.Shared.Physics.Events;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Content.Shared.Mobs.Systems;

namespace Content.Shared.Damage.Components;

public sealed class RequireProjectileTargetSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    private ISawmill _sawmill = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<RequireProjectileTargetComponent, PreventCollideEvent>(PreventCollide);
        SubscribeLocalEvent<RequireProjectileTargetComponent, StoodEvent>(StandingBulletHit);
        SubscribeLocalEvent<RequireProjectileTargetComponent, DownedEvent>(LayingBulletPass);

        _sawmill = _logManager.GetSawmill("targeting");
    }

    private void PreventCollide(Entity<RequireProjectileTargetComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!ent.Comp.Active)
        {
            return;
        }
        else
        {
            if (_mobState.IsDead(args.OtherEntity))
            { args.Cancelled = true; }
            //_sawmill.Info("checking");
            var rando = _random.NextFloat(0.0f, 100.0f);
            // 20% chance get hit
            if (rando >= 80.0f)
            {
                //_sawmill.Info("20%");
                return;
            }

        }

        var other = args.OtherEntity;
        // Resolve the ProjectileComponent on the 'other' entity (the projectile)
        if (TryComp(other, out ProjectileComponent? projectileComp) &&
            CompOrNull<TargetedProjectileComponent>(other)?.Target != ent)
        {
            // Prevents shooting out of while inside of crates
            var shooter = projectileComp.Shooter;
            if (!shooter.HasValue)
                return;

            // ProjectileGrenades delete the entity that's shooting the projectile,
            // so it's impossible to check if the entity is in a container
            if (TerminatingOrDeleted(shooter.Value))
            {
                // If the shooter is deleted, nullify the reference in the projectile component
                // to prevent network errors when serializing ProjectileComponent's state.
                // This ensures that GetNetEntity won't be called on a deleted entity.
                projectileComp.Shooter = null;
                Dirty(other, projectileComp); // Mark the ProjectileComponent as dirty so the change is networked.
                return;
            }

            if (!_container.IsEntityOrParentInContainer(shooter.Value))
                args.Cancelled = true;
        }
    }

    private void SetActive(Entity<RequireProjectileTargetComponent> ent, bool value)
    {
        if (ent.Comp.Active == value)
            return;

        ent.Comp.Active = value;
        Dirty(ent);
    }

    private void StandingBulletHit(Entity<RequireProjectileTargetComponent> ent, ref StoodEvent args)
    {
        SetActive(ent, false);
    }

    private void LayingBulletPass(Entity<RequireProjectileTargetComponent> ent, ref DownedEvent args)
    {
        SetActive(ent, true); // stalker-changes
    }
}
