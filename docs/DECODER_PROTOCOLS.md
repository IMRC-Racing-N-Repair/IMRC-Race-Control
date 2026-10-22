# Automatic decoder recognition — v0.14.1

The Decoder page now recognizes supported incoming formats automatically on the selected connection. There is no protocol dropdown to maintain. The detected format appears above live traffic; all recognized passings use the same readable transponder list, race check-in/lap path and Practice path.

| Incoming format | Recognition and scope |
| --- | --- |
| AMB/MyLaps P3 v2 numeric passings | Existing binary framing, escaping, length/checksum validation and duplicate suppression. |
| AMB enhanced text | SOH + tab-separated records with `x` hexadecimal CRC; numeric `@` passings. Status does not count as a crossing. |
| I-Lap native seven-digit | SOH/tab-framed initialization and passing records. Exact shape validation, fragmented/coalesced reads, duplicate suppression, milliseconds retained. Explicit I-Lap connection mode sends startup once per connection. |
| OpenStint | Direct ZeroMQ subscriber, single-frame text messages; P passings only, millisecond steady-clock time, OPN/AMB/RC4 type tags. ID zero is supported. Status, sync and learning messages are diagnostics. |
| AMB20 | Strict `@` plus ten-digit transponder/time records, hundredth-second clock. Transponder zero/status/init does not count as a lap. IDs in this legacy format are limited to two digits. |

The built-in simulator now emits valid enhanced passing records through this same decoder path. It cycles five test transponders; unassigned ones appear as Unknown driver in Practice and accumulate laps after their first crossing. Simulator traffic remains labeled Built-in Simulator.

Set the actual COM port or IP/port and the correct serial parameters, then connect. Serial baud, data bits, parity, stop bits and handshake are remembered. General passive recognition does **not** discover network addresses, install USB drivers or guess baud rates. The explicit I-Lap profile sends its documented seven-digit startup command; other connection profiles never send it. Selecting I-Lap presets 9600 baud, 8 data bits, no parity, one stop bit, no handshake; adjust if your hardware requires different framing. Restored serial settings take precedence over this selection preset.

This is not universal hardware support. This release implements I-Lap's native seven-digit serial protocol and OpenStint's documented feed. Other iLap variants, P98 binary variants, unrelated proprietary handshakes, USB APIs and other unsupported formats are not implemented or certified by this update. For those, the exact model/firmware, protocol documentation or a captured decoder log is still needed. Unsupported bytes stay in raw traffic and never become guessed IDs/laps.

The text decoder handles fragmented/coalesced records, rejects bad checksums/invalid clocks, bounds incomplete lines, suppresses duplicates and isolates binary payloads from text recognition. P3 details remain in DECODER_P3.md. As before, decoder clock intervals are anchored to local receipt time; this is not clock synchronization and initial transport latency remains. Reconnection/clock reset requires a fresh anchor.

Protocol facts and the independent enhanced/AMB20 sample vectors were checked against the original author's implementation notes at https://habr.com/ru/articles/196514/ (AMB20 time layout and AMB enhanced text record/checksum coverage). P3 references are in DECODER_P3.md. No vendor hardware was available for live loop verification.

## OpenStint connection

Run OpenStint's decoder process separately. Choose **OpenStint / ZeroMQ**, host `127.0.0.1` for this computer or its remote host/IP, default port `5556`, and Connect. NetMQ provides the ZeroMQ subscription; raw LAN/TCP mode is not a ZeroMQ connection. No newline is required within a message. Only passing messages create crossings; RSSI remains signed decimal dB in diagnostics. Both older EVM and newer duration/MER trailing fields are accepted without treating them as timestamps. Unknown practice tags use the same path as other decoders.

Connection is confirmed after the first OpenStint message; no initial data after six seconds, or no further data for fifteen seconds, reports an error. The normal Automatically reconnect option controls recovery. ZeroMQ cannot recover passings sent while disconnected. Timestamps anchor the decoder's millisecond clock to local receipt; clock restart reanchors, not a lap-time continuation guarantee across downtime. Unsupported multipart messages are discarded. Each decoder endpoint has independent duplicate history.

## Protocol references

- I-Lap wire shape, initialization command `01 25 0D 0A`, and independent passing vectors: Flip Side Racing, `Classes/SerialLapCounters.rbbas`, `ReadILapData`, `polling`, `selfOpen`; main window serial defaults: `Windows/mainWindow.rbfrm`. Reviewed revision `a37ffb9c969c3b4eeeb208415f9ce290887a2280`: https://github.com/scattyv/FlipSide-Racing . Only protocol facts were used; the IMRC parser is independently implemented.
- OpenStint owner-maintained decoder protocol: https://openstint.org/decoder/docs/decoder-protocol.html and https://github.com/zsellera/openstint/blob/master/docs/decoder-protocol.md . Public default endpoint: project README.
- NetMQ subscriber documentation: https://netmq.readthedocs.io/en/latest/pub-sub/ . Dependency pinned to 4.0.4.3.

Tests use independent documented I-Lap/OpenStint vectors and a local ZeroMQ publisher. Physical hardware is not certified by those tests.
