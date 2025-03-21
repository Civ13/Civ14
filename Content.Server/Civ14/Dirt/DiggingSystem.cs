using Content.Shared.Interaction;
using Content.Shared.DoAfter;
using Content.Server.DoAfter;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Farming;
using System.Linq;

namespace Content.Server.Farming;

public sealed partial class DiggingSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileManager = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    // Mapeamento dos estágios de escavação
    private readonly Dictionary<string, string> _digProgression = new()
    {
        { "FloorDirt", "FloorDirtDigged_1" },
        { "FloorDirtDigged_1", "FloorDirtDigged_2" },
        { "FloorDirtDigged_2", "FloorDirtDigged_3" }
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DiggingComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<DiggingComponent, DigDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<DiggingComponent> ent, ref AfterInteractEvent args)
    {
        Log.Debug("DIGGING AFTER INTERACT 1");
        if (args.Handled || args.Target != null || !args.CanReach)
            return;

        Log.Debug("DIGGING AFTER INTERACT 2");
        var comp = ent.Comp;
        var user = args.User;
        var clickLocation = args.ClickLocation;

        Log.Debug("DIGGING AFTER INTERACT 2");

        // Obtém o grid clicado
        var gridUid = _transform.GetGrid(clickLocation);
        if (!gridUid.HasValue || !TryComp<MapGridComponent>(gridUid.Value, out var grid))
            return;

        Log.Debug("DIGGING AFTER INTERACT 3");
        // Obtém as coordenadas do tile
        var snapPos = grid.TileIndicesFor(clickLocation);
        var tileRef = _map.GetTileRef(gridUid.Value, grid, snapPos);
        var tileDef = (ContentTileDefinition)_tileManager[tileRef.Tile.TypeId];

        Log.Debug("DIGGING AFTER INTERACT 4");
        // Verifica se o tile pode ser cavado e obtém o próximo estágio
        if (!_digProgression.TryGetValue(tileDef.ID, out var nextTileId))
        {
            if (tileDef.ID == "FloorDirtDigged_3")
                _popup.PopupEntity("Este solo já foi totalmente escavado!", ent, user);
            return; // Não é um tile cavável ou já está no último estágio
        }

        Log.Debug("DIGGING AFTER INTERACT 5 ANTES DOAFTER");

        // Inicia o DoAfter
        var delay = TimeSpan.FromSeconds(comp.DigTime);
        var netGridUid = GetNetEntity(gridUid.Value);
        var doAfterArgs = new DoAfterArgs(EntityManager, user, delay, new DigDoAfterEvent(netGridUid, snapPos, nextTileId), ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true
        };

        Log.Debug("DIGGING AFTER INTERACT 6 ANTES DO AFTER");

        if (_doAfter.TryStartDoAfter(doAfterArgs))
        {
            _popup.PopupEntity("Você começa a cavar o solo.", ent, user);
            args.Handled = true;
        }
    }

    private void OnDoAfter(Entity<DiggingComponent> ent, ref DigDoAfterEvent args)
    {
        Log.Debug("DIGGING OnDoAfter 1");
        if (args.Cancelled || args.Handled)
            return;

        var gridUid = GetEntity(args.GridUid);
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var snapPos = args.SnapPos;
        var nextTileId = args.NextTileId;

        // Verifica se o tile ainda é válido
        var tileRef = _map.GetTileRef(gridUid, grid, snapPos);
        var tileDef = (ContentTileDefinition)_tileManager[tileRef.Tile.TypeId];
        var expectedTileId = _digProgression.FirstOrDefault(x => x.Value == nextTileId).Key;

        if (tileDef.ID != expectedTileId)
        {
            _popup.PopupEntity("O tile mudou desde que você começou a cavar!", ent, args.User);
            return;
        }

        // Atualiza o tile para o próximo estágio
        var nextTile = _tileManager[nextTileId];
        _map.SetTile(gridUid, grid, snapPos, new Tile(nextTile.TileId));

        // Spawna a entidade MaterialDirt
        var coordinates = grid.GridTileToLocal(snapPos);
        Spawn("MaterialDirt1", coordinates);

        _popup.PopupEntity("Você termina de cavar o solo.", ent, args.User);
        args.Handled = true;
    }
}