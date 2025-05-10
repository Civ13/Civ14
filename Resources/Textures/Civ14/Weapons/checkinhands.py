import os
import argparse


def find_folders_missing_file(root_folder, filename_to_check):
    """
    Recursively searches a root folder and lists subfolders
    that do not contain a specific file.

    Args:
        root_folder (str): The path to the root folder to start searching from.
        filename_to_check (str): The name of the file to check for.

    Returns:
        list: A list of full absolute paths to folders missing the specified file.
    """
    missing_folders = []
    # Ensure the root folder path is absolute and exists
    abs_root_folder = os.path.abspath(root_folder)
    if not os.path.isdir(abs_root_folder):
        print(f"Error: Folder not found - {abs_root_folder}")
        return missing_folders

    # Walk through the directory tree
    # os.walk yields dirpath, dirnames, filenames for each directory
    for dirpath, dirnames, filenames in os.walk(abs_root_folder):
        # Construct the full path to the file we're looking for in the current directory
        file_path_to_check = os.path.join(dirpath, filename_to_check)

        # Check if the file exists at that path
        # os.path.exists returns False if the path doesn't exist or isn't a file
        if not os.path.exists(file_path_to_check) or os.path.isdir(file_path_to_check):
            # Add the *absolute* path of the folder to our list if the file is missing
            missing_folders.append(os.path.abspath(dirpath))

    return missing_folders


if __name__ == "__main__":
    # Set up argument parsing to make the script flexible
    parser = argparse.ArgumentParser(
        description="Find folders within a directory tree that are missing a specific file (default: 'inhand-left.png')."
    )
    # Optional argument for the folder to search. Defaults to the current directory ('.')
    parser.add_argument(
        "root_folder",
        nargs="?",  # Makes the argument optional
        default=".",  # Default value if no argument is provided
        help="The root folder to start the search from. Defaults to the current directory.",
    )
    # Optional argument to specify a different filename to check for
    parser.add_argument(
        "--filename",
        default="inhand-left.png",
        help="The name of the file to check for (default: inhand-left.png).",
    )

    # Parse the command-line arguments
    args = parser.parse_args()

    target_folder = args.root_folder
    file_to_find = args.filename
    abs_target_folder = os.path.abspath(target_folder)  # Get absolute path for clarity

    print(f"\nSearching for folders missing '{file_to_find}'")
    print(f"Starting in: '{abs_target_folder}'\n")

    # Run the search function
    folders_without_file = find_folders_missing_file(target_folder, file_to_find)

    # Print the results
    if folders_without_file:
        print("----------------------------------------")
        print("Folders missing the file:")
        print("----------------------------------------")
        for folder in folders_without_file:
            print(folder)
        print("----------------------------------------")

    else:
        print(
            f"✅ All folders checked within '{abs_target_folder}' contain the file '{file_to_find}'."
        )

    print("\nSearch complete.")
