import os
import shutil


def ensure_mag_0_exists(root_dir):
    for current_dir, subdirs, files in os.walk(root_dir):
        # Skip if mag-0.png already exists
        if "mag-0.png" in files:
            continue

        parent_dir = os.path.dirname(current_dir)
        source_path = os.path.join(parent_dir, "mag-0.png")
        target_path = os.path.join(current_dir, "mag-0.png")

        if os.path.isfile(source_path):
            print(f"Copying {source_path} → {target_path}")
            shutil.copyfile(source_path, target_path)
        else:
            print(f"No mag-0.png found to copy for: {current_dir}")


# Change this to your actual root folder
root_folder = os.path.abspath(os.path.dirname(__file__))
ensure_mag_0_exists(root_folder)
