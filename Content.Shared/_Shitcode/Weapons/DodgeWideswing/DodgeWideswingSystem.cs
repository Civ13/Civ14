// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Ilya246 <57039557+Ilya246@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Random;

namespace Content.Shared.Goobstation.Weapons.DodgeWideswing;

public sealed class DodgeWideswingSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;

    /// <summary>
    /// Subscribes to damage change events for entities with the <see cref="DodgeWideswingComponent"/>.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DodgeWideswingComponent, BeforeDamageChangedEvent>(OnDamageChanged);
    }

    /// <summary>
    /// Handles incoming heavy attack damage for entities with a DodgeWideswingComponent, potentially converting the damage into stamina loss and cancelling the original damage based on configured chance and conditions.
    /// </summary>
    /// <param name="uid">The entity receiving the damage.</param>
    /// <param name="component">The DodgeWideswingComponent associated with the entity.</param>
    /// <param name="args">The event data for the incoming damage, passed by reference.</param>
    private void OnDamageChanged(EntityUid uid, DodgeWideswingComponent component, ref BeforeDamageChangedEvent args)
    {
        if (args.HeavyAttack && (!HasComp<KnockedDownComponent>(uid) || component.WhenKnockedDown) && _random.Prob(component.Chance))
        {
            _stamina.TakeStaminaDamage(uid, args.Damage.GetTotal().Float() * component.StaminaRatio, source: args.Origin, immediate: false);

            if (component.PopupId != null)
                _popup.PopupPredicted(Loc.GetString(component.PopupId, ("target", uid)), uid, args.Origin);

            args.Cancelled = true;
        }
    }
}
