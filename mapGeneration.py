import numpy as np
import yaml
import base64
import struct
import random
from pyfastnoiselite.pyfastnoiselite import FastNoiseLite, NoiseType, FractalType, CellularReturnType, CellularDistanceFunction
import time
import os

# -----------------------------------------------------------------------------
# Tilemap
# -----------------------------------------------------------------------------
TILEMAP = {
    0: "Space",
    1: "FloorDirt",
    2: "FloorAstroGrass",
    3: "FloorGrassDark"
}
TILEMAP_REVERSE = {v: k for k, v in TILEMAP.items()}

# -----------------------------------------------------------------------------
# Funções Auxiliares
# -----------------------------------------------------------------------------
def round_to_chunk(number, chunk):
    """Arredonda um número para o múltiplo inferior mais próximo do tamanho do chunk."""
    return number - (number % chunk)

def add_border(tile_map, border_value):
    """Adiciona uma borda ao tile_map com o valor especificado."""
    bordered = np.pad(tile_map, pad_width=1, mode='constant', constant_values=border_value)
    return bordered.astype(np.int32)

def encode_tiles(tile_map):
    """Codifica os tiles em formato base64 para o YAML."""
    tile_bytes = bytearray()
    for y in range(tile_map.shape[0]):
        for x in range(tile_map.shape[1]):
            tile_id = tile_map[y, x]
            flags = 0
            variant = 0
            tile_bytes.extend(struct.pack("<I", tile_id)) # 4 bytes tile_id
            tile_bytes.append(flags)                      # 1 byte flag
            tile_bytes.append(variant)                    # 1 byte variant
    return base64.b64encode(tile_bytes).decode('utf-8')

# -----------------------------------------------------------------------------
# Geração do Tile Map com Múltiplas Camadas
# -----------------------------------------------------------------------------
def generate_tile_map(width, height, biome_tile_layers, seed_base=None):
    """Gera o tile_map com base nas camadas de tiles definidas em biome_tile_layers."""
    tile_map = np.full((height, width), TILEMAP_REVERSE["FloorDirt"], dtype=np.int32)
    for layer in biome_tile_layers:
        noise = FastNoiseLite()
        noise.noise_type = layer["noise_type"]
        noise.fractal_octaves = layer["octaves"]
        noise.frequency = layer["frequency"]
        noise.fractal_type = layer["fractal_type"]
        
        if "cellular_distance_function" in layer:
            noise.cellular_distance_function = layer["cellular_distance_function"]
        if "cellular_return_type" in layer:
            noise.cellular_return_type = layer["cellular_return_type"]
        if "cellular_jitter" in layer:
            noise.cellular_jitter = layer["cellular_jitter"]
        if "fractal_lacunarity" in layer:
            noise.fractal_lacunarity = layer["fractal_lacunarity"]
        
        if seed_base is not None:
            noise.seed = (seed_base + hash(layer["tile_type"])) % (2**31)
        count = 0
        for y in range(height):
            for x in range(width):
                noise_value = noise.get_noise(x, y)
                noise_value = (noise_value + 1) / 2
                if noise_value > layer["threshold"]:
                    if layer.get("overwrite", True) or tile_map[y, x] == TILEMAP_REVERSE["Space"]:
                        tile_map[y, x] = TILEMAP_REVERSE[layer["tile_type"]]
                        count += 1
        print(f"Camada {layer['tile_type']}: {count} tiles colocados")
    return tile_map

# -----------------------------------------------------------------------------
# Geração de Entidades
# -----------------------------------------------------------------------------
global_uid = 3
def next_uid():
    """Gera um UID único para cada entidade."""
    global global_uid
    uid = global_uid
    global_uid += 1
    return uid

def generate_dynamic_entities(tile_map, biome_entity_layers, seed_base=None):
    """Gera entidades dinâmicas com base nas camadas de entidades, respeitando prioridades."""
    groups = {}
    entity_count = {}  # Count entities by proto
    h, w = tile_map.shape
    occupied_positions = set()  # Set to trace occupied positions

    # Order layers by priority. Highest first
    sorted_layers = sorted(biome_entity_layers, key=lambda layer: layer.get("priority", 0), reverse=True)

    for layer in sorted_layers:
        # Get entity_protos list
        entity_protos = layer["entity_protos"]
        if isinstance(entity_protos, str):  # If its a string, turns it into a list
            entity_protos = [entity_protos]

        # Set layer noise
        noise = FastNoiseLite()
        noise.noise_type = layer["noise_type"]
        noise.fractal_octaves = layer["octaves"]
        noise.frequency = layer["frequency"]
        noise.fractal_type = layer["fractal_type"]

        if "cellular_distance_function" in layer:
            noise.cellular_distance_function = layer["cellular_distance_function"]
        if "cellular_return_type" in layer:
            noise.cellular_return_type = layer["cellular_return_type"]
        if "cellular_jitter" in layer:
            noise.cellular_jitter = layer["cellular_jitter"]
        if "fractal_lacunarity" in layer:
            noise.fractal_lacunarity = layer["fractal_lacunarity"]

        if seed_base is not None:
            noise.seed = (seed_base + hash(tuple(entity_protos))) % (2**31)

        for y in range(h):
            for x in range(w):
                if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                    continue
                if (x, y) in occupied_positions:
                    continue
                tile_val = tile_map[y, x]
                noise_value = noise.get_noise(x, y)
                noise_value = (noise_value + 1) / 2  # Normalizar para [0, 1]
                if noise_value > layer["threshold"] and layer["tile_condition"](tile_val):
                    # Chooses randomly a proto
                    proto = random.choice(entity_protos)
                    if proto not in groups:
                        groups[proto] = []
                    groups[proto].append({
                        "uid": next_uid(),
                        "components": [
                            {"type": "Transform", "parent": 2, "pos": f"{x},{y}"}
                        ]
                    })
                    occupied_positions.add((x, y))
                    # Counts entities by proto
                    entity_count[proto] = entity_count.get(proto, 0) + 1

    # Surrounding undestructible walls
    groups["WallPlastitaniumIndestructible"] = []
    for y in range(h):
        for x in range(w):
            if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                groups["WallPlastitaniumIndestructible"].append({
                    "uid": next_uid(),
                    "components": [
                        {"type": "Transform", "parent": 2, "pos": f"{x},{y}"}
                    ]
                })
                # Count undestructible walls
                entity_count["WallPlastitaniumIndestructible"] = entity_count.get("WallPlastitaniumIndestructible", 0) + 1

    dynamic_groups = [{"proto": proto, "entities": ents} for proto, ents in groups.items()]

    # Print generated protos
    for proto, count in entity_count.items():
        print(f"Generated {count} amount of {proto}")

    return dynamic_groups

# Definir uniqueMixes para atmosfera
unique_mixes = [
    {
        "volume": 2500,
        "immutable": True,
        "temperature": 293.15,
        "moles": [21.82478, 82.10312, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    },
    {
        "volume": 2500,
        "temperature": 293.15,
        "moles": [21.824879, 82.10312, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    }
]

def generate_atmosphere_tiles(width, height, chunk_size):
    """Gera os tiles de atmosfera com base no tamanho do mapa."""
    max_x = (width + chunk_size - 1) // chunk_size - 1
    max_y = (height + chunk_size - 1) // chunk_size - 1
    tiles = {}
    for y in range(-1, max_y + 1):
        for x in range(-1, max_x + 1):
            if x == -1 or x == max_x or y == -1 or y == max_y:
                tiles[f"{x},{y}"] = {0: 65535}
            else:
                tiles[f"{x},{y}"] = {1: 65535}
    return tiles

def generate_main_entities(tile_map, chunk_size=16):
    """Gera as entidades principais, incluindo os chunks do mapa e a atmosfera."""
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
    
    atmosphere_chunk_size = 4
    atmosphere_tiles = generate_atmosphere_tiles(w, h, atmosphere_chunk_size)
    
    main = {
        "proto": "",
        "entities": [
            {
                "uid": 1,
                "components": [
                    {"type": "MetaData", "name": "Map Entity"},
                    {"type": "Transform"},
                    {"type": "LightCycle"},
                    {"type": "MapLight", "ambientLightColor": "#D8B059FF"},
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
                    {"type": "SunShadow"},
                    {"type": "SunShadowCycle"},
                    {"type": "GridPathfinding"},
                    {"type": "Gravity",
                     "gravityShakeSound": { "!type:SoundPathSpecifier": {"path": "/Audio/Effects/alert.ogg"} },
                     "inherent": True,
                     "enabled": True
                    },
                    {"type": "BecomesStation", "id": "Nomads"},
                    {"type": "DecalGrid", "chunkCollection": {"version": 2, "nodes": []}},
                    {
                        "type": "GridAtmosphere",
                        "version": 2,
                        "data": {
                            "tiles": atmosphere_tiles,
                            "uniqueMixes": unique_mixes,
                            "chunkSize": atmosphere_chunk_size
                        }
                    },
                    {"type": "GasTileOverlay"},
                    {"type": "RadiationGridResistance"}
                ]
            }
        ]
    }
    return main

def generate_all_entities(tile_map, chunk_size=16, biome_entity_layers=None, seed_base=None):
    """Combina entidades principais e dinâmicas."""
    entities = []
    dynamic_groups = generate_dynamic_entities(tile_map, biome_entity_layers, seed_base)
    entities.append(generate_main_entities(tile_map, chunk_size))
    entities.extend(dynamic_groups)
    spawn_points = generate_spawn_points(tile_map)
    entities.append(spawn_points)
    return entities

# -----------------------------------------------------------------------------
# Salvar YAML
# -----------------------------------------------------------------------------
def represent_sound_path_specifier(dumper, data):
    """Representação personalizada para SoundPathSpecifier no YAML."""
    for key, value in data.items():
        if isinstance(key, str) and key.startswith("!type:"):
            tag = key
            if isinstance(value, dict) and "path" in value:
                return dumper.represent_mapping(tag, value)
    return dumper.represent_dict(data)

def save_map_to_yaml(tile_map, biome_entity_layers, output_dir, filename="output.yml", chunk_size=16, seed_base=None):
    """Salva o mapa gerado em um arquivo YAML no diretório especificado."""
    all_entities = generate_all_entities(tile_map, chunk_size, biome_entity_layers, seed_base)
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
    output_path = os.path.join(output_dir, filename)
    with open(output_path, 'w') as outfile:
        yaml.dump(map_data, outfile, default_flow_style=False, sort_keys=False)

# -----------------------------------------------------------------------------
# Geração de Spawn Points
# -----------------------------------------------------------------------------
def generate_spawn_points(tile_map, num_points=2):
    """Gera entidades SpawnPointNomads no centro do mapa."""
    h, w = tile_map.shape
    center_x = w // 2
    center_y = h // 2
    spawn_points = []
    for i in range(num_points):
        pos_x = center_x - 2.5
        pos_y = center_y - 0.5 - i
        spawn_points.append({
            "uid": next_uid(),
            "components": [
                {"type": "Transform", "parent": 2, "pos": f"{pos_x},{pos_y}"}
            ]
        })
    return {"proto": "SpawnPointNomads", "entities": spawn_points}

# -----------------------------------------------------------------------------
# Configuração do Mapa (MAP_CONFIG)
# -----------------------------------------------------------------------------
MAP_CONFIG = [
    {
        "type": "BiomeTileLayer",
        "tile_type": "FloorDirt",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 2,
        "frequency": 0.01,
        "fractal_type": FractalType.FractalType_None,
        "threshold": -1.0,
        "overwrite": True
    },
    {
        "type": "BiomeTileLayer",
        "tile_type": "FloorAstroGrass",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 3,
        "frequency": 0.02,
        "fractal_type": FractalType.FractalType_None,
        "threshold": 0.4,
        "overwrite": True
    },
    {
        "type": "BiomeEntityLayer",
        "entity_protos": "FloraRockSolid",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 4,
        "frequency": 0.5,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.65,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorGrassDark"],
        "priority": 1
    },
    { # Rocks
        "type": "BiomeEntityLayer",
        "entity_protos": "WallRock",
        "noise_type": NoiseType.NoiseType_Cellular,
        "cellular_distance_function": CellularDistanceFunction.CellularDistanceFunction_Hybrid,
        "cellular_return_type": CellularReturnType.CellularReturnType_CellValue,
        "octaves": 2,
        "cellular_jitter": 1.070,
        "frequency": 0.015,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.30,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorDirt"],
        "priority": 1
    },
    { # Rivers
        "type": "BiomeEntityLayer",
        "entity_protos": "FloorWaterEntity",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "fractal_lacunarity": 1.50,
        "frequency": 0.003,
        "fractal_type": FractalType.FractalType_Ridged,
        "threshold": 0.95,
        "tile_condition": lambda tile: True,
        "priority": 10 
    },
    { # Trees
        "type": "BiomeEntityLayer",
        "entity_protos": ["FloraTree", "FloraTreeLarge"],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.5,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorAstroGrass"],
        "priority": 0
    },
    ####### PREDATORS
    { # Wolves
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobGreyWolf",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9965,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorAstroGrass"],
        "priority": 11
    },
    { # Bears
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobBear",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 1,
        "frequency": 0.300,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.958,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorAstroGrass"],
        "priority": 11
    },
    { # Sabertooth
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobSabertooth",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 1,
        "frequency": 0.300,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96882,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorAstroGrass"],
        "priority": 11
    },
]

# -----------------------------------------------------------------------------
# Execução
# -----------------------------------------------------------------------------
start_time = time.time()

seed_base = random.randint(0, 1000000)
print(f"Seed base gerado: {seed_base}")

width, height = 250, 250
chunk_size = 16

biome_tile_layers = [layer for layer in MAP_CONFIG if layer["type"] == "BiomeTileLayer"]
biome_entity_layers = [layer for layer in MAP_CONFIG if layer["type"] == "BiomeEntityLayer"]

script_dir = os.path.dirname(os.path.abspath(__file__))
output_dir = os.path.join(script_dir, "Resources", "Maps", "civ")
os.makedirs(output_dir, exist_ok=True)

tile_map = generate_tile_map(width, height, biome_tile_layers, seed_base)
bordered_tile_map = add_border(tile_map, border_value=TILEMAP_REVERSE["FloorDirt"])

save_map_to_yaml(bordered_tile_map, biome_entity_layers, output_dir, filename="nomads_classic.yml", chunk_size=chunk_size, seed_base=seed_base)

end_time = time.time()
total_time = end_time - start_time
print(f"Mapa gerado e salvo em {total_time:.2f} segundos!")