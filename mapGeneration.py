import numpy as np
import yaml
from perlin_noise import PerlinNoise
import base64

# -----------------------------------------------------------------------------
# Tilemap – Apenas os 4 itens válidos (usados apenas na grid)
# -----------------------------------------------------------------------------
# Esses são os tiles que aparecem na grid.
TILEMAP = {
    1: "Space",
    2: "FloorAstroGrass",
    0: "FloorDirt",
    3: "FloorGrassDark"
}
# Mapeamento inverso: converte nome para índice
TILEMAP_REVERSE = {v: k for k, v in TILEMAP.items()}

# -----------------------------------------------------------------------------
# Geração do heightmap e mapeamento para tiles (para a grid)
# -----------------------------------------------------------------------------
def generate_heightmap(width, height, octaves=6, seed=None):
    """Gera um heightmap usando Perlin Noise."""
    noise = PerlinNoise(octaves=octaves, seed=seed)
    heightmap = np.zeros((height, width))
    for y in range(height):
        for x in range(width):
            heightmap[y, x] = noise([x / width, y / height])
    return heightmap

def map_noise_to_tiles(heightmap, thresholds=(0.3, 0.0)):
    """
    Mapeia os valores do heightmap para índices do tilemap.
    Se o valor < dirt_threshold (0.0): FloorDirt (0);
    se estiver entre 0.0 e grass_threshold (0.3): FloorAstroGrass (2);
    se >= grass_threshold: FloorGrassDark (3).
    """
    grass_threshold, dirt_threshold = thresholds
    h, w = heightmap.shape
    tile_map = np.zeros((h, w), dtype=np.int32)
    for y in range(h):
        for x in range(w):
            value = heightmap[y, x]
            if value < dirt_threshold:
                tile_map[y, x] = TILEMAP_REVERSE["FloorDirt"]
            elif value < grass_threshold:
                tile_map[y, x] = TILEMAP_REVERSE["FloorAstroGrass"]
            else:
                tile_map[y, x] = TILEMAP_REVERSE["FloorGrassDark"]
    return tile_map

def add_border(tile_map, border_value=None):
    """
    Adiciona bordas ao mapa.
    Por padrão, utiliza FloorDirt (índice 0) para a borda.
    """
    if border_value is None:
        border_value = TILEMAP_REVERSE["FloorDirt"]
    bordered = np.pad(tile_map, pad_width=1, mode='constant', constant_values=border_value)
    return bordered.astype(np.int32)

def encode_tiles(tile_map):
    """
    Codifica a matriz de tiles em base64.
    Converte a matriz para 32-bit little-endian usando '<i4'.
    """
    arr = np.ascontiguousarray(tile_map.astype('<i4'))
    return base64.b64encode(arr.tobytes()).decode('utf-8')

# -----------------------------------------------------------------------------
# Geração dinâmica das entidades sobre o grid
# -----------------------------------------------------------------------------
# UID global para atribuição sequencial (começa em 3, pois 1 e 2 são reservados)
global_uid = 3
def next_uid():
    global global_uid
    uid = global_uid
    global_uid += 1
    return uid

def generate_main_entities(tile_map):
    """
    Gera o grupo principal (proto: "") contendo:
      - UID 1: Map Entity (componentes básicos)
      - UID 2: grid (com MapGrid e demais componentes)
    """
    main = {
        "proto": "",
        "entities": [
            {
                "uid": 1,
                "components": [
                    {"type": "MetaData", "name": "Map Entity"},
                    {"type": "Transform"},
                    {"type": "Map", "mapPaused": True},
                    {"type": "PhysicsMap"},
                    {"type": "GridTree"},
                    {"type": "MovedGrids"},
                    {"type": "Broadphase"},
                    {"type": "OccluderTree"}
                ]
            },
            {
                "uid": 2,
                "components": [
                    {"type": "MetaData", "name": "grid"},
                    {"type": "Transform", "parent": 1, "pos": "3.46875,4.03125"},
                    {"type": "MapGrid",
                     "chunks": {
                         "0,0": {
                             "ind": "0,0",
                             "tiles": encode_tiles(tile_map),
                             "version": 6
                         }
                     }
                    },
                    {"type": "Broadphase"},
                    {"type": "Physics",
                     "angularDamping": 0.05,
                     "bodyStatus": "InAir",
                     "bodyType": "Dynamic",
                     "fixedRotation": True,
                     "linearDamping": 0.05
                    },
                    {"type": "Fixtures", "fixtures": {}},
                    {"type": "OccluderTree"},
                    {"type": "SpreaderGrid"},
                    {"type": "Shuttle"},
                    {"type": "GridPathfinding"},
                    {"type": "Gravity",
                     "gravityShakeSound": { "!type:SoundPathSpecifier": {"path": "/Audio/Effects/alert.ogg"} }
                    },
                    {"type": "BecomesStation", "id": "Nomads"},
                    {"type": "DecalGrid", "chunkCollection": {"version": 2, "nodes": []}},
                    {"type": "GridAtmosphere", "version": 2, "data": {"chunkSize": 4}},
                    {"type": "GasTileOverlay"},
                    {"type": "RadiationGridResistance"}
                ]
            }
        ]
    }
    return main

def generate_dynamic_entities(tile_map):
    """
    Gera entidades dinamicamente com base em cada tile do mapa (com borda).
    Neste exemplo, a lógica é:
      - Se o tile estiver na borda (primeira/última linha ou coluna): usa proto "WallPlastitaniumIndestructible".
      - No interior:
           * Se o tile for FloorAstroGrass (índice 2): usa proto "FloorWaterEntity".
           * Se o tile for FloorGrassDark (índice 3): usa proto "FloraRockSolid".
      - Além disso, se o tile estiver na penúltima linha (parte inferior interior),
        também gera uma entidade no grupo "WallRock".
    """
    groups = {
        "FloorWaterEntity": [],
        "FloraRockSolid": [],
        "WallPlastitaniumIndestructible": [],
        "WallRock": []
    }
    h, w = tile_map.shape
    center_x = w // 2
    center_y = h // 2
    for y in range(h):
        for x in range(w):
            pos_x = x - center_x - 0.5
            pos_y = -(y - center_y) + 0.5
            tile_val = tile_map[y, x]
            # Se estiver na borda:
            if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                groups["WallPlastitaniumIndestructible"].append({
                    "uid": next_uid(),
                    "components": [
                        {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                    ]
                })
            else:
                # Interior: gera entidades conforme o valor do tile
                if tile_val == TILEMAP_REVERSE["FloorAstroGrass"]:
                    groups["FloorWaterEntity"].append({
                        "uid": next_uid(),
                        "components": [
                            {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                        ]
                    })
                elif tile_val == TILEMAP_REVERSE["FloorGrassDark"]:
                    groups["FloraRockSolid"].append({
                        "uid": next_uid(),
                        "components": [
                            {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                        ]
                    })
                # Se o tile estiver na penúltima linha (parte inferior interior)
                if y == h - 2:
                    groups["WallRock"].append({
                        "uid": next_uid(),
                        "components": [
                            {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                        ]
                    })
    dynamic_groups = []
    for proto, ents in groups.items():
        dynamic_groups.append({"proto": proto, "entities": ents})
    return dynamic_groups

def generate_all_entities(tile_map):
    """
    Combina o grupo principal (UID 1 e 2) com os grupos dinâmicos gerados.
    """
    entities = []
    entities.append(generate_main_entities(tile_map))
    entities.extend(generate_dynamic_entities(tile_map))
    return entities

# -----------------------------------------------------------------------------
# Salvar YAML
# -----------------------------------------------------------------------------
def save_map_to_yaml(tile_map, filename="output.yml"):
    all_entities = generate_all_entities(tile_map)
    count = sum(len(group.get("entities", [])) for group in all_entities)
    map_data = {
        "meta": {
            "format": 7,
            "category": "Map",
            "engineVersion": "249.0.0",
            "forkId": "",
            "forkVersion": "",
            "time": "03/23/2025 18:21:23",
            "entityCount": count
        },
        "maps": [1],
        "grids": [2],
        "orphans": [],
        "nullspace": [],
        "tilemap": TILEMAP,
        "entities": all_entities
    }
    with open(filename, 'w') as outfile:
        yaml.dump(map_data, outfile, default_flow_style=False, sort_keys=False)

# -----------------------------------------------------------------------------
# Geração do mapa
# -----------------------------------------------------------------------------
width = 52   # 50 + 2 (bordas)
height = 52  # 50 + 2 (bordas)
# Gera o heightmap para a área interna (sem borda)
heightmap = generate_heightmap(width - 2, height - 2)
# Mapeia os valores para os tiles (usando thresholds: <0.0 -> FloorDirt, [0.0,0.3) -> FloorAstroGrass, >=0.3 -> FloorGrassDark)
tile_map = map_noise_to_tiles(heightmap, thresholds=(0.3, 0.0))
# Adiciona a borda – usando FloorDirt (índice 0) para a borda
bordered_tile_map = add_border(tile_map, border_value=TILEMAP_REVERSE["FloorDirt"])

# Salva o YAML
save_map_to_yaml(bordered_tile_map)
print("Mapa gerado e salvo em output.yml com sucesso!")
