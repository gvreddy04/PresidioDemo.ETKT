"""The ETKT feed through the real workers, the generic decoder and local Presidio (V-07, V-08, V-05 through the harness)."""
import struct
import unittest

from decoded_text_support import EXPECTED_REJECTIONS, PROJECT, baseline, in_scope, payload, verify_published
from pipeline_harness import run
from test_feed_logs import blocks, leaked_values

SAMPLES = PROJECT / "docs" / "sample-docs"


def counted(text):
    """An XDR counted string: big-endian length, ASCII text, zero padding."""
    data = text.encode("ascii")
    return struct.pack(">I", len(data)) + data + b"\0" * (-len(data) % 4)



def xdr_segments(data):
    """Return (start, end) byte ranges of the XDR counted strings of 4+ printable ASCII characters, with their counts."""
    offset, segments = 0, []
    while offset + 4 <= len(data):
        length = int.from_bytes(data[offset:offset + 4], "big")
        text = offset + 4
        if 4 <= length <= len(data) - text and all(32 <= b < 127 for b in data[text:text + length]):
            end = text + length + (-length % 4)
            segments.append((offset, end))
            offset = end
        else:
            offset += 4
    return segments


class DecodedTextFeedTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        inputs = {f"{p.parent.name}/{p.name}": p.read_bytes() for p in sorted(SAMPLES.rglob("*.txt"))}
        etkt = (SAMPLES / "ETKT" / "ETKT.txt").read_bytes()
        # A template change: the producer appends a new 4+ character string after the last ETKT field.
        cls.added_text = "NEWFIELD9"
        inputs["ETKT/template-added-field.bin"] = payload(etkt) + counted(cls.added_text)
        # Template changes (AC-007): ETKT string 2 (the terminal address) removed, and strings 0 and 1 (the entry date and
        # time) swapped; the ticket numbers, strings 4 and 5, and the ticket status record, string 6, stay in place.
        etkt_payload = payload(etkt)
        segments = xdr_segments(etkt_payload)
        assert len(segments) == 7, "ETKT.txt layout changed; choose new template-change fields"
        start, end = segments[2]
        inputs["ETKT/template-removed-field.bin"] = etkt_payload[:start] + etkt_payload[end:]
        (a0, a1), (b0, b1) = segments[0], segments[1]
        inputs["ETKT/template-reordered-fields.bin"] = (etkt_payload[:a0] + etkt_payload[b0:b1] + etkt_payload[a1:b0]
                                                        + etkt_payload[a0:a1] + etkt_payload[b1:])
        cls.inputs = inputs
        cls.harness = run(inputs, extra_args=["--detection-log", "on"], expect=2 if EXPECTED_REJECTIONS else 0, prefix="decoded-text-")
        cls.addClassCleanup(cls.harness.cleanup)

    def test_all_six_etkt_samples_published_and_negative_samples_rejected_with_their_reasons(self):
        items = [i for i in self.harness.report["Items"] if "template-" not in i["Source"]]
        published = [i for i in items if i["Status"] == "Published"]
        rejected = {i["Source"].replace("\\", "/").rsplit("/", 1)[-1]: i["Reason"] for i in items if i["Status"] == "Rejected"}
        self.assertEqual(len(published), 6)
        self.assertEqual(set(rejected), set(EXPECTED_REJECTIONS))
        for name, reason in EXPECTED_REJECTIONS.items():
            self.assertIn(reason, rejected[name])

    def test_published_samples_keep_framing_and_protect_every_baseline_name(self):
        # Every baseline value in scope is protected or approved; the rest is out of scope.
        document, exceptions = baseline()
        totals = {"protected": 0, "approved_exceptions": 0, "out_of_scope": 0}
        for key, original in self.inputs.items():
            feed, name = key.split("/")
            if "template-" in name or name in EXPECTED_REJECTIONS:
                continue
            counts = verify_published(feed, name, original, self.harness.published(feed, name), document, exceptions)
            for k in totals:
                totals[k] += counts[k]
        self.assertEqual(totals["protected"] + totals["approved_exceptions"], sum(1 for e in document["protected"] if in_scope(e)))
        self.assertEqual(totals["out_of_scope"], sum(1 for e in document["protected"] if not in_scope(e)))
        self.assertEqual(totals["approved_exceptions"], len(exceptions))

    def test_template_changes_still_publish_and_leave_an_undetected_new_value_unchanged(self):
        # WR-014: a string in which nothing is detected is published unchanged (and listed for review in the log).
        etkt = payload(self.harness.published("ETKT", "template-added-field.bin"))
        self.assertTrue(etkt.endswith(counted(self.added_text)), "the added, undetected ETKT string is published unchanged")

    def test_removed_and_reordered_template_fields_still_publish_protected(self):
        # Names-only: the ETKT baseline holds no PERSON value (0 names in scope) and the samples hold no SURNAME/GIVEN
        # name, so each changed template is published, and published exactly as received.
        document, _ = baseline()
        self.assertEqual([e for e in document["protected"] if in_scope(e)], [])
        for name in ("template-removed-field.bin", "template-reordered-fields.bin"):
            self.assertEqual(self.harness.published("ETKT", name), self.inputs["ETKT/" + name], f"{name}: not published as received")

    def test_detection_log_through_the_harness_is_value_free(self):
        found = blocks(self.harness.console)
        # The six samples and the three template variants hold 75 decoded strings.
        self.assertGreaterEqual(sum(len(b["entries"]) for b in found), 75)
        leaked = leaked_values("\n".join("\n".join(b["lines"][1:]) for b in found))
        self.assertEqual(leaked, [])


if __name__ == "__main__":
    unittest.main()
