import numpy as np
import yaml
from perlin_noise import PerlinNoise
import base64
import struct

# -----------------------------------------------------------------------------
# Tilemap – Apenas os 4 itens válidos (usados apenas na grid)
# -----------------------------------------------------------------------------
TILEMAP = {
    1: "Space",
    2: "FloorAstroGrass",
    0: "FloorDirt",
    3: "FloorGrassDark"
}
TILEMAP_REVERSE = {v: k for k, v in TILEMAP.items()}

# -----------------------------------------------------------------------------
# Geração do heightmap e mapeamento para tiles (para a grid)
# -----------------------------------------------------------------------------
def generate_heightmap(width, height, octaves=6, seed=None):
    noise = PerlinNoise(octaves=octaves, seed=seed)
    heightmap = np.zeros((height, width))
    for y in range(height):
        for x in range(width):
            heightmap[y, x] = noise([x / width, y / height])
    return heightmap

def map_noise_to_tiles(heightmap, thresholds=(0.3, 0.0)):
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
    if border_value is None:
        border_value = TILEMAP_REVERSE["FloorDirt"]
    bordered = np.pad(tile_map, pad_width=1, mode='constant', constant_values=border_value)
    return bordered.astype(np.int32)

def encode_tiles(tile_map):
    tile_bytes = bytearray()
    for y in range(tile_map.shape[0]):
        for x in range(tile_map.shape[1]):
            tile_id = tile_map[y, x]
            flags = 0
            variant = 0
            tile_bytes.extend(struct.pack("<I", tile_id))  # 4 bytes para tile_id
            tile_bytes.append(flags)                       # 1 byte para flags
            tile_bytes.append(variant)                     # 1 byte para variant
    return base64.b64encode(tile_bytes).decode('utf-8')

# -----------------------------------------------------------------------------
# Geração dinâmica das entidades sobre o grid
# -----------------------------------------------------------------------------
global_uid = 3
def next_uid():
    global global_uid
    uid = global_uid
    global_uid += 1
    return uid

def represent_sound_path_specifier(dumper, data):
    for key, value in data.items():
        if isinstance(key, str) and key.startswith("!type:"):
            tag = key
            if isinstance(value, dict) and "path" in value:
                return dumper.represent_mapping(tag, value)
    # Fallback to default representation if the structure doesn't match
    return dumper.represent_dict(data)

def generate_main_entities(tile_map, chunk_size=16):
    h, w = tile_map.shape
    chunks = {}
    for cy in range(0, h, chunk_size):
        for cx in range(0, w, chunk_size):
            chunk_key = f"{cx//chunk_size},{cy//chunk_size}"
            chunk_tiles = tile_map[cy:cy+chunk_size, cx:cx+chunk_size]
            # Preenche chunks incompletos nas bordas com FloorDirt (0)
            if chunk_tiles.shape[0] < chunk_size or chunk_tiles.shape[1] < chunk_size:
                full_chunk = np.zeros((chunk_size, chunk_size), dtype=np.int32)
                full_chunk[:chunk_tiles.shape[0], :chunk_tiles.shape[1]] = chunk_tiles
                chunk_tiles = full_chunk
            chunks[chunk_key] = {
                "ind": f"{cx//chunk_size},{cy//chunk_size}",
                "tiles": encode_tiles(chunk_tiles),
                "version": 6
            }
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
                    {"type": "MapGrid", "chunks": chunks},
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
    groups = {
        "FloorWaterEntity": [],
        "FloraRockSolid": [],
        "WallPlastitaniumIndestructible": [],
        "WallRock": []
    }
    h, w = tile_map.shape
    for y in range(h):
        for x in range(w):
            pos_x = x  # Usar coordenada X direta
            pos_y = y  # Usar coordenada Y direta
            tile_val = tile_map[y, x]
            if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                groups["WallPlastitaniumIndestructible"].append({
                    "uid": next_uid(),
                    "components": [
                        {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                    ]
                })
            else:
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

def generate_all_entities(tile_map, chunk_size=16):
    entities = []
    entities.append(generate_main_entities(tile_map, chunk_size))
    entities.extend(generate_dynamic_entities(tile_map))
    return entities

# -----------------------------------------------------------------------------
# Salvar YAML
# -----------------------------------------------------------------------------
def save_map_to_yaml(tile_map, filename="output.yml", chunk_size=16):
    all_entities = generate_all_entities(tile_map, chunk_size)
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
    yaml.add_representer(dict, represent_sound_path_specifier) # Register the custom representer
    with open(filename, 'w') as outfile:
        yaml.dump(map_data, outfile, default_flow_style=False, sort_keys=False)

# -----------------------------------------------------------------------------
# Geração do mapa
# -----------------------------------------------------------------------------
width = 100
height = 100
chunk_size = 16
heightmap = generate_heightmap(width - 2, height - 2)
print("Heightmap - Min:", heightmap.min(), "Max:", heightmap.max())

tile_map = map_noise_to_tiles(heightmap, thresholds=(0.3, 0.0))
print("Tiles únicos no mapa:", np.unique(tile_map))

bordered_tile_map = add_border(tile_map, border_value=TILEMAP_REVERSE["FloorDirt"])
print("Tiles únicos no mapa com borda:", np.unique(bordered_tile_map))

save_map_to_yaml(bordered_tile_map, chunk_size=chunk_size)
print("Mapa gerado e salvo em output.yml com sucesso!")