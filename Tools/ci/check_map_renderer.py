"""Exercise the actual renderer's failure exit and successful PNG output in CI.

Run only in the disposable CI checkout: the missing-resource probe temporarily
changes the railing sprite references, restoring the original bytes in finally.
No assertion, resource-loading policy, or game configuration is relaxed.
"""

from pathlib import Path
import struct
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[2]
PROTOTYPE = ROOT / "Resources/Prototypes/Civ14/Entities/Structures/Walls/barricades.yml"
MISSING_RSI = "Civ14/Objects/__ci_missing_railing__.rsi"
MAP_ID = "Nomads"  # The current Content.IntegrationTests.PoolManager.TestMap.


def renderer_command(output, *, build):
    command = ["dotnet", "run", "--project", "Content.MapRenderer",
               "--configuration", "Debug", "--no-restore"]
    if not build:
        command.append("--no-build")
    return command + ["--", "--output", str(output), MAP_ID]


def check_missing_resource(returncode, log):
    if returncode == 0:
        raise AssertionError("Missing RSI incorrectly returned success")
    if MISSING_RSI not in log or "Unable to load RSI" not in log:
        raise AssertionError("Renderer failed for a reason other than the injected missing RSI")


def check_success(returncode, output):
    if returncode != 0:
        raise AssertionError(f"Normal render returned {returncode}")
    image = output / MAP_ID / f"{MAP_ID}-0.png"
    data = image.read_bytes()
    if len(data) <= 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise AssertionError(f"Missing or invalid PNG: {image}")
    width, height = struct.unpack(">II", data[16:24])
    if width == 0 or height == 0:
        raise AssertionError(f"Empty PNG: {image}")
    print(f"Verified rendered PNG: {image.name}, {width}x{height}, {len(data)} bytes", flush=True)


def run_renderer(output, *, build):
    command = renderer_command(output, build=build)
    print(f"Running: {' '.join(command)}", flush=True)
    result = subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, timeout=600)
    print(result.stdout, flush=True)
    print(f"Renderer exit code: {result.returncode}", flush=True)
    return result


def main():
    original = PROTOTYPE.read_bytes()
    reference = b"Civ14/Objects/railing.rsi"
    if original.count(reference) != 4:
        raise AssertionError("Expected four authored lowercase railing RSI references")
    with tempfile.TemporaryDirectory(prefix="map-renderer-ci-") as temporary:
        output = Path(temporary)
        try:
            PROTOTYPE.write_bytes(original.replace(reference, MISSING_RSI.encode("ascii")))
            missing = run_renderer(output / "missing", build=True)
        finally:
            PROTOTYPE.write_bytes(original)

        # Run both probes before asserting, so a failed negative control cannot
        # hide the normal render's result. Reuse the Debug build from the probe.
        normal = run_renderer(output / "normal", build=False)
        check_missing_resource(missing.returncode, missing.stdout)
        print("Verified missing-resource failure exit", flush=True)
        check_success(normal.returncode, output / "normal")


if __name__ == "__main__":
    main()
