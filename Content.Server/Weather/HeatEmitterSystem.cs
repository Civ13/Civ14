using Content.Server.Atmos.EntitySystems;
using Content.Server.Weather;
using Content.Shared.Atmos;
using Content.Shared.Light.Components; // Adicionado para ExpendableLightComponent
using Content.Server.Light.Components; // Adicionado para ExpendableLightComponent
using Robust.Shared.Map;
using Robust.Shared.Map.Components; // Para MapGridComponent
using Robust.Shared.Maths; // Para Vector2i
using Robust.Shared.Timing;
using Content.Server.Atmos.Components;
using Robust.Server.GameObjects;
using Content.Shared.Atmos.Components;


namespace Content.Server.Weather;

/// <summary>
/// System responsible for emitting heat to nearby tiles when a light source is active.
/// </summary>
public sealed class HeatEmitterSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    private float _lastUpdateTime;

    public override void Initialize()
    {
        base.Initialize();
        _lastUpdateTime = (float)_timing.CurTime.TotalSeconds;
    }

    /// <summary>
    /// Updates the system every frame, applying heat to nearby tiles based on active light sources.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = (float)_timing.CurTime.TotalSeconds;
        var deltaTime = currentTime - _lastUpdateTime;
        _lastUpdateTime = currentTime;

        var query = EntityQueryEnumerator<HeatEmitterComponent, ExpendableLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var heater, out var light, out var transform))
        {
            if (!TryComp<PointLightComponent>(uid, out var pointLight) || !pointLight.Enabled)
            {
                Log.Debug($"Tocha {uid} não tem PointLightComponent ou ele está desativado.");
                continue;
            }

            var gridUid = transform.GridUid;

            if (gridUid == null || !TryComp<MapGridComponent>(gridUid.Value, out var grid))
                continue;

            var position = transform.Coordinates;
            var tileIndices = grid.WorldToTile(position.Position);

            // Obter a mistura de gases do tile onde a tocha está
            //var tileMixture = _atmosphere.GetTileMixture(gridUid.Value, transform.MapUid, tileIndices);
            var tileMixture = _atmosphere.GetContainingMixture(uid, true);

            if (tileMixture != null && tileMixture.Temperature != null)
            {
                var currentTemp = tileMixture.Temperature;
                Log.Debug($"Tile {tileIndices} - Temperatura atual: {currentTemp}");

                // Aplicar aquecimento apenas se a temperatura for menor que 30°C (303.15 K)
                if (currentTemp < 303.15f)
                {
                    // Calcular a quantidade de calor a ser adicionada
                    var heatCapacity = _atmosphere.GetHeatCapacity(tileMixture, true); // Capacidade térmica em J/K
                    var deltaT = heater.HeatingRate * deltaTime; // Variação desejada de temperatura em K
                    var dQ = heatCapacity * deltaT; // Calor em Joules

                    Log.Debug($"Adicionando {dQ} Joules ao tile {tileIndices} para aumentar a temperatura em {deltaT} K");

                    // Adicionar o calor usando AddHeat
                    _atmosphere.AddHeat(tileMixture, dQ);

                    // Invalidar o tile para o sistema atmosférico processar, se necessário
                    //if (TryComp<GridAtmosphereComponent>(gridUid.Value, out var gridAtmos))
                    //{
                    //    gridAtmos.InvalidatedCoords.Add(tileIndices);
                    //}
                }
                else
                {
                    Log.Debug($"Tile {tileIndices} já está acima de 30°C ({currentTemp} K), aquecimento não aplicado.");
                }
            }
            else
            {
                Log.Debug($"Nenhuma mistura de gases encontrada para tile {tileIndices}.");
            }
        }
    }

}