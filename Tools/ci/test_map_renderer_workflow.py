"""Offline contracts for the map-renderer setup and required result gate.

Run with: python3 -m unittest discover -s Tools/ci -p 'test_map_renderer_workflow.py' -v
Uses only the standard library and Bash (as provided by the Ubuntu runner).
These focused text checks are not a general YAML or GitHub expression parser.
"""

import itertools
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/build-map-renderer.yml"


def block(text, heading, indent):
    """Read an indentation-delimited block from this workflow's fixed layout."""
    match = re.search(rf"(?m)^{' ' * indent}{re.escape(heading)}\n", text)
    if match is None:
        raise AssertionError(f"Missing workflow block: {heading}")
    lines = []
    for line in text[match.end():].splitlines():
        if line.strip() and len(line) - len(line.lstrip()) <= indent:
            break
        lines.append(line)
    return "\n".join(lines)


class MapRendererWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.build = block(cls.workflow, "build:", 2)
        cls.aggregate = block(cls.workflow, "ci-success:", 2)

    def step(self, name):
        return block(self.build, f"- name: {name}", 6)

    def test_nested_submodules_follow_dependency_selection(self):
        setup = self.step("Setup Submodule")
        self.assertIn("git submodule update --init\n", setup + "\n")
        self.assertNotIn("--recursive", setup)
        self.assertLess(self.build.index("name: Setup Submodule"),
                        self.build.index("name: Pull engine updates"))
        self.assertLess(self.build.index("name: Pull engine updates"),
                        self.build.index("name: Update Engine Submodules"))
        self.assertIn("space-wizards/submodule-dependency@v0.1.5", self.build)
        self.assertIn("cd RobustToolbox/\n          git submodule update --init --recursive",
                      self.step("Update Engine Submodules"))

    def test_restore_scope_and_both_configurations_are_preserved(self):
        self.assertIn("run: dotnet restore Content.MapRenderer/Content.MapRenderer.csproj",
                      self.step("Install dependencies"))
        self.assertIn("--configuration Release --no-restore /p:WarningsAsErrors=nullable /m",
                      self.step("Build Project"))
        runtime = self.step("Run Map Renderer")
        self.assertIn("--configuration Debug --no-restore -- Dev", runtime)
        self.assertNotIn("--no-build", runtime)
        sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]
        self.assertEqual(sdk, {"version": "9.0.100", "rollForward": "latestFeature"})
        self.assertIn("dotnet-version: 9.0.x", self.step("Setup .NET Core"))

    def test_setup_and_runtime_have_finite_limits(self):
        self.assertIn("timeout-minutes: 30", self.build)
        for name in ("Setup Submodule", "Pull engine updates", "Update Engine Submodules",
                     "Setup .NET Core", "Install dependencies"):
            self.assertIn("timeout-minutes: 5", self.step(name), name)
        self.assertIn("timeout-minutes: 15", self.step("Run Map Renderer"))

    def test_gate_wiring_and_draft_coverage(self):
        self.assertIn("if: github.actor != 'PJBot'", self.build)
        self.assertNotIn("pull_request.draft", self.build)
        self.assertIn("if: always() && github.actor != 'PJBot'", self.aggregate)
        self.assertIn("- workflow-contract", self.aggregate)
        self.assertIn("- build", self.aggregate)
        self.assertIn("CONTRACT_RESULT: ${{ needs.workflow-contract.result }}", self.aggregate)
        self.assertIn("BUILD_RESULT: ${{ needs.build.result }}", self.aggregate)
        self.assertIn("name: Build & Test Debug", self.aggregate)
        self.assertNotIn("continue-on-error", self.workflow)

    def test_gate_rejects_failed_cancelled_skipped_missing_and_unknown_results(self):
        # Execute the actual gate, including Bash's fail-fast behavior, rather
        # than duplicating its decision in a test helper.
        script = block(self.aggregate, "run: |", 8)
        script = "\n".join(line[10:] for line in script.splitlines())
        self.assertTrue(script.strip())
        bash = shutil.which("bash")
        if os.name == "nt":
            bash = str(Path(os.environ["ProgramFiles"]) / "Git/bin/bash.exe")
        self.assertTrue(bash, "Bash is required to exercise the actual runner gate")
        for contract, build in itertools.product(
                ("success", "failure", "cancelled", "skipped", "", "unknown"), repeat=2):
            with self.subTest(contract=contract, build=build):
                env = dict(os.environ, CONTRACT_RESULT=contract, BUILD_RESULT=build)
                result = subprocess.run([bash, "--noprofile", "--norc", "-eo", "pipefail", "-c", script],
                                        env=env, capture_output=True, timeout=10)
                self.assertEqual(result.returncode == 0, contract == build == "success")


if __name__ == "__main__":
    unittest.main()
