using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Kitchen;
using Content.Shared.Placeable;
using Content.Shared.Stacks;
using Content.Shared.Temperature;
using Robust.Server.Audio;
using Robust.Shared.Timing;
using System.Linq;
using Content.Server.IgnitionSource;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Server.Kitchen;

public sealed class GrillFuelBurnSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly SharedStackSystem _stackSystem = default!;

    [Dependency] private readonly SharedPointLightSystem _pointLightSystem = default!;

    private readonly Dictionary<EntityUid, float> _remainingBurnTime = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GrillFuelBurnComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GrillFuelBurnComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GrillFuelBurnComponent, ItemPlacedEvent>(OnItemPlaced);
        SubscribeLocalEvent<GrillFuelBurnComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnMapInit(EntityUid uid, GrillFuelBurnComponent component, MapInitEvent args)
    {
        _remainingBurnTime[uid] = component.Fuel * 2f * 60f;
        component.IsLit = false;
    }

    private void OnItemPlaced(EntityUid uid, GrillFuelBurnComponent comp, ref ItemPlacedEvent args)
    {
        var itemProto = _entityManager.GetComponent<MetaDataComponent>(args.OtherEntity).EntityPrototype?.ID;
    }

    private void OnInteractUsing(EntityUid uid, GrillFuelBurnComponent comp, InteractUsingEvent args)
    {
        if (_entityManager.TryGetComponent<IgnitionSourceComponent>(args.Used, out var ignitionSource))
        {
            if (!comp.IsLit && ignitionSource.Ignited && _remainingBurnTime[uid] > 0)
            {
                comp.IsLit = true;
                AdjustHeaterSetting(uid, comp);
            }
            args.Handled = true;
            return;
        }

        // Add fuel
        if (!_entityManager.TryGetComponent<BurnFuelComponent>(args.Used, out var burnFuel))
        {
            return;
        }

        var itemProto = _entityManager.GetComponent<MetaDataComponent>(args.Used).EntityPrototype?.ID;

        if (_entityManager.TryGetComponent<StackComponent>(args.Used, out var stackComp))
        {
            int availableFuel = stackComp.Count;
            int fuelNeeded = comp.MaxFuel - comp.Fuel;
            int fuelToAdd = Math.Min(availableFuel, fuelNeeded);

            if (fuelToAdd > 0)
            {
                comp.Fuel += fuelToAdd;
                _remainingBurnTime[uid] += fuelToAdd * burnFuel.BurnTime * 60f;
                _stackSystem.SetCount(args.Used, stackComp.Count - fuelToAdd, stackComp);

                if (stackComp.Count <= 0)
                {
                    QueueDel(args.Used);
                }

                AdjustHeaterSetting(uid, comp);
                args.Handled = true;
            }
        }
        else
        {
            if (comp.Fuel < comp.MaxFuel)
            {
                comp.Fuel++;
                _remainingBurnTime[uid] += burnFuel.BurnTime * 60f;
                QueueDel(args.Used);
                AdjustHeaterSetting(uid, comp);
                args.Handled = true;
            }
        }
    }

    public override void Update(float deltaTime)
    {
        var query = EntityQueryEnumerator<GrillFuelBurnComponent, ItemPlacerComponent>();
        while (query.MoveNext(out var uid, out var comp, out var placer))
        {
            if (comp.IsLit && comp.Expends == true)
            {
                if (comp.Fuel <= 0 || _remainingBurnTime.GetValueOrDefault(uid) <= 0)
                {
                    var coordinates = Transform(uid).Coordinates;
                    Spawn("Coal1", coordinates);
                    QueueDel(uid);
                    _remainingBurnTime.Remove(uid);
                    comp.IsLit = false;
                    AdjustHeaterSetting(uid, comp);
                    continue;
                }

                _remainingBurnTime[uid] -= deltaTime;
                AdjustHeaterSetting(uid, comp);

                if (comp.Setting != EntityHeaterSetting.Off)
                {
                    var energy = SettingPower(comp.Setting) * deltaTime;
                    foreach (var ent in placer.PlacedEntities)
                    {
                        _temperature.ChangeHeat(ent, energy);
                    }
                }
            }
        }
    }

    private void OnExamined(EntityUid uid, GrillFuelBurnComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var remainingTime = _remainingBurnTime.GetValueOrDefault(uid) / 60f;
        args.PushMarkup($"Has approximately {remainingTime:F1} minutes of fuel remaining.");
    }

    public void ChangeSetting(EntityUid uid, EntityHeaterSetting setting, GrillFuelBurnComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        comp.Setting = setting;
        _appearance.SetData(uid, EntityHeaterVisuals.Setting, setting);

        // Adjust the PointLight based on the Setting
        if (_entityManager.TryGetComponent<PointLightComponent>(uid, out var lightComp))
        {
            switch (setting)
            {
                case EntityHeaterSetting.Off:
                    _pointLightSystem.SetEnabled(uid, false, lightComp);
                    break;
                case EntityHeaterSetting.Low:
                    _pointLightSystem.SetEnabled(uid, true, lightComp);
                    _pointLightSystem.SetRadius(uid, 2.0f, lightComp);
                    _pointLightSystem.SetEnergy(uid, 2.0f, lightComp);
                    break;
                case EntityHeaterSetting.Medium:
                    _pointLightSystem.SetEnabled(uid, true, lightComp);
                    _pointLightSystem.SetRadius(uid, 4.0f, lightComp);
                    _pointLightSystem.SetEnergy(uid, 4.0f, lightComp);
                    break;
                case EntityHeaterSetting.High:
                    _pointLightSystem.SetEnabled(uid, true, lightComp);
                    _pointLightSystem.SetRadius(uid, 6.0f, lightComp);
                    _pointLightSystem.SetEnergy(uid, 6.0f, lightComp);
                    break;
            }
        }
        else
        {
            Log.Warning($"No PointLightComponent found for campfire {uid}");
        }
    }
    private void AdjustHeaterSetting(EntityUid uid, GrillFuelBurnComponent comp)
    {
        if (!comp.IsLit) // If its not lit, updates to Off
        {
            if (comp.Setting != EntityHeaterSetting.Off)
            {
                ChangeSetting(uid, EntityHeaterSetting.Off, comp);
            }
            return;
        }

        var remainingTimeSeconds = _remainingBurnTime.GetValueOrDefault(uid);
        EntityHeaterSetting newSetting;

        if (remainingTimeSeconds > 600f) // > 10 minutes
            newSetting = EntityHeaterSetting.High;
        else if (remainingTimeSeconds > 300f) // 5-10 minutes
            newSetting = EntityHeaterSetting.Medium;
        else if (remainingTimeSeconds > 0f) // < 5 minutes
            newSetting = EntityHeaterSetting.Low;
        else
            newSetting = EntityHeaterSetting.Off;

        if (comp.Setting != newSetting)
        {
            ChangeSetting(uid, newSetting, comp);
        }
    }

    private float SettingPower(EntityHeaterSetting setting)
    {
        switch (setting)
        {
            case EntityHeaterSetting.Low:
                return 400f;
            case EntityHeaterSetting.Medium:
                return 800f;
            case EntityHeaterSetting.High:
                return 1600f;
            default:
                return 0f;
        }
    }
}
