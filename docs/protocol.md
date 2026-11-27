# LanBridge protocol v1

This document describes the wire protocol implemented by LanBridge 0.1.8. It is intentionally small: one TCP port, one UDP port, JSON payloads, and Windows-negotiated TLS with mutual certificates.

## Transport

| Purpose | Transport | Default port |
| --- | --- | --- |
| Discovery | UDP broadcast | `45873` |
| Pairing, control, file data | TCP + TLS (Windows-negotiated, 1.2 or newer when available) | `45873` |

- The server presents a self-signed certificate created on first launch. Clients verify its full SHA-256 fingerprint against the value learned during discovery or stored from pairing.
- The client also presents a certificate. The server requires one and checks that the fingerprint in `HELLO` matches the certificate it actually received. During first pairing any certificate is accepted; after pairing the fingerprint must be in the peer store.
- TLS is negotiated by Windows. The negotiated protocol and cipher are logged. Certificate chains are not used; LanBridge is a pinning system, not a public-key infrastructure.
- SNI is set to `lanbridge` but is not used for routing.
- Each request opens a new TCP/TLS connection. There is no connection reuse and no multiplexing.

## Framing

Every frame is:

```
+----------------+------------+----------------------+
| length (4, BE) | type (1)   | JSON or binary data  |
+----------------+------------+----------------------+
```

- `length` counts the payload only, not the five-byte header.
- Control payloads are limited to **1 MiB**. `FILE_DATA` payloads are limited to **65,540 bytes** (65,536 bytes of file data plus a 4-byte file index).
- A connection that sends an unknown type, an oversized frame, or a truncated frame is closed and logged.

## Frame types

| Value | Name | Direction | Payload |
| ---: | --- | --- | --- |
| 1 | `HELLO` | client → server | JSON `Hello` |
| 2 | `HELLO_ACK` | server → client | JSON `HelloAck` |
| 3 | `PING` | client → server | empty |
| 4 | `PONG` | server → client | empty |
| 5 | `PAIR_PROMPT` | server → client | JSON `PairPrompt` |
| 6 | `PAIR_PROOF` | client → server | JSON `PairProof` |
| 7 | `PAIR_ABORT` | either | empty or JSON `Cancel` |
| 8 | `PAIR_OK` | server → client | empty |
| 9 | `PAIR_DENY` | server → client | JSON `PairDeny` |
| 10 | `OFFER` | client → server | JSON `Offer` |
| 11 | `ACCEPT` | server → client | JSON `Accept`, including optional resume offsets |
| 12 | `REJECT` | server → client | JSON `Reject` |
| 13 | `FILE_BEGIN` | client → server | JSON `FileBegin`, including the requested resume offset |
| 14 | `FILE_DATA` | client → server | binary: 4-byte BE index + bytes |
| 15 | `FILE_END` | client → server | JSON `FileEnd` |
| 16 | `FILE_OK` | server → client | JSON `FileOk` |
| 17 | `FILE_ERROR` | server → client | JSON `FileError` |
| 18 | `CANCEL` | either | JSON `Cancel` |
| 19 | `DONE` | client → server | JSON `Done` |
| 20 | `DONE_ACK` | server → client | JSON `DoneAck` |
| 21 | `ERROR` | server → client | JSON `Error` |

All JSON is UTF-8. Unknown fields are ignored so a newer peer can add fields without breaking an older one. A receiver validates every length, index, name, size, and checksum before using it.

## Common messages

```json
// HELLO
{
  "v": 1,
  "id": "device-uuid",
  "name": "Laptop-A",
  "fp": "sha256-of-certificate-raw-bytes",
  "nonce": "base64-16-random-bytes",
  "intent": "pair",
  "paired": false,
  "port": 45873
}

// HELLO_ACK
{
  "v": 1,
  "id": "device-uuid",
  "name": "Studio-PC",
  "fp": "sha256-of-certificate-raw-bytes",
  "nonce": "base64-16-random-bytes",
  "paired": false,
  "busy": false,
  "port": 45873
}
```

`intent` is one of `ping`, `pair`, or `send`. `paired` is the sender's view of the peer store. `busy` is the server's current inbound state.

An `ACCEPT` may include resume information for every file:

```json
{
  "transferId": "...",
  "resume": [
    { "index": 0, "offset": 524288, "prefixSha256": "..." }
  ]
}
```

An empty or missing `resume` list means every file starts at zero. The client verifies the local prefix hash before sending the rest.

## Pairing

Pairing uses a short authentication string (SAS). The code is **derived from both certificates and fresh nonces; it is never sent over the network**.

1. The initiator connects, presents its certificate, and sends `HELLO` with `intent: "pair"` and a 16-byte nonce.
2. The server sends `HELLO_ACK` with its own nonce. It then checks hidden mode and the pairing lock. A hidden server denies unpaired pairing with `hidden`; a second concurrent pairing is denied with `busy`.
3. The server shows an approval prompt. Its 120-second approval window starts when the prompt is shown.
4. If approved, both sides compute the same six-digit code:

```
low, high = sort(fpInitiator, fpResponder)
key = SHA256("lanbridge-pair-v1" || low || high)
sas = HMAC-SHA256(key, nonceInitiator || nonceResponder)
code = first 4 bytes of sas as a big-endian uint, modulo 1,000,000, zero-padded
```

5. The server displays the code and sends `PAIR_PROMPT { expires: 120 }`. The code window starts now and lasts 120 seconds.
6. The initiator's user types the code locally. The initiator checks it before sending anything. On a match it computes:

```
proofKey = SHA256("lanbridge-pair-key-v1" || code)
proof = HMAC-SHA256(proofKey, "confirm" || nonceInitiator || nonceResponder
                    || fpInitiator || fpResponder)
```

7. The initiator sends `PAIR_PROOF { proof }` in base64. The server compares the proof in constant time. On success it stores the initiator's full fingerprint and endpoint, saves the peer store, and sends `PAIR_OK`. On failure it sends `PAIR_DENY { reason: "invalid" }`.

The code is checked on the initiator's side, and the proof is checked on the server's side. A man-in-the-middle has different certificate fingerprints and nonces toward each side, so the two screens show different codes and the proof fails. The code is not a password and is never sent in a frame.

A server that receives `PAIR_ABORT` or an invalid proof closes the connection without writing a peer record. Three wrong local attempts, a timeout, or a user cancel also abort. After a failed pairing the server applies a short global cooldown before accepting another pairing request, and the short SAS plus explicit approval remain the defense.

## Transfer

The initiator must have the server's fingerprint in its peer store, and the server must have the initiator's fingerprint. Both flags in `HELLO` and `HELLO_ACK` must be true.

1. `HELLO { intent: "send", paired: true }` → `HELLO_ACK { paired: true, busy: false }`.
2. `OFFER { transferId, totalSize, files: [{ name, size }] }` → `ACCEPT { transferId, resume }`, `REJECT { reason }`, or `ERROR`.
3. For each file:
   - `FILE_BEGIN { index, name, size, resumeOffset }`
   - one or more `FILE_DATA` frames
   - `FILE_END { index, sha256 }`
   - `FILE_OK { index }` or `FILE_ERROR { index, code, message }`.

Resume is opt-in through the advertised offset. A receiver only offers a partial file when its sidecar name, declared size, and on-disk length are valid. The sender hashes its own prefix and compares it with `prefixSha256`; on a mismatch it sends `CANCEL` and the receiver deletes the partial file. The final SHA-256 always covers the complete file, including the prefix.
4. After all files: `DONE { transferId }` → `DONE_ACK { transferId }`.

The receiver creates a new file for each entry, writes to a `.part` file in a private `parts` subfolder, hashes the bytes as they arrive, compares the SHA-256 from `FILE_END`, then renames the `.part` file into place. Existing files are never overwritten: the receiver picks `name (1).ext`, `name (2).ext`, and so on.

Only one inbound transfer and one outbound transfer may run at a time per app. Sending and receiving at the same time is allowed. A `CANCEL` aborts the current transfer; completed files are kept. A user cancel deletes the partial `.part` file; an interrupted network connection keeps it in the private `parts` folder with a `.part.resume` sidecar for a later retry. If an individual file fails, the whole transfer is aborted with `ERROR { code: "transfer_failed", completed }`; the sender's result reports how many files completed.

`REJECT` reasons include `declined`, `timeout`, and `disk_full`. `ERROR` codes include `not_paired`, `not_lan`, `busy_inbound`, `invalid_offer`, `offer_required`, `file_begin_required`, `transfer_failed`, and `timeout`.

## Discovery

Discovery uses UDP port `45873`, IPv4 only. A packet is `LNB1` followed by UTF-8 JSON, maximum 1,400 bytes.

```json
{ "t": "a", "v": 1, "id": "device-uuid", "name": "Laptop-A",
  "port": 45873, "fp": "sha256-hex", "discoverable": true }

{ "t": "q", "v": 1, "id": "device-uuid" }
```

- `a` is an announcement; `q` is a query.
- Announcements are sent every five seconds. A query is sent when the device list opens, is refreshed, or the network changes or resumes.
- A received announcement is **not trusted on its own**. Before a device appears in the list, LanBridge opens a TCP/TLS connection and sends `PING`. That proves the sender controls the private key for the certificate whose fingerprint it advertised.
- Announces are ignored when they match this device's ID or fingerprint, come from an address outside the local subnets, or arrive faster than four per second from one address.
- A hidden device neither announces nor queries. Paired peers can still reach a hidden device by its last known endpoint because the TCP listener remains available.
- When broadcast discovery is blocked, the user can enter an IPv4 address and port manually. The manual path uses the same TLS and six-digit pairing flow; the certificate is learned during that session and pinned only after the code succeeds.

## Limits and timeouts

| Item | Value |
| --- | --- |
| TCP/UDP port | `45873` |
| Max files per offer | 5,000 |
| Max control frame | 1 MiB |
| Max file-data frame | 65,540 bytes |
| Connect timeout | 5 seconds |
| Control frame timeout | 30 seconds |
| Pairing approval window | 120 seconds |
| Pairing code window | 120 seconds |
| Pairing attempts | 3 local attempts per session |
| Offer decision window | 120 seconds |
| Per-file idle timeout | 60 seconds |
| Discovery interval | 5 seconds |
| Discovery expiry | 15 seconds without an announcement |
| Discovery packet limit | 1,400 bytes |
