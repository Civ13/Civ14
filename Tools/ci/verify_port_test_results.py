"""Require the complete focused NUnit result set; preserve source-bound evidence."""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


PREFIX = "Content.Tests.Server.Civ14."
EXPECTED = {
    (PREFIX + "GatheringCompletionTest", "ExhaustedResourceDoesNotYieldAnotherItem"): 4,
    (PREFIX + "GatheringCompletionTest", "CancelledOrHandledCompletionDoesNotConsumeStock"): 4,
    (PREFIX + "CompostingInteractionTest", "RejectedInputIsNotConsumed"): 2,
    (PREFIX + "CompostingInteractionTest", "AllowedInputStillStartsComposting"): 1,
    (PREFIX + "CompostingInteractionTest", "NoAvailableHandDoesNotSpawnOrConsumeOutput"): 2,
    (PREFIX + "CompostingInteractionTest", "FailedPickupDeletesOnlyNewOutputAndPreservesStock"): 1,
    (PREFIX + "CompostingInteractionTest", "EmptyOrHandledInteractionDoesNothing"): 2,
}


def verify(root):
    namespace = root.tag.split("}")[0] + "}" if root.tag.startswith("{") else ""
    if root.tag != namespace + "TestRun":
        raise AssertionError("Not a TRX TestRun")
    counters = root.find(f"{namespace}ResultSummary/{namespace}Counters")
    if counters is None:
        raise AssertionError("Missing TRX counters")
    for key, expected in (("total", 16), ("executed", 16), ("passed", 16),
                          ("failed", 0), ("notExecuted", 0)):
        if int(counters.get(key, "-1")) != expected:
            raise AssertionError(f"TRX {key}: expected {expected}, got {counters.get(key)}")

    definitions = {}
    for unit in root.findall(f"{namespace}TestDefinitions/{namespace}UnitTest"):
        identifier = unit.get("id")
        method = unit.find(namespace + "TestMethod")
        if not identifier or identifier in definitions or method is None:
            raise AssertionError("Missing/duplicate test definition")
        cls = method.get("className", "").split(",", 1)[0]
        name = method.get("name", "").split("(", 1)[0].rsplit(".", 1)[-1]
        definitions[identifier] = (cls, name)

    results = root.findall(f"{namespace}Results/{namespace}UnitTestResult")
    if len(results) != 16:
        raise AssertionError(f"Expected 16 result rows, got {len(results)}")
    observed = Counter()
    result_ids = set()
    for result in results:
        identifier = result.get("testId")
        if identifier in result_ids or identifier not in definitions:
            raise AssertionError("Duplicate or undefined result testId")
        result_ids.add(identifier)
        if result.get("outcome") != "Passed":
            raise AssertionError(f"Non-passing case: {result.get('testName')}: {result.get('outcome')}")
        observed[definitions[identifier]] += 1
    if observed != Counter(EXPECTED):
        raise AssertionError(f"Wrong fixture/method case inventory: {dict(observed)}")
    if result_ids != set(definitions):
        raise AssertionError("Unexecuted test definitions")
    return [{"class": cls, "method": method, "passed": count}
            for (cls, method), count in sorted(observed.items())]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", type=Path, required=True)
    args = parser.parse_args()
    evidence = {"repository": os.environ.get("GITHUB_REPOSITORY"),
                "head_sha": os.environ.get("EVIDENCE_HEAD"),
                "base_sha": os.environ.get("EVIDENCE_BASE"),
                "workflow": os.environ.get("GITHUB_WORKFLOW"),
                "run_id": os.environ.get("GITHUB_RUN_ID"),
                "run_attempt": os.environ.get("GITHUB_RUN_ATTEMPT"), "status": "failed"}
    exit_code = 1
    try:
        for key in ("head_sha", "base_sha"):
            if not re.fullmatch(r"[0-9a-f]{40}", evidence[key] or ""):
                raise AssertionError(f"Invalid evidence {key}")
        for key in ("run_id", "run_attempt"):
            if not re.fullmatch(r"[1-9][0-9]*", evidence[key] or ""):
                raise AssertionError(f"Invalid evidence {key}")
        checkout = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True, timeout=10).strip()
        evidence["checkout_sha"] = checkout
        if checkout != os.environ.get("GITHUB_SHA"):
            raise AssertionError("Checkout differs from this workflow's source SHA")
        data = args.trx.read_bytes()
        evidence["trx_sha256"] = hashlib.sha256(data).hexdigest()
        evidence["cases"] = verify(ET.fromstring(data))
        evidence.update(status="passed", passed=16, skipped=0, failed=0)
        exit_code = 0
    except Exception as exc:
        evidence["error"] = str(exc)
        print(f"Port test evidence failed: {exc}", file=sys.stderr)
    finally:
        args.trx.parent.mkdir(parents=True, exist_ok=True)
        (args.trx.parent / "verification.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(evidence, indent=2))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
