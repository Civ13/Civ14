"""Offline negative controls for the focused NUnit evidence verifier."""
import unittest
import xml.etree.ElementTree as ET

from verify_port_test_results import EXPECTED, verify


def fixture():
    root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    definitions = ET.SubElement(root, "TestDefinitions")
    results = ET.SubElement(root, "Results")
    summary = ET.SubElement(root, "ResultSummary")
    ET.SubElement(summary, "Counters", total="16", executed="16", passed="16", failed="0", notExecuted="0")
    index = 0
    for (cls, method), count in EXPECTED.items():
        for case in range(count):
            identifier = str(index)
            unit = ET.SubElement(definitions, "UnitTest", id=identifier)
            ET.SubElement(unit, "TestMethod", className=cls, name=f"{method}({case})")
            ET.SubElement(results, "UnitTestResult", testId=identifier,
                          testName=f"{method}({case})", outcome="Passed")
            index += 1
    # Match the namespace expansion produced by parsing an actual TRX file.
    return ET.fromstring(ET.tostring(root))


class PortResultOracleTests(unittest.TestCase):
    def setUp(self):
        self.root = fixture()
        self.ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"

    def test_complete_sixteen_case_inventory_passes(self):
        self.assertEqual(sum(row["passed"] for row in verify(self.root)), 16)

    def test_zero_or_undiscovered_tests_fail(self):
        self.root.find(f"{self.ns}ResultSummary/{self.ns}Counters").set("total", "0")
        with self.assertRaises(AssertionError):
            verify(self.root)

    def test_skipped_case_fails_even_if_counters_claim_success(self):
        self.root.find(f"{self.ns}Results/{self.ns}UnitTestResult").set("outcome", "NotExecuted")
        with self.assertRaises(AssertionError):
            verify(self.root)

    def test_failed_case_fails(self):
        self.root.find(f"{self.ns}Results/{self.ns}UnitTestResult").set("outcome", "Failed")
        with self.assertRaises(AssertionError):
            verify(self.root)

    def test_missing_case_fails(self):
        results = self.root.find(self.ns + "Results")
        results.remove(results[0])
        with self.assertRaises(AssertionError):
            verify(self.root)

    def test_duplicate_result_fails(self):
        results = self.root.find(self.ns + "Results")
        results[0].set("testId", results[1].get("testId"))
        with self.assertRaises(AssertionError):
            verify(self.root)

    def test_wrong_fixture_or_method_fails(self):
        self.root.find(f"{self.ns}TestDefinitions/{self.ns}UnitTest/{self.ns}TestMethod").set("name", "Unexpected")
        with self.assertRaises(AssertionError):
            verify(self.root)

    def test_missing_definition_fails(self):
        definitions = self.root.find(self.ns + "TestDefinitions")
        definitions.remove(definitions[0])
        with self.assertRaises(AssertionError):
            verify(self.root)


if __name__ == "__main__":
    unittest.main()
