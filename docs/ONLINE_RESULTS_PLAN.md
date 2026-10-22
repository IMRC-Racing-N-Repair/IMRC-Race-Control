# IMRC Live connection — v0.15.2

Implemented: anonymous one-click desktop UUID registration with private-key retry; administrator-only profile edits, key replacement and whole-track deletion with UUID blocking; opt-in HTTPS publishing, track-specific encrypted upload credentials, durable retry outbox, stable event/race IDs, public v1 read API and authoritative Series standings.

See the main README for pairing instructions, limits and verification. Full API specification and a TypeScript client for IMRC Setup Manager are in docs/IMRC_LIVE_API. The live reference is https://live.shopimrc.com/api-docs.

Next-stage features, not implemented: video, account-linked driver identities, remote setup writeback, multi-track Series publishing, owner-managed pairing reset and automatic cloud backups. Local scoring and SQLite remain authoritative.
