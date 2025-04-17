import os
from pathlib import Path
from pydub import AudioSegment
from pydub.exceptions import CouldntDecodeError

# --- Configuration ---
input_directory = Path(r"D:\GitHub\Civ14\Resources\Audio\Weapons\Guns\Fire")
# Create a subdirectory for the mono output to avoid overwriting originals initially
output_directory = input_directory / "mono_output"
# --- End Configuration ---

# Ensure the output directory exists
output_directory.mkdir(parents=True, exist_ok=True)

print(f"Input directory: {input_directory}")
print(f"Output directory: {output_directory}")
print("-" * 30)

processed_count = 0
converted_count = 0
skipped_count = 0
error_count = 0

# Iterate through all files in the input directory
for filepath in input_directory.glob("*.ogg"):
    processed_count += 1
    print(f"Processing: {filepath.name}...")

    output_filepath = output_directory / filepath.name

    try:
        # Load the audio file
        audio = AudioSegment.from_ogg(filepath)

        # Check if it's already mono
        if audio.channels == 1:
            print("  Already mono. Skipping conversion (copying file).")
            # Optionally copy the original if you want all files in the output dir
            # shutil.copy2(filepath, output_filepath)
            # For this script, we'll just skip creating a duplicate if it's already mono
            skipped_count += 1
            continue  # Move to the next file

        # Convert to mono
        print(f"  Converting to mono (Channels: {audio.channels} -> 1)...")
        mono_audio = audio.set_channels(1)

        # Export the mono audio back to Ogg format
        # You can add parameters like bitrate if needed: e.g., bitrate="64k"
        mono_audio.export(output_filepath, format="ogg")
        converted_count += 1
        print(f"  Saved mono version to: {output_filepath.name}")

    except CouldntDecodeError:
        print(
            f"  ERROR: Could not decode file. Is FFmpeg installed and in PATH? Skipping."
        )
        error_count += 1
    except Exception as e:
        print(f"  ERROR: An unexpected error occurred: {e}")
        error_count += 1

print("-" * 30)
print("Batch conversion finished.")
print(f"Total files processed: {processed_count}")
print(f"Files converted:     {converted_count}")
print(f"Files skipped (already mono): {skipped_count}")
print(f"Files with errors:   {error_count}")
if error_count > 0:
    print("\nWARNING: Some files encountered errors. Please check the log above.")
if converted_count > 0 or skipped_count > 0:
    print(f"\nMono files (or skipped originals) are in: {output_directory}")
