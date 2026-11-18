# ETKT feed sample review and structure

> **Superseded design.** This review describes the per-feed parsers, field policies and the three XML files (`feed-policies.xml`, `xdr-layouts.xml`, `reference-layouts.xml`) that decoded-text detection replaced. Those files and parsers are removed. Protection now uses the generic decoder and the ETKT recognizer file, which keeps passenger names only (`passengerName`); Presidio's predefined recognizers run too only with `Presidio:DefaultDetection` on, which is off by default. The other fields described below are published as received; see [Decoded-text detection](../README.md#decoded-text-detection). The sample review, the structure findings (such as the Text0132 correction) and the field meanings below stay valid as analysis; the configuration notes refer to the removed design. The protection results they produced are frozen, value-free, in [tests/fixtures/parity-baseline.json](../tests/fixtures/parity-baseline.json).
>
> This worker keeps the ETKT feed only. The review of the other feeds, and the configuration examples of the removed design (which used one of those feeds), are in the repository history at commit `dffbdad`.

All six files in `docs/sample-docs/ETKT/` were reviewed without changing source bytes. Each is an MQ text export: `A` rows carry metadata and `X` rows carry hexadecimal payload bytes. All six are binary payloads, and none is a negative test.

## Binary representation

The examples use big-endian 32-bit words and counted ASCII strings padded to four bytes. A counted string consists of its byte length, that many text bytes and zero padding. An empty string is a length word of zero, with no value or padding. Missing text is therefore represented structurally, not by a special character. Actual `*` characters in populated fields are masked/placeholder content and are not universal missing-field markers.

The initial ETKT word is five. The removed schema checked this observed discriminator. Subsequent words and string counts are parsed separately, and offsets are calculated while reading each message.

A scanner can read some pairs of raw words as a short counted string: for example `00000001 31000000` looks like a length-one string containing `1` with three zero pad bytes (see [Text0132 correction](#text0132-correction)). The generic decoder therefore treats only strings of four or more printable characters as text and keeps shorter spans as raw words (WR-015).

## ETKT

ETKT is decoded by `src/PresidioDemo.Core/Decoder/EtktDecoder.cs` over `XdrFeedDecoder`; the schema-driven module and its policy XML described here were removed with decoded-text detection (2026110501). The analysis is kept for reference.

The six samples begin with `5, 1, 0`. The screenshot union defines error=1, issue=2 and update=3. The removed implementation supported an explicitly observed type-5 profile while leaving its discriminator meaning unconfirmed. All 136 scalar slots align with the screenshot history order. Its encoder rebuilt the entire payload, including empty string slots and four-byte character slots, and a separate Python reader agreed on every value, type, offset and encoded width.

The approved decoded label for `h_aaa_lniata` is **Agent terminal address — provisional mapping**. XDR and policy names followed the history declarations rather than legacy offsets:

| Source declaration | Former name | Current name | Treatment (removed design) |
| --- | --- | --- | --- |
| `h_aaa_lniata` | `pnr` | `agentTerminalAddress` | Keep; serializer guard by structure path |
| `h_aaa_city_code` | `Text0044` | `agentCityCode` | Unclassified-identifier mask (unchanged) |
| `h_aaa_sine_in_nbr` | `Text0072` | `agentSignInNumber` | Unclassified-identifier mask (unchanged) |
| `h_tkt_nbr` | `Text0088` | `ticketNumber` | Ticket-reference mask (was unclassified identifier) |
| `glob_tkt_hist_seq_nbr` | `ticketNumber` | `historySequenceNumber` | Unclassified-identifier mask (was ticket reference) |
| `h_new_*` / `h_prev_*` strings | `Flight1*` / `Flight2*` | `new*` / `previous*` (for example `newBoardPoint`, `previousOperatingFlightNumber`) | Keep |
| `h_new_recloc` / `h_old_recloc` | `UnconfirmedLocator1` / `UnconfirmedLocator2` | `newPnr` / `oldPnr` | Keep; serializer guard |

Both swapped ticket policies masked every character with `*`, so the protected bytes were unchanged. The populated `ticketStatusRecord` occupies a field declared as spare, so its undocumented business semantics remain unresolved. Its text looks like four per-coupon `status&code` groups, but that layout is not documented, so it is not split further.

The sample ticket-number field has 15 characters although the screenshot comment says 13; the actual counted width is preserved. Type-5 field meanings remain provisional. No ticket, PNR, name or ordinal is treated as a stable passenger identity.

The other 43 documented strings (issue, exchange, refund, name adjustment, flight and purge spares, purge dates, FIM, ATO, commission, settlement and involuntary spares) are empty in all six samples. In the removed design each one was declared as a string with its own policy: names used the person mask, `formOfPayment` and `creditCardNumber` the payment-reference mask, exchange/new/old ticket and FIM numbers the ticket-reference mask, fare and commission amounts the financial mask, comments and undocumented spares the sensitive-attribute mask, `settlementAuthorizationCode` the unclassified-identifier mask, and the create, purge and unflown dates were kept. Decoded-text detection has no per-field policy: a populated string is scanned like any other, and one in which nothing is detected is published unchanged and listed for review (WR-014).

The approved status report used a provisional MSB-first convention, retained the original signed number and hex value, labelled set documented flags, and exposed reserved set bits. `0x00002000` maps to Checked in, `0x00004000` to Divided ETKT, and `0x00010002` to Adjusted flight plus Re-accommodation. Exchange destination uses document bit 31 only when Exchanged is set. The `h_indicator` word follows the same convention from the source comments: bits 0–2 are the ID type (001 = ETKT), bit 3 is ETKT was in PNR in AAA, bit 7 is Actioned all subsequent coupons and bit 12 is Action for OA. The samples hold `0x20000000`, `0x34000000`, `0x34080000` and `0x31000000`; bit 5 is set in two of them and stays visible as unassigned. These are display interpretations, never serialization changes. ETKT_06's spare text says ADJUSTED while its status word maps to Checked in; the report retained both without assuming they describe the same event.

The issue/reissue declarations remain documentary because there are no populated supplied examples and the source repeats `etkt_inds_2`. The removed decoder supported the verified type-5 history sample family; it did not silently enable the other documented union arms. The generic decoder needs no such support: it scans whatever counted strings a message holds.

Every ETKT example fits the same 67 raw words and 69 counted-string slots. Sixteen flight-information strings are empty in most examples and populated in ETKT_03. The two divide-event PNR strings are empty except in ETKT_05.

- ETKT/ETKT_02/ETKT_04/ETKT_06 are 672 bytes. ETKT and ETKT_02 are byte-identical.
- ETKT_03 is 760 bytes: populated flight strings add 88 bytes to the same sequence.
- ETKT_05 is 628 bytes: its status/reference text is eight rather than 65 characters, saving 60 encoded bytes including padding. Two newly populated six-character strings add 16 encoded bytes. Thus 672 - 60 + 16 = 628.

Flight airlines/numbers, dates, departure/arrival times, routes, class and remaining status text were kept. The provisional agent terminal address and event date/time stayed unchanged. Ticket numbers, uncertain identifiers and the initial eight-character reference prefix were masked. ETKT_05's new and old PNRs, the two six-character strings at value offsets 488 and 500, were preserved; those positions were calculated from that input rather than stored as dynamic message offsets in XML.

### Text0132 correction

The word at offset 132 is respectively 2, 2, 3, 4, 1 and 9 across the six samples. ETKT_05 happens to contain `00000001 31000000`: a scanner can incorrectly read that as a length-one string containing `1` with three zero pad bytes. ETKT_06's value nine disproves that interpretation for the common sequence. These are two independent raw words at 132 and 136, aligned with the history sequence number and indicator fields. The `Text0132` text field and its mask were removed; both words are preserved unchanged in every example, and the generic decoder keeps them as raw words (WR-015). After also preserving the two approved divide-event PNRs, ETKT_05 had five populated masked fields and five kept fields.

## Reviewed samples and results

Counts below describe populated binary text/capture fields under the removed field policies. Fixed structural tags, empty text slots and raw framing words are excluded. Retained verification evidence records source hashes and calculated field spans without personal values.

| Sample | Payload bytes | Kept | Masked |
| --- | ---: | ---: | ---: |
| ETKT.txt | 672 | 4 | 5 Mask |
| ETKT_02.txt | 672 | 4 | 5 Mask |
| ETKT_03.txt | 760 | 20 | 5 Mask |
| ETKT_04.txt | 672 | 4 | 5 Mask |
| ETKT_05.txt | 628 | 5 | 5 Mask |
| ETKT_06.txt | 672 | 4 | 5 Mask |

With decoded-text detection, all six samples are published through the real worker and local Presidio, with `Presidio:DefaultDetection` off. The parity check is names-only: none of the 30 values the former field policies protected is a name, so 0 are in scope and all 30 (ticket numbers, status reference prefixes, sign-in numbers and short strings) are out of scope and published as received; the samples hold no name in the `SURNAME/GIVEN` shape, so they are published unchanged. The generic decoder finds 7 counted strings of four or more characters in ETKT, ETKT_02, ETKT_04 and ETKT_06, 17 in ETKT_03 and 9 in ETKT_05. ETKT_05's two divide-event PNRs are published unchanged, and source hashes, payload sizes, raw words, string counts, zero padding, MQ headers, row framing and newline style are preserved.

## Configuration behavior (removed design)

The removed design kept three XML files with these responsibilities:

| File | Responsibility |
| --- | --- |
| `reference-layouts.xml` | Readable field names, baseline offsets/lengths and optional capture mappings |
| `xdr-layouts.xml` | Ordered binary words, counted strings, padding, arrays and optional groups |
| `feed-policies.xml` | Per-feed Keep/Mask actions, with an optional shared catalog |

The worker validated them together before it connected to the broker; a restart applied changes. Every exposed scalar needed exactly one policy, unknown structure went to the dead-letter queue without publication, and new structural variations required reviewed XML. Decoded-text detection replaced all three files with `etkt.xml`.

## Reviewed field names

ETKT names follow the screenshot history declarations (see the ETKT section), with unresolved business meanings labelled accordingly.

| Original field | Policy alias | Evidence and limits |
| --- | --- | --- |
| `Text0172` | `ticketStatusRecord` | Eight-character masked prefix followed by readable status text; the `StatusText` capture is kept |
| `Text0044`, `Text0072` | `agentCityCode`, `agentSignInNumber` | History declarations `h_aaa_city_code` and `h_aaa_sine_in_nbr` |
| `Text0088`, `ticketNumber` | `ticketNumber`, `historySequenceNumber` | `h_tkt_nbr` (15 characters in the samples) and `glob_tkt_hist_seq_nbr` (13) |
| `UnconfirmedLocator1`, `UnconfirmedLocator2` | `newPnr`, `oldPnr` | Divide-event PNRs `h_new_recloc` and `h_old_recloc`; populated only in ETKT_05 |

## Reproduce verification

```powershell
dotnet build PresidioDemo.slnx
dotnet run --project tests/FeedPipelineChecks
dotnet run --project tests/WorkerChecks
src/.venv/Scripts/python.exe -m unittest discover -s tests/unit-tests -p test_*.py -v
src/.venv/Scripts/python.exe tests/run_sample_verification.py
```

The sample verification runs the six ETKT samples through `tests/FeedPipelineHarness`. It keeps the outputs, transaction logs and a value-free `verification.json` in a fresh `.test-runs/e2e-*` folder. The harness exits with code 0 because all six samples are published.
