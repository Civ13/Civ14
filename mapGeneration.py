import numpy as np
import yaml
import base64
import struct
import random
import sys

from pyfastnoiselite.pyfastnoiselite import (
    FastNoiseLite,
    NoiseType,
    FractalType,
    CellularReturnType,
    CellularDistanceFunction,
    DomainWarpType,
)
import time
import os

if len(sys.argv) == 1:
    mapWidth = 300
    mapHeight = 300
    print(f"No custom mapsize specified, using defaults: {mapWidth}w x {mapHeight}h")
else:
    mapWidth = int(sys.argv[1])
    mapHeight = int(sys.argv[2])
    print(f"Using specified mapsize: {mapWidth}w x {mapHeight}h")

# -----------------------------------------------------------------------------
# Tilemap
# -----------------------------------------------------------------------------
TILEMAP = {
    0: "Space",
    1: "FloorDirt",
    2: "FloorPlanetGrass",
    3: "FloorGrassDark",
    4: "FloorSand",
    5: "FloorDirtRock",
}
TILEMAP_REVERSE = {v: k for k, v in TILEMAP.items()}


# -----------------------------------------------------------------------------
# Helper Functions
# -----------------------------------------------------------------------------
def round_to_chunk(number, chunk):
    """Rounds a number to the inferior multiplier of a chunk."""
    return number - (number % chunk)


def add_border(tile_map, border_value):
    """Adds a border to tile_map with the specified value."""
    bordered = np.pad(
        tile_map, pad_width=1, mode="constant", constant_values=border_value
    )
    return bordered.astype(np.int32)


def encode_tiles(tile_map):
    """Codifies the tiles in base64 for the YAML."""
    tile_bytes = bytearray()
    for y in range(tile_map.shape[0]):  # u
        for x in range(tile_map.shape[1]):
            tile_id = tile_map[y, x]
            flags = 0
            variant = 0
            tile_bytes.extend(struct.pack("<I", tile_id))  # 4 bytes tile_id
            tile_bytes.append(flags)  # 1 byte flag
            tile_bytes.append(variant)  # 1 byte variant
    return base64.b64encode(tile_bytes).decode("utf-8")


# -----------------------------------------------------------------------------
# Generating a TileMap with multiple layers
# -----------------------------------------------------------------------------
def generate_tile_map(width, height, biome_tile_layers, seed_base=None):
    """Generates the tile_map based on the layers defined in biome_tile_layers."""
    tile_map = np.full((height, width), TILEMAP_REVERSE["FloorDirt"], dtype=np.int32)

    # Orders the layers by priority (largest to smallest)
    sorted_layers = sorted(
        biome_tile_layers, key=lambda layer: layer.get("priority", 1)
    )

    for layer in sorted_layers:
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
            seed_key = layer.get("seed_key", layer["tile_type"])
            noise.seed = (seed_base + hash(seed_key)) % (2**31)

        # Modulation config, if present
        mod_noise = None
        if "modulation" in layer:
            mod_config = layer["modulation"]
            mod_noise = FastNoiseLite()
            mod_noise.noise_type = mod_config.get(
                "noise_type", NoiseType.NoiseType_OpenSimplex2
            )
            if "cellular_distance_function" in mod_config:
                mod_noise.cellular_distance_function = mod_config[
                    "cellular_distance_function"
                ]
            if "cellular_return_type" in mod_config:
                mod_noise.cellular_return_type = mod_config["cellular_return_type"]
            if "cellular_jitter" in mod_config:
                mod_noise.cellular_jitter = mod_config["cellular_jitter"]
            if "fractal_lacunarity" in mod_config:
                mod_noise.fractal_lacunarity = mod_config["fractal_lacunarity"]
            mod_noise.frequency = mod_config.get("frequency", 0.010)
            mod_noise.seed = (seed_base + hash(seed_key + "_mod")) % (2**31)
            threshold_min = mod_config.get("threshold_min", 0.4)
            threshold_max = mod_config.get("threshold_max", 0.6)

        count = 0
        dont_overwrite = [TILEMAP_REVERSE[t] for t in layer.get("dontOverwrite", [])]

        for y in range(height):
            for x in range(width):
                noise_value = noise.get_noise(x, y)
                noise_value = (noise_value + 1) / 2  # Normalise into [0, 1]

                place_tile = False
                if mod_noise:
                    mod_value = mod_noise.get_noise(x, y)
                    mod_value = (mod_value + 1) / 2
                    if noise_value > layer["threshold"]:
                        if mod_value > threshold_max:
                            place_tile = True
                        elif mod_value > threshold_min:
                            probability = (mod_value - threshold_min) / (
                                threshold_max - threshold_min
                            )
                            place_tile = random.random() < probability
                else:
                    if noise_value > layer["threshold"]:
                        place_tile = True

                if place_tile:
                    current_tile = tile_map[y, x]
                    if current_tile not in dont_overwrite:
                        if (
                            layer.get("overwrite", True)
                            or current_tile == TILEMAP_REVERSE["Space"]
                        ):
                            tile_map[y, x] = TILEMAP_REVERSE[layer["tile_type"]]
                            count += 1

        print(f"Layer {layer['tile_type']}: {count} tiles placed")
    return tile_map


# -----------------------------------------------------------------------------
# Entity generation
# -----------------------------------------------------------------------------
global_uid = 3


def next_uid():
    """Generates an unique UID for each entity."""
    global global_uid
    uid = global_uid
    global_uid += 1
    return uid


def generate_dynamic_entities(tile_map, biome_entity_layers, seed_base=None):
    """Generates dynamic entities based on the entity layers, respecting priorities."""
    groups = {}
    entity_count = {}  # Count entities by proto
    h, w = tile_map.shape
    occupied_positions = set()  # Set to trace occupied positions

    # Order layers by priority. Highest first
    sorted_layers = sorted(
        biome_entity_layers, key=lambda layer: layer.get("priority", 0), reverse=True
    )

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
            # Uses "seed_key" if available, if not uses a hash based on entity_protos
            seed_key = layer.get("seed_key", tuple(entity_protos))
            noise.seed = (seed_base + hash(seed_key)) % (2**31)

        for y in range(h):
            for x in range(w):
                if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                    continue
                if (x, y) in occupied_positions:
                    continue
                tile_val = tile_map[y, x]
                noise_value = noise.get_noise(x, y)
                noise_value = (noise_value + 1) / 2  # Normalise into [0, 1]
                if noise_value > layer["threshold"] and layer["tile_condition"](
                    tile_val
                ):
                    # Chooses randomly a proto
                    proto = random.choice(entity_protos)
                    if proto not in groups:
                        groups[proto] = []
                    groups[proto].append(
                        {
                            "uid": next_uid(),
                            "components": [
                                {"type": "Transform", "parent": 2, "pos": f"{x},{y}"}
                            ],
                        }
                    )
                    occupied_positions.add((x, y))
                    # Counts entities by proto
                    entity_count[proto] = entity_count.get(proto, 0) + 1

    # Surrounding undestructible walls
    groups["WallRockIndestructible"] = []
    for y in range(h):
        for x in range(w):
            if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                groups["WallRockIndestructible"].append(
                    {
                        "uid": next_uid(),
                        "components": [
                            {"type": "Transform", "parent": 2, "pos": f"{x},{y}"}
                        ],
                    }
                )
                # Count undestructible walls
                entity_count["WallRockIndestructible"] = (
                    entity_count.get("WallRockIndestructible", 0) + 1
                )

    dynamic_groups = [
        {"proto": proto, "entities": ents} for proto, ents in groups.items()
    ]

    # Print generated protos
    for proto, count in entity_count.items():
        print(f"Generated {count} amount of {proto}")

    return dynamic_groups


def generate_decals(tile_map, biome_decal_layers, seed_base=None, chunk_size=16):
    """Generate decals using biome_decal_layers and log the count of each decal type."""
    decals_by_id = {}
    h, w = tile_map.shape
    occupied_tiles = set()
    decal_count = {}

    for layer in biome_decal_layers:
        noise = FastNoiseLite()
        noise.noise_type = layer["noise_type"]
        noise.fractal_octaves = layer["octaves"]
        noise.frequency = layer["frequency"]
        noise.fractal_type = layer["fractal_type"]

        if seed_base is not None:
            seed_key = layer.get(
                "seed_key",
                (
                    tuple(layer["decal_id"])
                    if isinstance(layer["decal_id"], list)
                    else layer["decal_id"]
                ),
            )
            noise.seed = (seed_base + hash(seed_key)) % (2**31)

        decal_ids = (
            layer["decal_id"]
            if isinstance(layer["decal_id"], list)
            else [layer["decal_id"]]
        )

        for y in range(h):
            for x in range(w):
                if x == 0 or x == w - 1 or y == 0 or y == h - 1:
                    continue
                if (x, y) in occupied_tiles:
                    continue
                tile_val = tile_map[y, x]
                noise_value = noise.get_noise(x, y)
                noise_value = (noise_value + 1) / 2
                if noise_value > layer["threshold"] and layer["tile_condition"](
                    tile_val
                ):
                    chosen_decal_id = random.choice(decal_ids)
                    if chosen_decal_id not in decals_by_id:
                        decals_by_id[chosen_decal_id] = []
                    # Small random offset for decals
                    offset_x = (
                        noise.get_noise(x + 1000, y + 1000) + 1
                    ) / 4 - 0.25  # Between -0.25 and 0.25
                    offset_y = (
                        noise.get_noise(x + 2000, y + 2000) + 1
                    ) / 4 - 0.25  # Between -0.25 and 0.25
                    pos_x = x + offset_x
                    pos_y = y + offset_y
                    pos_str = f"{pos_x:.7f},{pos_y:.7f}"
                    decals_by_id[chosen_decal_id].append(
                        {"color": layer.get("color", "#FFFFFFFF"), "position": pos_str}
                    )
                    occupied_tiles.add((x, y))
                    decal_count[chosen_decal_id] = (
                        decal_count.get(chosen_decal_id, 0) + 1
                    )

    return decals_by_id


# Defines uniqueMixes for the atmosphere
unique_mixes = [
    {
        "volume": 2500,
        "immutable": True,
        "temperature": 278.15,
        "moles": [21.82478, 82.10312, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    },
    {
        "volume": 2500,
        "temperature": 278.15,
        "moles": [21.824879, 82.10312, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    },
]


def generate_atmosphere_tiles(width, height, chunk_size):
    """Generates the atmos tiles based on the map size."""
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


def generate_main_entities(tile_map, chunk_size=16, decals_by_id=None):
    """Generates entities, decals and atmos."""
    if decals_by_id is None:
        decals_by_id = {}

    h, w = tile_map.shape
    chunks = {}
    for cy in range(0, h, chunk_size):
        for cx in range(0, w, chunk_size):
            chunk_key = f"{cx//chunk_size},{cy//chunk_size}"
            chunk_tiles = tile_map[cy : cy + chunk_size, cx : cx + chunk_size]
            if chunk_tiles.shape[0] < chunk_size or chunk_tiles.shape[1] < chunk_size:
                full_chunk = np.zeros((chunk_size, chunk_size), dtype=np.int32)
                full_chunk[: chunk_tiles.shape[0], : chunk_tiles.shape[1]] = chunk_tiles
                chunk_tiles = full_chunk
            chunks[chunk_key] = {
                "ind": f"{cx//chunk_size},{cy//chunk_size}",
                "tiles": encode_tiles(chunk_tiles),
                "version": 6,
            }

    atmosphere_chunk_size = 4
    atmosphere_tiles = generate_atmosphere_tiles(w, h, atmosphere_chunk_size)

    # Decals generation
    decal_nodes = []
    global_index = 0
    for decal_id, decals in decals_by_id.items():
        if decals:
            node_decals = {}
            for decal in decals:
                node_decals[str(global_index)] = decal["position"]
                global_index += 1
            node = {
                "node": {"color": decals[0]["color"], "id": decal_id},
                "decals": node_decals,
            }
            decal_nodes.append(node)

    print(f"Total decal nodes generated: {len(decal_nodes)}")
    print(f"Total decals: {global_index}")

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
                    {"type": "OccluderTree"},
                ],
            },
            {
                "uid": 2,
                "components": [
                    {"type": "MetaData", "name": "grid"},
                    {"type": "Transform", "parent": 1, "pos": "0,0"},
                    {"type": "MapGrid", "chunks": chunks},
                    {"type": "Broadphase"},
                    {
                        "type": "Physics",
                        "angularDamping": 0.05,
                        "bodyStatus": "InAir",
                        "bodyType": "Dynamic",
                        "fixedRotation": True,
                        "linearDamping": 0.05,
                    },
                    {"type": "Fixtures", "fixtures": {}},
                    {"type": "OccluderTree"},
                    {"type": "SpreaderGrid"},
                    {"type": "Shuttle"},
                    {"type": "SunShadow"},
                    {"type": "SunShadowCycle"},
                    {"type": "GridPathfinding"},
                    {
                        "type": "Gravity",
                        "gravityShakeSound": {
                            "!type:SoundPathSpecifier": {
                                "path": "/Audio/Effects/alert.ogg"
                            }
                        },
                        "inherent": True,
                        "enabled": True,
                    },
                    {"type": "BecomesStation", "id": "Nomads"},
                    {"type": "Weather"},
                    {
                        "type": "WeatherNomads",
                        "enabledWeathers": [
                            "Rain",
                            "Storm",
                            "SnowfallLight",
                            "SnowfallMedium",
                            "SnowfallHeavy",
                        ],
                        "minSeasonMinutes": 10,
                        "maxSeasonMinutes": 30,
                    },
                    {
                        "type": "DecalGrid",
                        "chunkCollection": {"version": 2, "nodes": decal_nodes},
                    },
                    {
                        "type": "GridAtmosphere",
                        "version": 2,
                        "data": {
                            "tiles": atmosphere_tiles,
                            "uniqueMixes": unique_mixes,
                            "chunkSize": atmosphere_chunk_size,
                        },
                    },
                    {"type": "GasTileOverlay"},
                    {"type": "RadiationGridResistance"},
                ],
            },
        ],
    }
    return main


def generate_all_entities(tile_map, chunk_size=16, biome_layers=None, seed_base=None):
    """Combines tiles, entities and decals."""
    entities = []
    if biome_layers is None:
        biome_layers = []
    biome_tile_layers = [
        layer for layer in biome_layers if layer["type"] == "BiomeTileLayer"
    ]
    biome_entity_layers = [
        layer for layer in biome_layers if layer["type"] == "BiomeEntityLayer"
    ]
    biome_decal_layers = [
        layer for layer in biome_layers if layer["type"] == "BiomeDecalLayer"
    ]

    dynamic_groups = generate_dynamic_entities(tile_map, biome_entity_layers, seed_base)
    decals_by_chunk = generate_decals(
        tile_map, biome_decal_layers, seed_base, chunk_size
    )
    main_entities = generate_main_entities(tile_map, chunk_size, decals_by_chunk)
    entities.append(main_entities)
    entities.extend(dynamic_groups)
    spawn_points = generate_spawn_points(tile_map)
    entities.extend(spawn_points)
    return entities


# -----------------------------------------------------------------------------
# Save YAML
# -----------------------------------------------------------------------------
def represent_sound_path_specifier(dumper, data):
    """Customised representation for the SoundPathSpecifier in the YAML."""
    for key, value in data.items():
        if isinstance(key, str) and key.startswith("!type:"):
            tag = key
            if isinstance(value, dict) and "path" in value:
                return dumper.represent_mapping(tag, value)
    return dumper.represent_dict(data)


def save_map_to_yaml(
    tile_map,
    biome_layers,
    output_dir,
    filename="output.yml",
    chunk_size=16,
    seed_base=None,
):
    """Saves the generated map in a YAML file in the specified folder."""
    all_entities = generate_all_entities(tile_map, chunk_size, biome_layers, seed_base)
    count = sum(len(group.get("entities", [])) for group in all_entities)
    map_data = {
        "meta": {
            "format": 7,
            "category": "Map",
            "engineVersion": "249.0.0",
            "forkId": "",
            "forkVersion": "",
            "time": "03/23/2025 18:21:23",
            "entityCount": count,
        },
        "maps": [1],
        "grids": [2],
        "orphans": [],
        "nullspace": [],
        "tilemap": TILEMAP,
        "entities": all_entities,
    }
    yaml.add_representer(dict, represent_sound_path_specifier)
    output_path = os.path.join(output_dir, filename)
    with open(output_path, "w") as outfile:
        yaml.dump(map_data, outfile, default_flow_style=False, sort_keys=False)


import numpy as np
from collections import defaultdict


def apply_erosion(tile_map, tile_type, min_neighbors=3):
    h, w = tile_map.shape
    new_map = tile_map.copy()

    for y in range(1, h - 1):
        for x in range(1, w - 1):
            if tile_map[y, x] == tile_type:
                neighbors = 0
                neighbor_types = []
                for dy in [-1, 0, 1]:
                    for dx in [-1, 0, 1]:
                        if dy == 0 and dx == 0:
                            continue
                        neighbor_y = y + dy
                        neighbor_x = x + dx
                        if 0 <= neighbor_y < h and 0 <= neighbor_x < w:
                            nt = tile_map[neighbor_y, neighbor_x]
                            neighbor_types.append(nt)
                            if nt == tile_type:
                                neighbors += 1
                if neighbors < min_neighbors:
                    counts = defaultdict(int)
                    for nt in neighbor_types:
                        counts[nt] += 1
                    if counts:
                        max_count = max(counts.values())
                        candidates = [k for k, v in counts.items() if v == max_count]
                        majority_type = candidates[0]  # Defines majority_type here
                        new_map[y, x] = majority_type
    return new_map


def count_isolated_tiles(tile_map, tile_type, min_neighbors=3):
    h, w = tile_map.shape
    isolated = 0
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            if tile_map[y, x] == tile_type:
                neighbors = sum(
                    1
                    for dy in [-1, 0, 1]
                    for dx in [-1, 0, 1]
                    if not (dy == 0 and dx == 0)
                    and 0 <= y + dy < h
                    and 0 <= x + dx < w
                    and tile_map[y + dy, x + dx] == tile_type
                )
                if neighbors < min_neighbors:
                    isolated += 1
    return isolated


def apply_iterative_erosion(tile_map, tile_type, min_neighbors=3, max_iterations=10):
    """Applies erosion interactively untill there are no more tiles with the declared min neighbors"""
    iteration = 0
    while iteration < max_iterations:
        isolated_before = count_isolated_tiles(tile_map, tile_type, min_neighbors)
        tile_map = apply_erosion(tile_map, tile_type, min_neighbors)
        isolated_after = count_isolated_tiles(tile_map, tile_type, min_neighbors)
        if isolated_after == isolated_before or isolated_after == 0:
            break
        iteration += 1
    return tile_map


# -----------------------------------------------------------------------------
# Spawn Point Generation
# -----------------------------------------------------------------------------
def generate_spawn_points(tile_map, num_points_per_corner=1):
    """Generates 4 SpawnPointNomads and 4 SpawnPointLatejoin, one on each corner, on FloorPlanetGrass."""
    h, w = tile_map.shape
    spawn_positions = set()
    nomads_entities = []
    latejoin_entities = []
    corners = ["top_left", "top_right", "bottom_left", "bottom_right"]
    astro_grass_id = TILEMAP_REVERSE["FloorPlanetGrass"]
    directions = [(-1, 0), (1, 0), (0, -1), (0, 1)]

    for corner in corners:
        found = False
        initial_size = 15  # Initial size to search for positions
        while not found and initial_size <= min(w, h) // 2:
            x_min, x_max, y_min, y_max = get_corner_region(corner, w, h, initial_size)
            candidates = []
            # Searchs for AstroTileGrass in the initial size in the corners
            for y in range(y_min, y_max + 1):
                for x in range(x_min, x_max + 1):
                    if (
                        tile_map[y, x] == astro_grass_id
                        and (x, y) not in spawn_positions
                    ):
                        # Verifies adjacent valid tiles
                        adjacent = []
                        for dx, dy in directions:
                            nx, ny = x + dx, y + dy
                            if (
                                0 <= nx < w
                                and 0 <= ny < h
                                and tile_map[ny, nx] == astro_grass_id
                                and (nx, ny) not in spawn_positions
                            ):
                                adjacent.append((nx, ny))
                        if adjacent:
                            candidates.append((x, y, adjacent))
            if candidates:
                x, y, adjacent = random.choice(candidates)
                adj_x, adj_y = random.choice(adjacent)
                if random.random() < 0.5:
                    nomads_pos = (x, y)
                    latejoin_pos = (adj_x, adj_y)
                else:
                    nomads_pos = (adj_x, adj_y)
                    latejoin_pos = (x, y)
                nomads_entities.append(
                    {
                        "uid": next_uid(),
                        "components": [
                            {
                                "type": "Transform",
                                "parent": 2,
                                "pos": f"{nomads_pos[0]},{nomads_pos[1]}",
                            }
                        ],
                    }
                )
                latejoin_entities.append(
                    {
                        "uid": next_uid(),
                        "components": [
                            {
                                "type": "Transform",
                                "parent": 2,
                                "pos": f"{latejoin_pos[0]},{latejoin_pos[1]}",
                            }
                        ],
                    }
                )
                spawn_positions.add(nomads_pos)
                spawn_positions.add(latejoin_pos)
                found = True
            else:
                initial_size += 1
        if not found:
            print(
                f"Possible to find an available position at the corner for spawn points {corner}"
            )

    print("SpawnPointNomads positions:")
    for ent in nomads_entities:
        pos = ent["components"][0]["pos"]
        print(pos)
    print("SpawnPointLatejoin positions:")
    for ent in latejoin_entities:
        pos = ent["components"][0]["pos"]
        print(pos)

    # Retorna as entidades no formato correto para o YAML
    return [
        {"proto": "SpawnPointNomads", "entities": nomads_entities},
        {"proto": "SpawnPointLatejoin", "entities": latejoin_entities},
    ]


def get_corner_region(corner, w, h, initial_size):
    """Defines a region to search in the map's corners."""
    if corner == "top_left":
        x_min = 1
        x_max = min(initial_size, w - 2)
        y_min = 1
        y_max = min(initial_size, h - 2)
    elif corner == "top_right":
        x_min = max(w - 1 - initial_size, 1)
        x_max = w - 2
        y_min = 1
        y_max = min(initial_size, h - 2)
    elif corner == "bottom_left":
        x_min = 1
        x_max = min(initial_size, w - 2)
        y_min = max(h - 1 - initial_size, 1)
        y_max = h - 2
    elif corner == "bottom_right":
        x_min = max(w - 1 - initial_size, 1)
        x_max = w - 2
        y_min = max(h - 1 - initial_size, 1)
        y_max = h - 2
    else:
        raise ValueError("Invalid corner")
    return x_min, x_max, y_min, y_max


# -----------------------------------------------------------------------------
# Configuração do Mapa (MAP_CONFIG)
# -----------------------------------------------------------------------------
MAP_CONFIG = [
    {  # Rock dirt formations
        "type": "BiomeTileLayer",
        "tile_type": "FloorDirtRock",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 2,
        "frequency": 0.01,
        "fractal_type": FractalType.FractalType_None,
        "threshold": -1.0,
        "overwrite": True,
    },
    {  # Sprinkled dirt around the map
        "type": "BiomeTileLayer",
        "tile_type": "FloorDirt",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 10,
        "frequency": 0.3,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.825,
        "overwrite": True,
        "dontOverwrite": ["FloorSand", "FloorDirtRock"],
        "priority": 10,
    },
    {
        "type": "BiomeTileLayer",
        "tile_type": "FloorPlanetGrass",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 3,
        "frequency": 0.02,
        "fractal_type": FractalType.FractalType_None,
        "threshold": 0.4,
        "overwrite": True,
    },
    {  # Boulders for flints
        "type": "BiomeEntityLayer",
        "entity_protos": "FloraRockSolid",
        "noise_type": NoiseType.NoiseType_OpenSimplex2S,
        "octaves": 6,
        "frequency": 0.3,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.815,
        "tile_condition": lambda tile: tile
        in [
            TILEMAP_REVERSE["FloorPlanetGrass"],
            TILEMAP_REVERSE["FloorDirt"],
            TILEMAP_REVERSE["FloorDirtRock"],
        ],
        "priority": 1,
    },
    {  # Rocks
        "type": "BiomeEntityLayer",
        "entity_protos": "WallRock",
        "noise_type": NoiseType.NoiseType_Cellular,
        "cellular_distance_function": CellularDistanceFunction.CellularDistanceFunction_Hybrid,
        "cellular_return_type": CellularReturnType.CellularReturnType_CellValue,
        "cellular_jitter": 1.070,
        "octaves": 2,
        "frequency": 0.015,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.30,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorDirtRock"],
        "priority": 2,
    },
    {  # Wild crops
        "type": "BiomeEntityLayer",
        "entity_protos": [
            "WildPlantPotato",
            "WildPlantCorn",
            "WildPlantRice",
            "WildPlantWheat",
            "WildPlantHemp",
            "WildPlantPoppy",
            "WildPlantAloe",
            "WildPlantYarrow",
            "WildPlantElderflower",
            "WildPlantMilkThistle",
            "WildPlantComfrey",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2S,
        "octaves": 6,
        "frequency": 0.3,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.84,
        "tile_condition": lambda tile: tile in [TILEMAP_REVERSE["FloorPlanetGrass"]],
        "priority": 1,
    },
    {  # Rivers
        "type": "BiomeEntityLayer",
        "entity_protos": "FloorWaterEntity",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "fractal_lacunarity": 1.50,
        "frequency": 0.003,
        "fractal_type": FractalType.FractalType_Ridged,
        "threshold": 0.95,
        "tile_condition": lambda tile: True,
        "priority": 10,
        "seed_key": "river_noise",
    },
    {  # River sand
        "type": "BiomeTileLayer",
        "tile_type": "FloorSand",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.003,  # Same as the river
        "fractal_type": FractalType.FractalType_Ridged,
        "threshold": 0.935,  # Larger than the river
        "overwrite": True,
        "seed_key": "river_noise",
    },
    {  # Additional River Sand with More Curves
        "type": "BiomeTileLayer",
        "tile_type": "FloorSand",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.003,
        "fractal_type": FractalType.FractalType_Ridged,
        "threshold": 0.92,  # Slightly lower than the original
        "overwrite": True,
        "seed_key": "river_noise",  # Same as the original to follow its path
        "modulation": {
            "noise_type": NoiseType.NoiseType_Perlin,  # Different noise for variation
            "frequency": 0.01,  # Controls the scale of the variation
            "threshold_min": 0.43,  # Lower bound where sand starts appearing
            "threshold_max": 0.55,  # Upper bound for a smooth transition
        },
    },
    {  # Trees
        "type": "BiomeEntityLayer",
        "entity_protos": "TreeTemperate",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.5,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 0,
    },
    ####### PREDATORS
    {  # Wolves
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobGreyWolf",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9981,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 11,
    },
    {  # Bears
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobBear",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 1,
        "frequency": 0.300,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.958,
        "tile_condition": lambda tile: tile
        in [TILEMAP_REVERSE["FloorPlanetGrass"], TILEMAP_REVERSE["FloorDirtRock"]],
        "priority": 1,
    },
    {  # Sabertooth
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobSabertooth",
        "noise_type": NoiseType.NoiseType_Perlin,
        "octaves": 1,
        "frequency": 0.300,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96882,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 11,
    },
    ####### Preys
    {  # Rabbits
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobRabbit",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9989,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 11,
    },
    {  # Chicken
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobChicken",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9989,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 11,
    },
    {  # Deers
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobDeer",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9989,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 11,
    },
    {  # Pigs
        "type": "BiomeEntityLayer",
        "entity_protos": "SpawnMobPig",
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.9992,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "priority": 11,
    },
    # DECALS
    {  # Bush Temperate group 1
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate1",
            "BushTemperate2",
            "BushTemperate3",
            "BushTemperate4",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 2
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate5",
            "BushTemperate6",
            "BushTemperate7",
            "BushTemperate8",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 3
        "type": "BiomeDecalLayer",
        "decal_id": ["BushTemperate9", "BushTemperate10", "BushTemperate11"],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 4
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate12",
            "BushTemperate13",
            "BushTemperate14",
            "BushTemperate15",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 5
        "type": "BiomeDecalLayer",
        "decal_id": ["BushTemperate16", "BushTemperate17", "BushTemperate18"],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 6
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate19",
            "BushTemperate20",
            "BushTemperate21",
            "BushTemperate22",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 7
        "type": "BiomeDecalLayer",
        "decal_id": ["BushTemperate23", "BushTemperate24", "BushTemperate25"],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 8
        "type": "BiomeDecalLayer",
        "decal_id": ["BushTemperate26", "BushTemperate27", "BushTemperate28"],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 9
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate29",
            "BushTemperate30",
            "BushTemperate31",
            "BushTemperate32",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 10
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate33",
            "BushTemperate34",
            "BushTemperate35",
            "BushTemperate36",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
    {  # Bush Temperate group 11 - High grass
        "type": "BiomeDecalLayer",
        "decal_id": [
            "BushTemperate37",
            "BushTemperate38",
            "BushTemperate39",
            "BushTemperate40",
            "BushTemperate41",
            "BushTemperate42",
        ],
        "noise_type": NoiseType.NoiseType_OpenSimplex2,
        "octaves": 1,
        "frequency": 0.1,
        "fractal_type": FractalType.FractalType_FBm,
        "threshold": 0.96,
        "tile_condition": lambda tile: tile == TILEMAP_REVERSE["FloorPlanetGrass"],
        "color": "#FFFFFFFF",
    },
]

# -----------------------------------------------------------------------------
# Execution
# -----------------------------------------------------------------------------
start_time = time.time()

seed_base = random.randint(0, 1000000)
print(f"Generated seed: {seed_base}")

width, height = mapWidth, mapHeight
chunk_size = 16

biome_tile_layers = [layer for layer in MAP_CONFIG if layer["type"] == "BiomeTileLayer"]
biome_entity_layers = [
    layer for layer in MAP_CONFIG if layer["type"] == "BiomeEntityLayer"
]

script_dir = os.path.dirname(os.path.abspath(__file__))
output_dir = os.path.join(script_dir, "Resources", "Maps", "civ")
os.makedirs(output_dir, exist_ok=True)

tile_map = generate_tile_map(width, height, biome_tile_layers, seed_base)

# Applies erosion to lone sand tiles, overwritting it with surrounding tiles
tile_map = apply_iterative_erosion(
    tile_map, TILEMAP_REVERSE["FloorSand"], min_neighbors=1
)

bordered_tile_map = add_border(tile_map, border_value=TILEMAP_REVERSE["FloorDirt"])

save_map_to_yaml(
    bordered_tile_map,
    MAP_CONFIG,
    output_dir,
    filename="nomads_classic.yml",
    chunk_size=chunk_size,
    seed_base=seed_base,
)

end_time = time.time()
total_time = end_time - start_time
print(f"Map generated and saved in {total_time:.2f} seconds!")
