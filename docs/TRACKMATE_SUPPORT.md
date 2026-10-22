# Trackmate RC / Iridium support — v0.15.3

## Setup
Choose Trackmate / USB Serial in Decoder, select its COM port, then Connect. The profile presets 9600 baud, 8-N-1, no handshake. Other timing applications must release the COM port. New blue boards use FTDI USB drivers; older red boards use a serial adapter and its driver. The network-style cable to the overhead sensors is not a PC Ethernet connection.

The startup bytes are `01 3F 2C 32 30 32 2C 30 2C 31 31 2C 0D 0A` (SOH followed by `?,202,0,11,` and CRLF). They are sent on connection/reconnection, not every race start. No proprietary software or third-party source is bundled.

## Parsing and scope
The independently implemented parser uses RC AMB-style SOH/tab/CRLF messages. Passing fields are marker, decoder identifier, passing counter, transponder identifier, and decoder seconds; optional numeric diagnostics may follow. The existing integration's executable field indexes (ID index 3, seconds index 4) are used, not its inconsistent older prose comment. Time conversion uses decimal arithmetic, and identifiers retain leading zeroes. Status messages never create crossings. Trackmate handling requires its explicit transport profile, isolating it from I-Lap's different four-field grammar and generic AMB CRC validation.

Only the known RC layout is accepted. Optional checksum-shaped trailers are format-checked, not verified: a reliable Trackmate checksum contract was not available in the references. Other firmware layouts require a hardware traffic sample rather than guessed field positions. A backward clock jump greater than two seconds resets the clock anchor; connection reset clears partial bytes and duplicate history because the startup resets the device timer.

Tests construct representative protocol messages; these are not real captured packets. Tests cover every message split, byte-by-byte reception, simultaneous cars, batched passings, exact duplicates, decoder-time lap intervals, malformed data, status records, oversized data, reconnects and ordered ingress routing. Windows UI and physical hardware remain unverified.

## References consulted
- Trackmate manufacturer support: https://trackmateracing.com/shop/en/content/17-rc-car-lap-counter
- Trackmate RC product description: https://trackmateracing.com/shop/en/home/91-rc-lap-counter.html
- Flip Side Racing interoperability reference, commit a37ffb9c969c3b4eeeb208415f9ce290887a2280: https://github.com/scattyv/FlipSide-Racing/blob/a37ffb9c969c3b4eeeb208415f9ce290887a2280/Classes/SerialLapCounters.rbbas (startup byte sequence, 9600 baud, RC field positions). Its GPL source was inspected for protocol facts only; no source routines were copied or distributed.

For field testing: connect in Practice, observe the detected transponder ID, assign it to a driver, and make several passes separated by the minimum lap time. Compare lap count and timing before using a scored event. If parsing fails, save the live raw traffic log with the model/firmware information for diagnosis.
