# Handoff

### Key Decisions
1. **Proportionate Design:** Organized into `Controllers`, `Services`, `Models`, `Dtos`, and `Validation` with zero third-party runtime dependencies. `InMemoryLoyaltyService` is registered as a singleton seeded with `W100` (100 points) and `W200` (50 points).
2. **Strict Validation (`400 Bad Request`):** `RequestValidator` validates payloads before any state lookup. It requires a JSON object body, case-sensitive `sourceId`, `externalTransactionId`, and `walletId` strings of 1–64 ASCII letters, digits, underscores, or hyphens (`char.IsAsciiLetterOrDigit`), and `points` as an integer from 1 to 1000 inclusive (rejecting strings, decimals like `25.0`/`25.5`, and out-of-range values). `GET /wallets/{walletId}` applies the same identifier rule, returning `400` for malformed IDs and `404` for valid but unknown IDs.
3. **Immutability & Domain Modeling:** `AwardIdentity(SourceId, ExternalTransactionId)` is a `readonly record struct` providing stack-allocated, ordinal, case-sensitive value equality as a dictionary key. `Award` and all DTOs are immutable `record`s (since accepted transactions and payloads never change), while `Wallet` is a mutable `class` whose balance updates over time (stored with `StringComparer.Ordinal`).
4. **Concurrency & Atomicity:** All state access in `InMemoryLoyaltyService` is synchronized via `_syncLock`. `ProcessAward` performs the idempotency check, wallet lookup, balance update, and award insertion atomically. `GetWallet` returns a snapshot copy inside the lock so concurrent readers always observe consistent balances.
5. **Error Precedence (`400 -> 409 -> 404`):**
   - `400 Bad Request` runs first before state lookups.
   - `409 Conflict` runs second by checking `_awards` for `(sourceId, externalTransactionId)`. Accepted awards are immutable; retrying an accepted identity with a different `walletId` (even an unknown wallet) or different `points` immediately returns `409` in a single hash lookup without querying `_wallets`.
   - `404 Not Found` runs third when the identity is new and `walletId` is unknown. Rejected requests never mutate balances or consume the identity, allowing a corrected retry to reuse it.

### Checks Executed
- Ran 34 automated xUnit integration and concurrency tests (`WebApplicationFactory<Program>`):
  - Seeded wallet lookups (`200`), unknown/wrong-case wallets (`404`), and invalid ID formats (`400`).
  - 18 invalid/malformed JSON payloads and boundary checks (`1`, `1000` points; `64` vs `65` char IDs).
  - Idempotent retries (`201` then `200` with identical `awardId` and single balance increment), distinct sources, conflicts (`409`), `404` identity reuse, and `409` over `404` precedence.
  - Multi-threaded concurrency tests (`ManualResetEventSlim` + `Task.WhenAll`) for 20 simultaneous identical retries, 50 simultaneous distinct awards, and simultaneous conflicting requests.
- Verified endpoints manually via Postman.

### Remaining Limitations
- All requirements are complete; remaining limitations reflect in-memory design constraints:
- State is in-memory for one process and resets on restart.
- Accepted awards in `_awards` are retained in memory for the process lifetime without TTL eviction.

### Approximate Time Spent
- ~115 minutes total (requirements & project setup: 35m, models & validation: 20m, endpoints, concurrency tests & documentation: 60m).
