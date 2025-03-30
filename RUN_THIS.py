#!/usr/bin/env python3

# Import future so people on py2 still get the clear error that they need to upgrade.
from __future__ import print_function
import sys
import subprocess
import importlib.util

version = sys.version_info
if version.major < 3 or (version.major == 3 and version.minor < 5):
    print("ERROR: You need at least Python 3.5 to build SS14.")
    sys.exit(1)

# These libraries are used into mapGeneration.py
required_libraries = ['numpy', 'pyyaml', 'pyfastnoiselite']

# Checks if we have all libs needed
def is_library_installed(library):
    return importlib.util.find_spec(library) is not None

# Install needed libs that are missing
for library in required_libraries:
    if not is_library_installed(library):
        print(f"Installing lib {library}...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", library])

subprocess.run([sys.executable, "git_helper.py"], cwd="BuildChecker")
