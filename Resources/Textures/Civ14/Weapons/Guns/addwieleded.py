import os
import json
import shutil


def process_folder(folder):
    left_src = os.path.join(folder, "inhand-left.png")
    right_src = os.path.join(folder, "inhand-right.png")
    left_dst = os.path.join(folder, "wielded-inhand-left.png")
    right_dst = os.path.join(folder, "wielded-inhand-right.png")
    meta_path = os.path.join(folder, "meta.json")

    if os.path.isfile(left_src) and os.path.isfile(right_src):
        # Copy files
        shutil.copyfile(left_src, left_dst)
        shutil.copyfile(right_src, right_dst)
        print(f"Copied to wielded-inhand-* in {folder}")

        # Modify meta.json
        if os.path.isfile(meta_path):
            with open(meta_path, "r", encoding="utf-8") as f:
                meta = json.load(f)

            states = meta.get("states", [])
            state_names = [s["name"] for s in states]

            changed = False
            for name in ["wielded-inhand-left", "wielded-inhand-right"]:
                if name not in state_names:
                    states.append({"name": name, "directions": 1})
                    changed = True

            if changed:
                meta["states"] = states
                with open(meta_path, "w", encoding="utf-8") as f:
                    json.dump(meta, f, indent=4)
                print(f"Updated meta.json in {folder}")
        else:
            print(f"meta.json not found in {folder}, skipping JSON update.")
    else:
        print(f"Missing inhand-left or inhand-right in {folder}, skipping.")


def main():
    root = os.path.abspath(os.path.dirname(__file__))

    for current_dir, subdirs, files in os.walk(root):
        process_folder(current_dir)


if __name__ == "__main__":
    main()
