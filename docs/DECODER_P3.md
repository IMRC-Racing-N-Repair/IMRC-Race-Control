# P3 decoder input (introduced in v0.13.8)

For current automatic protocol coverage, see [DECODER_PROTOCOLS.md](DECODER_PROTOCOLS.md).

## Reading a new transponder

Connect the decoder as usual and open the decoder page. Pass the tag over the loop. **Detected Transponders** shows its decimal number, driver (or Unassigned), crossing count, last detection time, hit count and signal. Select the row and press **Copy Number**, then paste into the driver's class/transponder assignment. No event, class or existing driver is required to read a tag. **Clear Detected** only clears this diagnostic list; it does not erase drivers or race results.

The reader lists all validated P3 passings independently of registration. When a race roster is loaded, known tags use the normal check-in/lap path and unknown tags enter the existing assignment window, which also has a Copy Number button. During Practice, registered tags use practice timing; unregistered tags stay visible in the detector list and the footer points to that list. Reader observations are never replayed into a later race. Only the existing current-race unknown-crossing queue supports replay after assignment.

## Protocol scope and timing

This release implements passive AMB/MyLaps **P3 version 2 numeric passing records**. It handles TCP/serial chunks split anywhere, multiple frames per read, escaped delimiters, little-endian fields, complete-frame lengths and the P3 checksum. Status and other non-passing records never create a transponder or lap. Unknown optional fields are skipped; malformed or unsupported frames are diagnosed in the **Decoded** traffic column. Raw HEX/ASCII and existing on-disk logs are retained.

Up to 4,096 recent decoder/passing-number/clock/tag identities are retained to suppress retransmissions, including immediately after reconnecting. Partial frames and local clock anchors reset on connection changes. Decoder times are mapped to the local clock at receipt of the first batch, preserving microsecond intervals between crossings, including multiple passings in one read. This is not a decoder clock synchronization protocol: the initial mapping includes transport latency, and a clock reset or reconnect requires a new anchor. No historical-download or resend commands are sent.

P98/enhanced, iLap, first-contact-only records and other decoder protocols are not implemented by this change. Unsupported data remains available in raw logs; it is never guessed into a transponder number. Test live timing on the actual hardware before using this new input path for a scored event.

## Verification and protocol references

The implementation is independent C# code. Protocol field/framing facts were cross-checked against these authors' public P3 implementations and notes:

- https://github.com/datagutten/amb-p3-parser — P3 record layout, field identifiers, and real RC4/status packet fixtures (`tests/test_data/passing_rc4`, `tests/test_data/status`). The two short captured packet byte sequences are used as independent test vectors; their expected transponder, decoder, clock and signal values come from the accompanying tests. No library code or dependency is incorporated.
- https://github.com/vmindru/ambp3client/blob/master/NOTES/CRC_NOTES — checksum coverage, including delimiters and zeroed checksum bytes.
- https://stackoverflow.com/questions/75536231/crc16-calculation-translation-from-c-to-java — firsthand P3 checksum implementation discussion.

Executable checks cover real packet values, every split boundary, byte-at-a-time escaped input, combined records, duplicate/reconnect handling, checksum/length/field rejection, noise recovery, buffer limits, readable unassigned observations, preserved decoder time intervals and the ordered background ingress pipeline. Windows rendering, clipboard access and live hardware require a Windows/decoder check.
