using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Jittering;
using Content.Shared.Projectiles;
using Content.Shared.Rejuvenate;
using Content.Shared.Rounding;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random; // Goob - Shove
using Robust.Shared.Timing;
using Content.Shared.Common.Stunnable;

namespace Content.Shared.Damage.Systems;

public sealed partial class StaminaSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
    [Dependency] private readonly SharedStunSystem _stunSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffect = default!; // goob edit
    [Dependency] private readonly SharedStutteringSystem _stutter = default!; // goob edit
    [Dependency] private readonly SharedJitteringSystem _jitter = default!; // goob edit
    [Dependency] private readonly IRobustRandom _random = default!; // Goob - Shove
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// How much of a buffer is there between the stun duration and when stuns can be re-applied.
    /// </summary>
    private static readonly TimeSpan StamCritBufferTime = TimeSpan.FromSeconds(3f);

    /// <summary>
    /// Initializes the StaminaSystem, setting up event subscriptions for stamina-related components and configuring logging.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        InitializeModifier();

        SubscribeLocalEvent<StaminaComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<StaminaComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<StaminaComponent, AfterAutoHandleStateEvent>(OnStamHandleState);
        SubscribeLocalEvent<StaminaComponent, DisarmedEvent>(OnDisarmed);
        SubscribeLocalEvent<StaminaComponent, RejuvenateEvent>(OnRejuvenate);

        SubscribeLocalEvent<StaminaDamageOnEmbedComponent, EmbedEvent>(OnProjectileEmbed);

        SubscribeLocalEvent<StaminaDamageOnCollideComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<StaminaDamageOnCollideComponent, ThrowDoHitEvent>(OnThrowHit);

        SubscribeLocalEvent<StaminaDamageOnHitComponent, MeleeHitEvent>(OnMeleeHit);

        _sawmill = _logManager.GetSawmill("stamina");
    }

    /// <summary>
    /// Handles stamina state changes after state synchronisation, entering stamina critical state if necessary or updating active stamina components.
    /// </summary>
    private void OnStamHandleState(EntityUid uid, StaminaComponent component, ref AfterAutoHandleStateEvent args)
    {
        // goob edit - stunmeta
        if (component.Critical)
            EnterStamCrit(uid, component, duration: 3f);
        else
        {
            if (component.StaminaDamage > 0f)
                EnsureComp<ActiveStaminaComponent>(uid);

            ExitStamCrit(uid, component);
        }
    }

    private void OnShutdown(EntityUid uid, StaminaComponent component, ComponentShutdown args)
    {
        if (MetaData(uid).EntityLifeStage < EntityLifeStage.Terminating)
        {
            RemCompDeferred<ActiveStaminaComponent>(uid);
        }
        _alerts.ClearAlert(uid, component.StaminaAlert);
    }

    private void OnStartup(EntityUid uid, StaminaComponent component, ComponentStartup args)
    {
        SetStaminaAlert(uid, component);
    }

    [PublicAPI]
    public float GetStaminaDamage(EntityUid uid, StaminaComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return 0f;

        var curTime = _timing.CurTime;
        var pauseTime = _metadata.GetPauseTime(uid);
        return MathF.Max(0f, component.StaminaDamage - MathF.Max(0f, (float)(curTime - (component.NextUpdate + pauseTime)).TotalSeconds * component.Decay));
    }

    private void OnRejuvenate(EntityUid uid, StaminaComponent component, RejuvenateEvent args)
    {
        if (component.StaminaDamage >= component.CritThreshold)
        {
            ExitStamCrit(uid, component);
        }

        component.StaminaDamage = 0;
        RemComp<ActiveStaminaComponent>(uid);
        SetStaminaAlert(uid, component);
        Dirty(uid, component);
    }

    /// <summary>
    /// Applies immediate stamina damage with resistances to an entity when disarmed, unless already handled or in a critical state.
    /// </summary>
    private void OnDisarmed(EntityUid uid, StaminaComponent component, DisarmedEvent args)
    {
        // No random stamina damage
        if (args.Handled)
            return;

        if (component.Critical)
            return;

        TakeStaminaDamage(uid, args.StaminaDamage, component, source: args.Source, applyResistances: true, immediate: true);

        args.PopupPrefix = "disarm-action-shove-";
        args.IsStunned = component.Critical;
        // Shoving shouldnt handle it
    }

    /// <summary>
    /// Handles stamina damage application when an entity with a <see cref="StaminaDamageOnHitComponent"/> lands a melee hit,
    /// splitting immediate and overtime stamina damage among all valid hit entities and applying relevant multipliers and modifiers.
    /// </summary>
    private void OnMeleeHit(EntityUid uid, StaminaDamageOnHitComponent component, MeleeHitEvent args)
    {
        if (!args.IsHit ||
            !args.HitEntities.Any() ||
            component.Damage <= 0f)
        {
            return;
        }

        var ev = new StaminaDamageOnHitAttemptEvent(args.Direction == null, false); // Goob edit
        RaiseLocalEvent(uid, ref ev);
        if (ev.Cancelled)
            return;

        var stamQuery = GetEntityQuery<StaminaComponent>();
        var toHit = new List<(EntityUid Entity, StaminaComponent Component)>();

        // Split stamina damage between all eligible targets.
        foreach (var ent in args.HitEntities)
        {
            if (!stamQuery.TryGetComponent(ent, out var stam))
                continue;

            toHit.Add((ent, stam));
        }

        // Goobstation
        RaiseLocalEvent(uid, new StaminaDamageMeleeHitEvent(toHit, args.Direction));

        // goobstation
        foreach (var (ent, comp) in toHit)
        {
            var hitEvent = new TakeStaminaDamageEvent((ent, comp));
            // raise event for each entity hit
            RaiseLocalEvent(ent, hitEvent);

            if (hitEvent.Handled)
                return;

            var damageImmediate = component.Damage;
            var damageOvertime = component.Overtime;
            damageImmediate *= hitEvent.Multiplier;
            damageImmediate += hitEvent.FlatModifier;
            damageOvertime *= hitEvent.Multiplier;
            damageOvertime += hitEvent.FlatModifier;

            if (args.Direction == null)
            {
                damageImmediate *= component.LightAttackDamageMultiplier;
                damageOvertime *= component.LightAttackOvertimeDamageMultiplier;
            }

            TakeStaminaDamage(ent, damageImmediate / toHit.Count, comp, source: args.User, with: args.Weapon, sound: component.Sound, immediate: true);
            TakeOvertimeStaminaDamage(ent, damageOvertime);
        }
    }

    private void OnProjectileHit(EntityUid uid, StaminaDamageOnCollideComponent component, ref ProjectileHitEvent args)
    {
        OnCollide(uid, component, args.Target);
    }

    /// <summary>
    /// Applies immediate stamina damage with resistances to an entity when a projectile embeds into it.
    /// </summary>
    private void OnProjectileEmbed(EntityUid uid, StaminaDamageOnEmbedComponent component, ref EmbedEvent args)
    {
        if (!TryComp<StaminaComponent>(args.Embedded, out var stamina))
            return;

        TakeStaminaDamage(args.Embedded, component.Damage, stamina, source: uid, applyResistances: true, immediate: true);
    }

    /// <summary>
    /// Applies stamina damage to a target entity when struck by a thrown object.
    /// </summary>
    private void OnThrowHit(EntityUid uid, StaminaDamageOnCollideComponent component, ThrowDoHitEvent args)
    {
        OnCollide(uid, component, args.Target);
    }

    /// <summary>
    /// Applies stamina damage to a target entity upon collision if it has a stamina component, allowing for event-based modification or cancellation.
    /// </summary>
    /// <param name="uid">The entity causing the collision.</param>
    /// <param name="component">The stamina damage on collide component.</param>
    /// <param name="target">The entity being collided with.</param>
    private void OnCollide(EntityUid uid, StaminaDamageOnCollideComponent component, EntityUid target)
    {
        // you can't inflict stamina damage on things with no stamina component
        // this prevents stun batons from using up charges when throwing it at lockers or lights
        if (!TryComp<StaminaComponent>(target, out var stamComp))
            return;

        var ev = new StaminaDamageOnHitAttemptEvent();
        RaiseLocalEvent(uid, ref ev);
        if (ev.Cancelled)
            return;

        // goobstation
        var hitEvent = new TakeStaminaDamageEvent((target, stamComp));
        RaiseLocalEvent(target, hitEvent);

        if (hitEvent.Handled)
            return;

        var damage = component.Damage;
        var overtime = component.Damage;

        damage *= hitEvent.Multiplier;
        damage += hitEvent.FlatModifier;
        overtime *= hitEvent.Multiplier;
        overtime += hitEvent.FlatModifier;

        TakeStaminaDamage(target, damage, source: uid, sound: component.Sound, immediate: true);
        TakeOvertimeStaminaDamage(target, overtime); // Goobstation
    }

    /// <summary>
    /// Updates the stamina alert level for an entity based on its current stamina damage relative to the critical threshold.
    /// </summary>
    private void SetStaminaAlert(EntityUid uid, StaminaComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || component.Deleted)
            return;

        var severity = ContentHelpers.RoundToLevels(MathF.Max(0f, component.CritThreshold - component.StaminaDamage), component.CritThreshold, 7);
        _alerts.ShowAlert(uid, component.StaminaAlert, (short)severity);
    }

    /// <summary>
    /// Tries to take stamina damage without raising the entity over the crit threshold.
    /// <summary>
    /// Attempts to apply stamina damage to an entity, returning whether the entity remains below the critical threshold.
    /// </summary>
    /// <param name="uid">The entity to apply stamina damage to.</param>
    /// <param name="value">The amount of stamina damage to attempt to apply.</param>
    /// <param name="component">Optional stamina component; resolved if not provided.</param>
    /// <param name="source">Optional source of the stamina damage.</param>
    /// <param name="with">Optional weapon or item used to inflict the damage.</param>
    /// <returns>True if the stamina damage was applied and the entity is not in a critical state; false if the entity would exceed or is already at the critical threshold.</returns>
    public bool TryTakeStamina(EntityUid uid, float value, StaminaComponent? component = null, EntityUid? source = null, EntityUid? with = null)
    {
        // Something that has no Stamina component automatically passes stamina checks
        if (!Resolve(uid, ref component, false))
            return true;

        var oldStam = component.StaminaDamage;

        if (oldStam + value > component.CritThreshold || component.Critical)
            return false;

        TakeStaminaDamage(uid, value, component, source, with, visual: false, immediate: true);
        return true;
    }

    /// <summary>
    /// Adds stamina damage over time to the specified entity, accumulating the value in its overtime stamina component.
    /// </summary>
    /// <param name="uid">The entity to receive overtime stamina damage.</param>
    /// <param name="value">The amount of stamina damage to add.</param>
    public void TakeOvertimeStaminaDamage(EntityUid uid, float value)
    {
        // do this only on server side because otherwise shit happens
        if (value == 0)
            return;

        var hasComp = TryComp<OvertimeStaminaDamageComponent>(uid, out var overtime);

        if (!hasComp)
            overtime = EnsureComp<OvertimeStaminaDamageComponent>(uid);

        overtime!.Amount = hasComp ? overtime.Amount + value : value;
        overtime!.Damage = hasComp ? overtime.Damage + value : value;
    }

    /// <summary>
    /// Applies stamina damage to an entity, optionally factoring in resistances, triggering visual and audio effects, and logging the event.
    /// </summary>
    /// <param name="uid">The entity receiving stamina damage.</param>
    /// <param name="value">The amount of stamina damage to apply.</param>
    /// <param name="component">Optional stamina component; resolved if not provided.</param>
    /// <param name="source">Optional source entity responsible for the damage.</param>
    /// <param name="with">Optional entity used to inflict the damage.</param>
    /// <param name="visual">Whether to trigger visual effects for the damage.</param>
    /// <param name="sound">Optional sound to play when damage is applied.</param>
    /// <param name="immediate">If true, applies a hard stun when entering stamina crit.</param>
    /// <param name="applyResistances">If true, applies resistance modifiers before applying damage.</param>
    /// <param name="shouldLog">Whether to log the stamina damage event.</param>
    /// <remarks>
    /// If the entity is already in stamina crit or the event is cancelled, no damage is applied. Exceeding the slowdown threshold applies jitter, stutter, and slowdown effects. Entering stamina crit may apply a hard stun depending on the <paramref name="immediate"/> flag.
    /// </remarks>
    public void TakeStaminaDamage(EntityUid uid, float value, StaminaComponent? component = null,
        EntityUid? source = null, EntityUid? with = null, bool visual = true, SoundSpecifier? sound = null, bool immediate = false, bool applyResistances = false, bool shouldLog = true)
    {
        if (!Resolve(uid, ref component, false)
        || value == 0) // no damage???
            return;

        var ev = new BeforeStaminaDamageEvent(value);
        RaiseLocalEvent(uid, ref ev);
        if (ev.Cancelled)
            return;

        // Have we already reached the point of max stamina damage?
        if (component.Critical)
            return;

        if (applyResistances)
        {
            var hitEvent = new TakeStaminaDamageEvent((uid, component));
            RaiseLocalEvent(uid, hitEvent);

            if (hitEvent.Handled)
                return;

            value *= hitEvent.Multiplier;
            value += hitEvent.FlatModifier;
        }

        var oldDamage = component.StaminaDamage;
        component.StaminaDamage = MathF.Max(0f, component.StaminaDamage + value);

        // Reset the decay cooldown upon taking damage.
        if (oldDamage < component.StaminaDamage)
        {
            var nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(component.Cooldown);

            if (component.NextUpdate < nextUpdate)
                component.NextUpdate = nextUpdate;
        }

        var slowdownThreshold = component.SlowdownThreshold; // stalker-changes

        // If we go above n% then apply effects
        if (component.StaminaDamage > slowdownThreshold)
        {
            // goob edit - stunmeta
            _jitter.DoJitter(uid, TimeSpan.FromSeconds(2f), true);
            _stutter.DoStutter(uid, TimeSpan.FromSeconds(10f), true);
            _stunSystem.TrySlowdown(uid, TimeSpan.FromSeconds(8), true, 0.7f, 0.7f);

        }

        SetStaminaAlert(uid, component);

        if (!component.Critical && component.StaminaDamage >= component.CritThreshold && value > 0) // goob edit
            EnterStamCrit(uid, component, immediate, duration: 3f);
        else if (component.StaminaDamage < component.CritThreshold)
            ExitStamCrit(uid, component);

        EnsureComp<ActiveStaminaComponent>(uid);
        Dirty(uid, component);

        if (value <= 0)
            return;
        if (source != null && shouldLog) // stalker-changes
        {
            _adminLogger.Add(LogType.Stamina, $"{ToPrettyString(source.Value):user} caused {value} stamina damage to {ToPrettyString(uid):target}{(with != null ? $" using {ToPrettyString(with.Value):using}" : "")}");
        }
        else if (shouldLog)  // stalker-changes
        {
            _adminLogger.Add(LogType.Stamina, $"{ToPrettyString(uid):target} took {value} stamina damage");
        }

        if (visual)
        {
            _color.RaiseEffect(Color.Aqua, new List<EntityUid>() { uid }, Filter.Pvs(uid, entityManager: EntityManager));
        }

        if (_net.IsServer)
        {
            _audio.PlayPvs(sound, uid);
        }
    }

    /// <summary>
    /// Enables or disables a stamina drain effect on an entity from a specified source, with an optional speed modification.
    /// </summary>
    /// <param name="target">The entity to apply or remove the stamina drain from.</param>
    /// <param name="drainRate">The rate at which stamina is drained per second.</param>
    /// <param name="enabled">Whether to enable or disable the stamina drain.</param>
    /// <param name="modifiesSpeed">Whether the drain should also affect the entity's movement speed.</param>
    /// <param name="source">The source entity responsible for the drain; if null, the target is used as the source.</param>
    public void ToggleStaminaDrain(EntityUid target, float drainRate, bool enabled, bool modifiesSpeed, EntityUid? source = null)
    {
        if (!TryComp<StaminaComponent>(target, out var stamina))
            return;

        // If theres no source, we assume its the target that caused the drain.
        var actualSource = source ?? target;

        if (enabled)
        {
            stamina.ActiveDrains[actualSource] = (drainRate, modifiesSpeed);
            EnsureComp<ActiveStaminaComponent>(target);
        }
        else
            stamina.ActiveDrains.Remove(actualSource);

        Dirty(target, stamina);
    }

    /// <summary>
    /// Processes stamina updates for all entities with active stamina components, applying stamina drains, handling recovery, and managing entry and exit from stamina critical states.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_timing.IsFirstTimePredicted)
            return;

        var stamQuery = GetEntityQuery<StaminaComponent>();
        var query = EntityQueryEnumerator<ActiveStaminaComponent>();
        var curTime = _timing.CurTime;
        while (query.MoveNext(out var uid, out _))
        {
            // Just in case we have active but not stamina we'll check and account for it.
            if (!stamQuery.TryGetComponent(uid, out var comp) ||
                comp.StaminaDamage <= 0f && !comp.Critical && comp.ActiveDrains.Count == 0)
            {
                RemComp<ActiveStaminaComponent>(uid);
                continue;
            }
            if (comp.ActiveDrains.Count > 0)
                foreach (var (source, (drainRate, modifiesSpeed)) in comp.ActiveDrains)
                    TakeStaminaDamage(uid,
                    drainRate * frameTime,
                    comp,
                    source: source,
                    visual: false);
            // Shouldn't need to consider paused time as we're only iterating non-paused stamina components.
            var nextUpdate = comp.NextUpdate;

            if (nextUpdate > curTime)
                continue;

            // We were in crit so come out of it and continue.
            if (comp.Critical)
            {
                ExitStamCrit(uid, comp);
                continue;
            }

            comp.NextUpdate += TimeSpan.FromSeconds(1f);
            // If theres no active drains, recover stamina.
            if (comp.ActiveDrains.Count == 0)
                TakeStaminaDamage(uid, -comp.Decay, comp);

            Dirty(uid, comp);
        }
    }

    /// <summary>
    /// Puts an entity into stamina critical state, optionally applying a hard stun (paralysis) for a specified duration.
    /// </summary>
    /// <param name="uid">The entity to enter stamina crit.</param>
    /// <param name="component">The stamina component, if already resolved.</param>
    /// <param name="hardStun">If true, applies a full paralysis; otherwise, does not apply a hard stun.</param>
    /// <param name="duration">Duration of the stun effect in seconds if hard stun is applied.</param>
    private void EnterStamCrit(EntityUid uid, StaminaComponent? component = null, bool hardStun = false, float duration = 6f)
    {
        if (!Resolve(uid, ref component) || component.Critical)
        {
            return;
        }
        _sawmill.Info("entering stamcrit");
        if (!hardStun)
        {
            _sawmill.Info("no hardcrit");
            //var parsedDuration = TimeSpan.FromSeconds(duration);
            //if (!_statusEffect.HasStatusEffect(uid, "KnockedDown"))
            //    _stunSystem.TryKnockdown(uid, parsedDuration, true);
            //return;
        }
        else
        {        // you got batonned hard.
            component.Critical = true;
            _stunSystem.TryParalyze(uid, component.StunTime, true);
        }


        component.NextUpdate = _timing.CurTime + component.StunTime + StamCritBufferTime; // Goobstation

        EnsureComp<ActiveStaminaComponent>(uid);
        Dirty(uid, component);

        _adminLogger.Add(LogType.Stamina, LogImpact.Medium, $"{ToPrettyString(uid):user} entered stamina crit");
    }

    // goob edit - made it public.
    // in any case it requires a stamina component that can be freely modified.
    // so it doesn't really matter if it's public or private. besides, very convenient.
    /// <summary>
    /// Exits stamina critical state for the specified entity, resetting stamina damage and related effects.
    /// </summary>
    public void ExitStamCrit(EntityUid uid, StaminaComponent? component = null)
    {
        if (!Resolve(uid, ref component) ||
            !component.Critical)
        {
            return;
        }

        component.Critical = false;
        component.StaminaDamage = 0f;
        component.NextUpdate = _timing.CurTime;
        SetStaminaAlert(uid, component);
        RemComp<ActiveStaminaComponent>(uid);
        Dirty(uid, component);
        _adminLogger.Add(LogType.Stamina, LogImpact.Low, $"{ToPrettyString(uid):user} recovered from stamina crit");
    }
}

/// <summary>
///     Raised before stamina damage is dealt to allow other systems to cancel it.
/// </summary>
[ByRefEvent]
public record struct BeforeStaminaDamageEvent(float Value, bool Cancelled = false);
