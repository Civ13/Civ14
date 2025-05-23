using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Overlays;
using System.Linq;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed class ShowFactionIconsSystem : EquipmentHudSystem<ShowFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, ShowFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}

public sealed class ShowFrenchFactionIconsSystem : EquipmentHudSystem<ShowFrenchFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowFrenchFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, ShowFrenchFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}
public sealed class ShowEnglishFactionIconsSystem : EquipmentHudSystem<ShowEnglishFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowEnglishFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, ShowEnglishFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}
public sealed class ShowGermanFactionIconsSystem : EquipmentHudSystem<ShowGermanFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowGermanFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, ShowGermanFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}
public sealed class ShowSovietFactionIconsSystem : EquipmentHudSystem<ShowSovietFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowSovietFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, ShowSovietFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}
public sealed class ShowUsFactionIconsSystem : EquipmentHudSystem<ShowUsFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowUsFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    private void OnGetStatusIconsEvent(EntityUid uid, ShowUsFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}

