# Pushcut Trading Approval Integration Plan - Python Server Architecture

## Overview

This plan establishes a mobile-first trade approval system using Pushcut with a Python server backend hosted on Render.com. The system generates dynamic chart visualizations and handles approval workflows without requiring localhost connectivity, making it accessible from anywhere.

## Core Concept

### Current Approach
- Trading signals automatically execute when detected
- No manual intervention or approval step
- Real-time trading operates fully automated

### New Approach (Python Server Architecture)
- Trading signals trigger chart generation on Python server
- Server renders last 400 bars with trade context
- Pushcut notification sent with chart link and approval buttons
- User approves/rejects via mobile with full chart visualization
- 5-7 minute timeout window with automatic rejection
- Only active during real-time trading (State.Realtime)

## Architecture

### System Components

#### 1. **NinjaTrader Integration** (C# - OrderManager)
- **PushcutService.cs**: Minimal partial class for server communication
- **Purpose**: Generate trade_uuid, send OHLCV data, poll for responses
- **Responsibilities**:
  - Generate unique trade identifiers
  - Send market data to Python server
  - Poll server for approval status
  - Execute trades based on approval

#### 2. **Python Server** (Render.com hosted)
- **Technology**: Flask/FastAPI + matplotlib/plotly
- **URL Structure**: `https://your-app.onrender.com`
- **Purpose**: Chart generation and approval workflow management
- **Endpoints**:
  ```
  POST /generate-chart/{trade_uuid}     # Receive OHLCV data
  GET  /chart/{trade_uuid}              # View chart page
  POST /approve/{trade_uuid}            # Approval webhook
  POST /reject/{trade_uuid}             # Rejection webhook  
  GET  /status/{trade_uuid}             # Check approval status
  ```

#### 3. **Pushcut Mobile Integration**
- **Notification Name**: "Trade Approval"
- **Chart Display**: Dynamic image from Python server
- **Actions**: APPROVE/REJECT buttons with webhook callbacks
- **URL**: `https://api.pushcut.io/8a_iGKpg-bNQDqFVFQAON/notifications/Trade%20Approval`

### Data Flow Architecture
```
NinjaTrader Signal → Python Server → Chart Generation → Pushcut → iPhone
       ↓                  ↓              ↓              ↓         ↓
1. Generate trade_uuid → 2. Receive OHLCV → 3. Render chart → 4. Send notif → 5. User views
       ↓                  ↓              ↓              ↓         ↓
6. Poll for response ← 7. Store decision ← 8. Process webhook ← 9. User decides
       ↓
10. Execute/Reject Trade
```

### Integration Points
- **MainStrategy.cs**: Add approval hooks in entry methods
- **PushcutService.cs**: New lightweight partial class for server communication
- **Python Server**: Hosted chart generation and approval management
- **Pushcut App**: Mobile notification configuration

## Implementation Phases

### Phase 1: Python Server Foundation (Status: 📋 PLANNED)
**Goal**: Deploy Python server with chart generation and approval endpoints

#### Tasks:
- [ ] Task 1.1: Create Python Flask/FastAPI server
  - File: `python-server/app.py`
  - Purpose: Core server with all endpoints
  - Size: ~300 lines (server setup, routing, chart generation)
  
- [ ] Task 1.2: Implement chart rendering system
  - File: `python-server/chart_renderer.py`
  - Purpose: Generate trading charts from OHLCV data
  - Libraries: matplotlib/plotly for chart generation
  - Size: ~200 lines (chart styling, indicators, annotations)

- [ ] Task 1.3: Deploy to Render.com
  - Files: `requirements.txt`, `render.yaml`
  - Purpose: Production deployment with auto-scaling
  - Configuration: Environment variables, domain setup

✅ **Phase 1 Success Criteria:**
- Python server responds to all endpoints
- Chart generation works with sample data
- Render.com deployment is stable and accessible

### Phase 2: NinjaTrader Integration (Status: �� PLANNED)
**Goal**: Create lightweight C# integration for server communication

#### Tasks:
- [ ] Task 2.1: Create PushcutService partial class
  - File: `OrderManager/PushcutService.cs`
  - Purpose: Minimal server communication layer
  - Size: ~200 lines (HTTP calls, polling, trade_uuid generation)
  
- [ ] Task 2.2: Add OHLCV data serialization
  - File: `OrderManager/PushcutService.cs`
  - Purpose: Format market data for Python server
  - Size: ~100 lines (JSON serialization, bar data packaging)

- [ ] Task 2.3: Implement approval polling system
  - File: `OrderManager/PushcutService.cs`
  - Purpose: Check server for approval status
  - Size: ~150 lines (async polling, timeout handling)

✅ **Phase 2 Success Criteria:**
- NinjaTrader successfully sends data to Python server
- Polling system retrieves approval status
- Error handling works for server connectivity issues

### Phase 3: Pushcut Mobile Setup (Status: 📋 PLANNED)
**Goal**: Configure mobile notifications with chart visualization

#### Tasks:
- [ ] Task 3.1: Configure Pushcut notification
  - App: Pushcut iOS app
  - Purpose: Create "Trade Approval" notification with dynamic content
  - Settings: Enable dynamic title, text, image, actions
  
- [ ] Task 3.2: Set up webhook actions
  - App: Pushcut iOS app
  - Purpose: APPROVE/REJECT buttons call Python server
  - URLs: 
    - `https://your-app.onrender.com/approve/{trade_uuid}`
    - `https://your-app.onrender.com/reject/{trade_uuid}`

- [ ] Task 3.3: Test notification workflow
  - Purpose: End-to-end testing of mobile approval
  - Validation: Chart images display, buttons work, responses recorded

✅ **Phase 3 Success Criteria:**
- Pushcut notifications display charts correctly
- APPROVE/REJECT buttons trigger server endpoints
- Mobile workflow is responsive and reliable

### Phase 4: MainStrategy Integration (Status: 📋 PLANNED)
**Goal**: Integrate approval workflow into existing trade execution

#### Tasks:
- [ ] Task 4.1: Add approval hooks to entry methods
  - File: `OrderManager/MainStrategy.cs`
  - Purpose: Add approval gates before trade execution
  - Changes: Modify all `EntryLimitFunctionLite()` calls
  - Size: ~50 lines (conditional approval checks)
  
- [ ] Task 4.2: Add configuration parameters
  - File: `OrderManager/MainStrategy.cs`
  - Purpose: Enable/disable Pushcut approval system
  - Size: ~30 lines (NinjaScript properties)

- [ ] Task 4.3: Implement timeout and fallback logic
  - File: `OrderManager/PushcutService.cs`
  - Purpose: Handle server unavailability and timeouts
  - Size: ~100 lines (fallback to auto-execution, error scenarios)

✅ **Phase 4 Success Criteria:**
- Approval workflow integrates seamlessly with existing strategy
- Historical/backtest modes remain unaffected
- Timeout and error scenarios handled gracefully

## Technical Specifications

### Python Server Endpoints

#### POST /generate-chart/{trade_uuid}
**Purpose**: Receive OHLCV data and generate chart
**Payload**:
```json
{
  "instrument": "ES",
  "timeframe": "5min",
  "bars": [
    {"timestamp": "2024-01-15T14:30:00", "open": 4125.0, "high": 4128.75, "low": 4124.25, "close": 4127.50, "volume": 1250},
    // ... last 400 bars
  ],
  "signal": {
    "direction": "LONG",
    "entry_price": 4127.50,
    "risk_amount": 50,
    "target_amount": 150,
    "pattern_type": "breakout",
    "confidence": 0.85
  },
  "indicators": {
    "ema_20": 4126.80,
    "vwap": 4127.15,
    "rsi": 62.4
  }
}
```

#### GET /chart/{trade_uuid}
**Purpose**: Display chart page for mobile viewing
**Response**: HTML page with interactive chart

#### POST /approve/{trade_uuid} & POST /reject/{trade_uuid}
**Purpose**: Record user decision
**Response**: JSON confirmation

#### GET /status/{trade_uuid}
**Purpose**: Check approval status for NinjaTrader polling
**Response**:
```json
{
  "trade_uuid": "123e4567-e89b-12d3-a456-426614174000",
  "status": "approved|rejected|pending",
  "timestamp": "2024-01-15T14:32:15Z",
  "expires_at": "2024-01-15T14:37:15Z"
}
```

### Pushcut Notification Format
```json
{
  "title": "🔥 ES LONG Signal",
  "text": "Entry: $4127.50 | Risk: $50 | Target: $150\nPattern: Breakout | Confidence: 85%",
  "image": "https://your-app.onrender.com/chart/{trade_uuid}",
  "isTimeSensitive": true,
  "actions": [
    {
      "name": "APPROVE ✅",
      "url": "https://your-app.onrender.com/approve/{trade_uuid}",
      "urlBackgroundOptions": {"httpMethod": "POST"}
    },
    {
      "name": "REJECT ❌", 
      "url": "https://your-app.onrender.com/reject/{trade_uuid}",
      "urlBackgroundOptions": {"httpMethod": "POST"}
    }
  ]
}
```

### NinjaTrader Integration Code
```csharp
// In MainStrategy.cs - Enhanced entry methods
private async Task<bool> EntryLimitFunctionLiteWithApproval(/* parameters */)
{
    if (isRealTime && EnablePushcutApproval)
    {
        string tradeUuid = Guid.NewGuid().ToString();
        
        // Send chart data to Python server
        await pushcutService.SendChartDataAsync(tradeUuid, GetMarketData());
        
        // Send Pushcut notification
        await pushcutService.SendTradeNotificationAsync(tradeUuid, signal);
        
        // Poll for approval (with timeout)
        bool approved = await pushcutService.WaitForApprovalAsync(tradeUuid, TimeSpan.FromMinutes(7));
        
        if (!approved) return false;
    }
    
    // Execute original entry logic
    return EntryLimitFunctionLite(/* parameters */);
}
```

## Configuration Parameters

### NinjaScript Properties
```csharp
[NinjaScriptProperty]
[Display(Name = "Enable Pushcut Approval", Order = 1, GroupName = "Pushcut Settings")]
public bool EnablePushcutApproval { get; set; } = false;

[NinjaScriptProperty]
[Display(Name = "Python Server URL", Order = 2, GroupName = "Pushcut Settings")]
public string PythonServerUrl { get; set; } = "https://your-app.onrender.com";

[NinjaScriptProperty]
[Display(Name = "Pushcut API Key", Order = 3, GroupName = "Pushcut Settings")]
public string PushcutApiKey { get; set; } = "8a_iGKpg-bNQDqFVFQAON";

[NinjaScriptProperty]
[Display(Name = "Approval Timeout (Minutes)", Order = 4, GroupName = "Pushcut Settings")]
public int ApprovalTimeoutMinutes { get; set; } = 7;
```

## Implementation Status

### Overall Progress: 0% Complete

### Phase Status:
- 📋 Phase 1: Python Server Foundation - PLANNED
- 📋 Phase 2: NinjaTrader Integration - PLANNED  
- 📋 Phase 3: Pushcut Mobile Setup - PLANNED
- 📋 Phase 4: MainStrategy Integration - PLANNED

### Current Pushcut Configuration:
- ✅ **API URL Confirmed**: `https://api.pushcut.io/8a_iGKpg-bNQDqFVFQAON/notifications/Trade%20Approval`
- ✅ **Webhook URL Ready**: `https://webhook.site/751cf028-5b6a-4ce1-a529-ea400dbb46fa`
- ✅ **Basic Notifications Working**: Simple test notifications delivered successfully
- 📋 **Chart Integration**: Pending Python server deployment

### Next Steps:
1. Deploy Python server to Render.com with chart generation
2. Create PushcutService.cs for server communication
3. Configure Pushcut notification with chart image support
4. Test end-to-end workflow with sample data

## Advantages of Python Server Architecture

### ✅ **Scalability Benefits**
- **No localhost limitations**: Accessible from anywhere
- **Professional hosting**: Render.com provides reliable infrastructure
- **Auto-scaling**: Server scales based on demand
- **Global accessibility**: Works on any network (WiFi, cellular, VPN)

### ✅ **Chart Visualization Benefits**
- **Professional charts**: matplotlib/plotly generate publication-quality charts
- **Dynamic rendering**: Charts generated with current market context
- **Mobile-optimized**: Charts sized and styled for mobile viewing
- **Rich indicators**: Support for multiple technical indicators and annotations

### ✅ **Approval Workflow Benefits**
- **Reliable webhooks**: Professional webhook handling vs. localhost callbacks
- **Persistent storage**: Server maintains approval state reliably
- **Timeout management**: Server-side timeout tracking independent of NinjaTrader
- **Audit trail**: Complete log of approval decisions and timing

### ✅ **Development Benefits**
- **Language flexibility**: Python ecosystem for rapid chart development
- **Easy deployment**: Simple git-based deployment to Render.com
- **Independent scaling**: Chart generation doesn't impact NinjaTrader performance
- **Future extensibility**: Easy to add features like trade analytics, logging, etc.

## Success Metrics

### Primary Goals
- ✅ 100% reliable notification delivery with chart visualization
- ✅ <45 second end-to-end response time (chart generation + notification)
- ✅ Zero interference with backtesting/historical analysis
- ✅ <2% performance impact on real-time trading

### Secondary Goals
- ✅ Professional-quality chart images in mobile notifications
- ✅ Approval workflow completes in <3 minutes average
- ✅ Server handles 99.9% uptime during trading hours
- ✅ Mobile-first user experience with intuitive approval interface

---

**Note**: This Python server architecture provides a professional, scalable foundation for mobile trade approval while maintaining clean separation from the core NinjaTrader strategy. The external server approach eliminates network connectivity issues and provides a superior chart visualization experience.