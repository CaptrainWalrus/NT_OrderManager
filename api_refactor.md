# CurvesV2 Service API Refactor Plan (HTTP-Centric)

## Goal
// ... existing code ...
-   [ ] **Remove Static Signal State:**
    -   [ ] Delete static fields: `CurrentBullStrength`, `CurrentBearStrength`, `CurrentMatches`, `PatternName`, `LastSignalTimestamp`, `SignalsAreFresh`.
    -   [ ] Delete `ResetStaticData` method (or modify if other static data exists).
-   [ ] **Implement HTTP Health Check:**
// ... existing code ...
    -   [ ] Return `true` for success status codes, `false` otherwise (handle exceptions).

-   [ ] **Implement Synchronous Bar Send:**
    -   [ ] Create `public bool SendBarSync(BarDataPacket barData)`.
    -   [ ] Inside, use `HttpClient` and `.GetAwaiter().GetResult()` to POST JSON to `/api/bars/{instrument}`.
    -   [ ] Include timeout and error handling.
    -   [ ] Return `true`/`false` for success/failure.

-   [ ] **Implement Synchronous Signal Fetch:**
    -   [ ] Create `public SignalData GetSignalsSync(string instrument)`: (Rename/adapt the previous synchronous debug method).
    -   [ ] Use `client.GetAsync(endpoint, timeoutCts.Token).GetAwaiter().GetResult();` to fetch from `GET /api/signals/{instrument}`.
// ... existing code ...
-   [ ] **`OnBarUpdate()`:**
    -   [ ] Create `BarDataPacket` from current bar info.
    -   [ ] Call `bool success = service.SendBarSync(packet);` (blocking).
    -   [ ] Implement logic to decide when to fetch signals (e.g., `if (CurrentBar % N == 0)`).
    -   [ ] When fetching: Call `SignalData signals = service.GetSignalsSync(Instrument.FullName);` (blocking).
// ... existing code ...
-   [ ] **`POST /api/bars/{instrument}`:** Exists and correctly processes incoming bar data JSON (needs `symbol`, `timestamp`, `open`, `high`, `low`, `close`, `volume`).
-   [ ] **`GET /api/signals/{instrument}`:** Exists and returns JSON matching `CurvesV2Response` structure, specifically including the `signals` object with `bull` and `bear` fields.
-   [ ] **`GET /` (or other health check endpoint):** Returns `200 OK` for basic reachability check. 