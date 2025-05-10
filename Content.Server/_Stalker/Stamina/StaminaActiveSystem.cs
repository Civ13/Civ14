using Content.Shared._Stalker.Stamina;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Content.Server.Chat.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Stalker.Stamina;

public sealed class StaminaActiveSystem : EntitySystem
{
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    [Dependency] private readonly IGameTiming _gameTiming = default!;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<StaminaActiveComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<StaminaComponent, MovementSpeedModifierComponent, StaminaActiveComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out var stamina, out var modifier, out var active, out var input))
        {
            var curTime = _gameTiming.CurTime;
            if (stamina.StaminaDamage > stamina.SlowdownThreshold)
            {
                if ((curTime - stamina.LastMessageTime).TotalSeconds >= 6)
                {
                    _chat.TryEmoteWithChat(uid, "BreathGasp");
                    stamina.LastMessageTime = curTime; // Update last message time
                }
            }
            // If our entity is slowed, we can't apply new speed/speed modifiers
            // Because CurrentSprintSpeed will change
            if (!active.Slowed)
            {
                active.SprintModifier = modifier.BaseWalkSpeed / modifier.BaseSprintSpeed;
            }

            if (!TryComp<PhysicsComponent>(uid, out var phys))
                return;

            // If Walk button pressed we will apply stamina damage.
            if (input.HeldMoveButtons.HasFlag(MoveButtons.Walk) && !active.Slowed && phys.LinearVelocity.Length() != 0)
            {
                _stamina.TakeStaminaDamage(uid, active.RunStaminaDamage, stamina, visual: false);
            }

            // If our entity gets through SlowThreshold, we will apply slowing.
            // If our entity is slowed already, we don't need to multiply SprintModifier.
            if (stamina.StaminaDamage >= active.SlowThreshold && active.Slowed == false)
            {
                active.Slowed = true;
                active.Change = true;
                _speed.RefreshMovementSpeedModifiers(uid);
                return;
            }

            // If our entity revives until ReviveStaminaLevel we will remove same SprintModifier.
            // If our entity is already revived, we _don't need to remove SprintModifier.
            if (stamina.StaminaDamage <= active.ReviveStaminaLevel && active.Slowed)
            {
                active.Slowed = false;
                active.Change = true;
                _speed.RefreshMovementSpeedModifiers(uid);
                return;
            }
        }
    }

    private void OnRefresh(EntityUid uid, StaminaActiveComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (!component.Change)
            return;

        var sprint = component.Slowed
            ? component.SprintModifier
            : args.SprintSpeedModifier;

        args.ModifySpeed(args.WalkSpeedModifier, sprint);
    }
}
