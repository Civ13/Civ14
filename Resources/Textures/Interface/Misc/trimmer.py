import os
from PIL import Image
import sys


def trim_png_transparency(directory):
    """
    Opens all PNG files in the specified directory, trims transparent borders,
    and overwrites the original files.

    Args:
        directory (str): The path to the directory containing PNG files.
    """
    if not os.path.isdir(directory):
        print(f"Error: Directory not found: {directory}")
        return

    print(f"Scanning directory: {directory}")
    trimmed_count = 0
    skipped_count = 0
    error_count = 0

    for filename in os.listdir(directory):
        if filename.lower().endswith(".png"):
            file_path = os.path.join(directory, filename)
            try:
                img = Image.open(file_path)

                # Ensure image has an alpha channel
                if img.mode != "RGBA":
                    img = img.convert("RGBA")

                # Get the bounding box of the non-transparent area
                bbox = img.getbbox()

                if bbox:
                    # Crop the image to the contents of the bounding box
                    img_cropped = img.crop(bbox)

                    # Check if cropping actually changed the size
                    if img_cropped.size != img.size:
                        # Save the cropped image, overwriting the original
                        img_cropped.save(file_path)
                        print(f"Trimmed: {filename}")
                        trimmed_count += 1
                    else:
                        print(f"Skipped (no transparency to trim): {filename}")
                        skipped_count += 1
                else:
                    # Handle completely transparent images (optional: delete or skip)
                    print(f"Skipped (image is fully transparent): {filename}")
                    skipped_count += 1

                img.close()  # Close the original image handle

            except Exception as e:
                print(f"Error processing {filename}: {e}")
                error_count += 1

    print("\n--- Processing Summary ---")
    print(f"Trimmed files: {trimmed_count}")
    print(f"Skipped files: {skipped_count}")
    print(f"Errors: {error_count}")
    print("------------------------")


# --- Configuration ---
# IMPORTANT: Replace this with the actual path to your image directory!
image_directory = (
    r"d:\GitHub\Civ14\Resources\Textures\Interface\Misc\civ_hud_squads.rsi"
)
# Example: image_directory = r'd:\GitHub\Civ14\Resources\Textures\Interface\Misc'
# --- End Configuration ---

trim_png_transparency(image_directory)
