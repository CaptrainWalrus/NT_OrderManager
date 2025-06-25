# NinjaTrader Pushcut Approval Microservice

This microservice handles mobile trade approval for NinjaTrader via Pushcut notifications.

## Features

- **Chart Generation**: Creates price charts with entry/stop/target levels
- **Mobile Notifications**: Sends approvals requests to iPhone via Pushcut
- **Sync Polling**: NinjaTrader waits for approval with timeout
- **Auto-cleanup**: Removes old trades and charts automatically

## API Endpoints

### POST /trade/request
Submit a trade for approval. Returns immediately with trade_id.

**Request Body:**
```json
{
  "instrument": "ES 03-25",
  "timeframe": "5min",
  "bars": [
    {
      "time": "2024-01-15T14:30:00",
      "open": 4125.0,
      "high": 4128.75,
      "low": 4124.25,
      "close": 4127.50,
      "volume": 1250
    }
  ],
  "signal": {
    "direction": "LONG",
    "entry_price": 4127.50,
    "risk_amount": 50,
    "target_amount": 150,
    "pattern_type": "breakout",
    "confidence": 0.85
  }
}
```

### GET /trade/status/{trade_id}
Check approval status. Returns: pending, approved, rejected, timeout

### GET /chart/{trade_id}
Serve the generated chart image

### POST /trade/approve/{trade_id}
Approve trade (called by Pushcut)

### POST /trade/reject/{trade_id}
Reject trade (called by Pushcut)

## Deployment

1. Push this folder to GitHub
2. Connect to Render.com
3. Deploy as Web Service
4. Use the generated URL in NinjaTrader

## Configuration

- **Timeout**: 5 minutes (configurable in app.py)
- **Chart Style**: Dark background for mobile viewing
- **Cleanup**: Old trades removed after 1 hour

## Usage from NinjaTrader

```csharp
// Request approval
var response = await httpClient.PostAsJsonAsync(
    "https://nt-pushcut-approval.onrender.com/trade/request", 
    tradeData
);

// Poll for result
while (timeout not reached) {
    var status = await httpClient.GetAsync(
        $"https://nt-pushcut-approval.onrender.com/trade/status/{tradeId}"
    );
    // Check status.approved/rejected/timeout
}
``` 