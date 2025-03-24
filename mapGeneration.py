import numpy as np
import yaml
import base64
import struct
import random
from pyfastnoiselite.pyfastnoiselite import FastNoiseLite, NoiseType, FractalType

# -----------------------------------------------------------------------------
# Tilemap
# -----------------------------------------------------------------------------
TILEMAP = {
    1: "Space",
    2: "FloorAstroGrass",
    0: "FloorDirt",
    3: "FloorGrassDark"
}
TILEMAP_REVERSE = {v: k for k, v in TILEMAP.items()}

# -----------------------------------------------------------------------------
# Geração do heightmap e mapeamento para tiles
# -----------------------------------------------------------------------------
def round_to_chunk(number, chunk):
    return number - (number % chunk)

def generate_heightmap(width, height, octaves=6, seed=None, chunk=16):
    width = round_to_chunk(width, chunk) - 2
    height = round_to_chunk(height, chunk) - 2
    noise = FastNoiseLite()
    noise.noise_type = NoiseType.NoiseType_OpenSimplex2
    noise.fractal_octaves = octaves
    noise.frequency = 0.02  # Ajuste conforme necessário
    noise.fractal_type = FractalType.FractalType_FBm

    heightmap = np.zeros((height, width))
    for y in range(height):
        for x in range(width):
            heightmap[y, x] = noise.get_noise(x, y)
    return heightmap

def map_noise_to_tiles(heightmap, thresholds=(0.3, 0.0)):
    grass_threshold, dirt_threshold = thresholds
    h, w = heightmap.shape
    tile_map = np.zeros((h, w), dtype=np.int32)
    for y in range(h):
        for x in range(w):
            value = (heightmap[y, x] + 1) / 2  # Normalizar de [-1, 1] para [0, 1]
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
# Geração de entidades
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
    return dumper.represent_dict(data)

def generate_main_entities(tile_map, chunk_size=16):
    h, w = tile_map.shape
    chunks = {}
    for cy in range(0, h, chunk_size):
        for cx in range(0, w, chunk_size):
            chunk_key = f"{cx//chunk_size},{cy//chunk_size}"
            chunk_tiles = tile_map[cy:cy+chunk_size, cx:cx+chunk_size]
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
                    {"type": "Transform", "parent": 1, "pos": "0,0"},
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

def generate_dynamic_entities(tile_map, entity_config, seed_base=None):
    groups = {proto: [] for proto in entity_config.keys()}
    groups["WallPlastitaniumIndestructible"] = []
    h, w = tile_map.shape
    for y in range(h):
        for x in range(w):
            pos_x, pos_y = x, y
            tile_val = tile_map[y, x]
            if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                groups["WallPlastitaniumIndestructible"].append({
                    "uid": next_uid(),
                    "components": [
                        {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                    ]
                })
            else:
                for proto, config in entity_config.items():
                    noise = FastNoiseLite()
                    noise.noise_type = config["noise_type"]
                    noise.fractal_octaves = config["octaves"]
                    noise.frequency = config["frequency"]
                    noise.fractal_type = config["fractal_type"]
                    noise_value = noise.get_noise(x, y)
                    noise_value = (noise_value + 1) / 2
                    if noise_value > config["threshold"] and config["tile_condition"](tile_val):
                        groups[proto].append({
                            "uid": next_uid(),
                            "components": [
                                {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
                            ]
                        })
    dynamic_groups = [{"proto": proto, "entities": ents} for proto, ents in groups.items()]
    return dynamic_groups

def generate_all_entities(tile_map, chunk_size=16, entity_config=None, seed_base=None):
    entities = []
    entities.append(generate_main_entities(tile_map, chunk_size))
    entities.extend(generate_dynamic_entities(tile_map, entity_config, seed_base))
    return entities

# -----------------------------------------------------------------------------
# Salvar YAML
# -----------------------------------------------------------------------------
def save_map_to_yaml(tile_map, filename="output.yml", chunk_size=16, entity_config=None, seed_base=None):
    all_entities = generate_all_entities(tile_map, chunk_size, entity_config, seed_base)
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
    yaml.add_representer(dict, represent_sound_path_specifier)
    with open(filename, 'w') as outfile:
        yaml.dump(map_data, outfile, default_flow_style=False, sort_keys=False)

# -----------------------------------------------------------------------------
# Configuração e execução
# -----------------------------------------------------------------------------
ENTITY_CONFIG = {
    "FloorWaterEntity": {
        "noise_type": NoiseType.NoiseType_OpenSimplex2S,
        "octaves": 3,
        "frequency": 0.01,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.3,
        "tile_condition": lambda tile: True
    },
    "FloraRockSolid": {
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 4,
        "frequency": 0.02,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.65,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorGrassDark"]
    },
    "WallRock": {
        "noise_type": NoiseType.NoiseType_Cellular,
        "octaves": 5,
        "frequency": 0.05,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.30,
        "tile_condition": lambda tile: True
    },
}

seed_base = random.randint(0, 1000000)
print(f"Seed base gerado: {seed_base}")

width, height = 100, 100
chunk_size = 16

heightmap = generate_heightmap(width, height, seed=seed_base)
tile_map = map_noise_to_tiles(heightmap, thresholds=(0.3, 0.0))
bordered_tile_map = add_border(tile_map, border_value=TILEMAP_REVERSE["FloorDirt"])

save_map_to_yaml(bordered_tile_map, chunk_size=chunk_size, entity_config=ENTITY_CONFIG, seed_base=seed_base)
print("Mapa gerado e salvo em output.yml com sucesso!")