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

namespace Content.Server.Kitchen;

public sealed class GrillFuelBurnSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly SharedStackSystem _stackSystem = default!;

    private readonly Dictionary<EntityUid, float> _remainingBurnTime = new();

    public override void Initialize()
    {
        Log.Debug("INITIALIZING");
        base.Initialize();
        SubscribeLocalEvent<GrillFuelBurnComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GrillFuelBurnComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GrillFuelBurnComponent, ItemPlacedEvent>(OnItemPlaced);
        SubscribeLocalEvent<GrillFuelBurnComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnMapInit(EntityUid uid, GrillFuelBurnComponent component, MapInitEvent args)
    {
        _remainingBurnTime[uid] = component.Fuel * 2f * 60f; // Assuming 2 minutes per initial fuel unit
        AdjustHeaterSetting(uid, component);
    }

    private void OnItemPlaced(EntityUid uid, GrillFuelBurnComponent comp, ref ItemPlacedEvent args)
    {
        var itemProto = _entityManager.GetComponent<MetaDataComponent>(args.OtherEntity).EntityPrototype?.ID;
        Log.Debug($"Item placed on campfire: {itemProto}");
    }

    private void OnInteractUsing(EntityUid uid, GrillFuelBurnComponent comp, InteractUsingEvent args)
    {
        if (!_entityManager.TryGetComponent<BurnFuelComponent>(args.Used, out var burnFuel))
        {
            Log.Debug($"Item {args.Used} has no BurnFuelComponent");
            return;
        }

        var itemProto = _entityManager.GetComponent<MetaDataComponent>(args.Used).EntityPrototype?.ID;
        Log.Debug($"InteractUsing on campfire with: {itemProto}, BurnTime: {burnFuel.BurnTime}");

        if (_entityManager.TryGetComponent<StackComponent>(args.Used, out var stackComp))
        {
            int availableFuel = stackComp.Count;
            int fuelNeeded = comp.MaxFuel - comp.Fuel;
            int fuelToAdd = Math.Min(availableFuel, fuelNeeded);

            if (fuelToAdd > 0)
            {
                Log.Debug($"Adding {fuelToAdd} fuel units from stack of {stackComp.Count}");
                comp.Fuel += fuelToAdd;
                _remainingBurnTime[uid] = _remainingBurnTime.GetValueOrDefault(uid) + (fuelToAdd * burnFuel.BurnTime * 60f);
                _stackSystem.SetCount(args.Used, stackComp.Count - fuelToAdd, stackComp);

                if (stackComp.Count <= 0)
                {
                    Log.Debug("Stack depleted, deleting item");
                    QueueDel(args.Used);
                }

                AdjustHeaterSetting(uid, comp);
                //_audio.PlayPvs("/Audio/Effects/click.ogg", uid); //Fuel added sound asset
                args.Handled = true;
            }
            else
            {
                Log.Debug("Fuel at max, no fuel added from stack");
            }
        }
        else
        {
            if (comp.Fuel < comp.MaxFuel)
            {
                Log.Debug("Adding 1 fuel unit from non-stack item");
                comp.Fuel++;
                _remainingBurnTime[uid] = _remainingBurnTime.GetValueOrDefault(uid) + (burnFuel.BurnTime * 60f);
                QueueDel(args.Used);
                AdjustHeaterSetting(uid, comp);
                //_audio.PlayPvs("/Audio/Effects/click.ogg", uid); // Fuel added, consume the whole stack
                args.Handled = true;
            }
            else
            {
                Log.Debug("Fuel at max, item not consumed");
            }
        }
    }

    public override void Update(float deltaTime)
    {
        var query = EntityQueryEnumerator<GrillFuelBurnComponent, ItemPlacerComponent>();
        while (query.MoveNext(out var uid, out var comp, out var placer))
        {
            if (comp.Fuel <= 0 || _remainingBurnTime.GetValueOrDefault(uid) <= 0)
            {
                var coordinates = Transform(uid).Coordinates;
                Spawn("Coal1", coordinates);
                QueueDel(uid);
                _remainingBurnTime.Remove(uid);
                AdjustHeaterSetting(uid, comp); // Ensure Off state
                continue;
            }

            _remainingBurnTime[uid] -= deltaTime;
            AdjustHeaterSetting(uid, comp); // Check setting based on remaining time

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

    private void OnExamined(EntityUid uid, GrillFuelBurnComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var remainingTime = _remainingBurnTime.GetValueOrDefault(uid) / 60f;
        args.PushMarkup($"The campfire has approximately {remainingTime:F1} minutes of fuel remaining.");
    }

    public void ChangeSetting(EntityUid uid, EntityHeaterSetting setting, GrillFuelBurnComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        comp.Setting = setting;
        Log.Debug($"Changing setting to {setting} for campfire {uid}");
        _appearance.SetData(uid, EntityHeaterVisuals.Setting, setting);
    }

    private void AdjustHeaterSetting(EntityUid uid, GrillFuelBurnComponent comp)
    {
        var remainingTimeSeconds = _remainingBurnTime.GetValueOrDefault(uid);
        EntityHeaterSetting newSetting;

        if (remainingTimeSeconds > 600f) // > 6 minutes
            newSetting = EntityHeaterSetting.High;
        else if (remainingTimeSeconds > 300f) // 5-10 minutes
            newSetting = EntityHeaterSetting.Medium;
        else if (remainingTimeSeconds > 0f) // < 5 minutes
            newSetting = EntityHeaterSetting.Low;
        else
            newSetting = EntityHeaterSetting.Off;

        if (comp.Setting != newSetting)
        {
            Log.Debug($"Adjusting heater setting from {comp.Setting} to {newSetting} (Remaining Time: {remainingTimeSeconds / 60f:F1} minutes)");
            ChangeSetting(uid, newSetting, comp);
        }
    }

    private float SettingPower(EntityHeaterSetting setting)
    {
        switch (setting)
        {
            case EntityHeaterSetting.Low:
                return 800f;
            case EntityHeaterSetting.Medium:
                return 1600f;
            case EntityHeaterSetting.High:
                return 2400f;
            default:
                return 0f;
        }
    }
}