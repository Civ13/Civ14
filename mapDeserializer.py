import base64
import struct
from typing import Dict


class Tile:
    def __init__(self, type_id: int, flags: int = 0, variant: int = 0):
        self.type_id = type_id
        self.flags = flags
        self.variant = variant

    def __repr__(self):
        return f"Tile {self.type_id}, {self.flags}, {self.variant}"

    def __eq__(self, other):
        return (
            self.type_id == other.type_id
            and self.flags == other.flags
            and self.variant == other.variant
        )


class MapChunk:
    def __init__(self, x: int, y: int, chunk_size: int):
        self.x = x
        self.y = y
        self.chunk_size = chunk_size
        self.tiles = [[Tile(0) for _ in range(chunk_size)] for _ in range(chunk_size)]

    def __repr__(self):
        return f"MapChunk ({self.x}, {self.y}) - Size {self.chunk_size}"

    def set_tile(self, x: int, y: int, tile: Tile):
        self.tiles[y][x] = tile

    def get_tile(self, x: int, y: int) -> Tile:
        return self.tiles[y][x]


def deserialize_map_chunk(
    node: Dict[str, any], tile_map: Dict[int, str], chunk_size: int = 16
) -> MapChunk:
    ind = tuple(map(int, node["ind"].split(",")))
    chunk = MapChunk(ind[0], ind[1], chunk_size)
    tile_bytes = base64.b64decode(node["tiles"])

    version = node.get("version", 1)
    idx = 0

    for y in range(chunk_size):
        for x in range(chunk_size):
            if version < 6:
                tile_id = struct.unpack_from("<H", tile_bytes, idx)[0]  # 2 bytes
                idx += 2
            else:
                tile_id = struct.unpack_from("<I", tile_bytes, idx)[0]  # 4 bytes
                idx += 4

            flags = struct.unpack_from("<B", tile_bytes, idx)[0]  # 1 byte
            idx += 1
            variant = struct.unpack_from("<B", tile_bytes, idx)[0]  # 1 byte
            idx += 1

            # Validação com tile_map
            if tile_id not in tile_map:
                raise ValueError(f"tile_id {tile_id} not found in tile_map.")

            tile = Tile(tile_id, flags, variant)
            chunk.set_tile(x, y, tile)

    return chunk


def serialize_map_chunk(chunk: MapChunk, version: int = 6) -> Dict[str, any]:
    root = {
        "ind": f"{chunk.x},{chunk.y}",
        "version": version,
        "tiles": encode_tiles(chunk),
    }
    return root


def encode_tiles(chunk: MapChunk) -> str:
    tile_bytes = bytearray()
    for y in range(chunk.chunk_size):
        for x in range(chunk.chunk_size):
            tile = chunk.get_tile(x, y)
            tile_bytes.extend(struct.pack("<I", tile.type_id))  # 4 bytes para type_id
            tile_bytes.append(tile.flags)  # 1 byte para flags
            tile_bytes.append(tile.variant)  # 1 byte para variant
    return base64.b64encode(tile_bytes).decode("utf-8")


# Dados de exemplo
tile_map = {1: "Space", 2: "FloorGrass", 0: "FloorDirt", 3: "FloorGrassDark"}

chunk_data = {
    "ind": "-1,0",
    "tiles": "AQAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAgAAAAAAAgAAAAAAAgAAAAAAAgAAAAAAAwAAAAAAAwAAAAAAAwAAAAAAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAAAQAAAAAA",
    "version": 6,
}

# Teste
chunk = deserialize_map_chunk(chunk_data, tile_map)
print(chunk)

serialized_chunk = serialize_map_chunk(chunk)
print(
    "Original tiles == Serializado tiles:",
    serialized_chunk["tiles"] == chunk_data["tiles"],
)

# Para inspecionar alguns tiles
for y in range(5):
    for x in range(5):
        tile = chunk.get_tile(x, y)
        print(f"Tile em ({x}, {y}): {tile}")
