# ProjectX Integration Architecture

**Strategy**: Dual execution with profit synchronization and fail-safe exits

---

## 1. Core Components

### 1.1 SharedCustomClasses.cs
**Update `brokerSelection` enum:**
```csharp
public enum brokerSelection
{
    Apex,
    Topstep,
    NinjaTrader,
    BlueSky_projectx        // ADD
}
```

**Update `OrderPriceStats` class:**
```csharp
public class OrderPriceStats
{
    // ... existing fields ...
    public ProjectXPositionInfo OrderStatsProjectXInfo { get; set; } = new ProjectXPositionInfo();  // ADD
}
```

**Add ProjectX data structures:**
```csharp
public class ProjectXPositionInfo
{
    public int positionId { get; set; } = 0;
    public string contractId { get; set; } = "";
    public int size { get; set; } = 0;
    public double entryPrice { get; set; } = 0.0;
    public double currentPrice { get; set; } = 0.0;
    public double unrealizedPnL { get; set; } = 0.0;
    public double calculatedProfit { get; set; } = 0.0;  // Our calculated profit
    public DateTime lastUpdate { get; set; } = DateTime.MinValue;
    public bool isActive { get; set; } = false;
}

// Add to signalExitAction enum
EMERGENCY  // For profit drift exits
```

### 1.2 ProjectXApiClient.cs (NEW FILE)
```csharp
public class ProjectXApiClient
{
    private string baseUrl = "https://gateway-api-demo.s2f.projectx.com";
    private string authToken;
    private int accountId;
    
    public async Task<bool> AuthenticateAsync(string userName, string apiKey)
    public async Task<ProjectXOrderResponse> PlaceOrderAsync(ProjectXOrder order)
    public async Task<bool> CancelOrderAsync(int orderId)
    public async Task<List<ProjectXPosition>> GetOpenPositionsAsync()
    public async Task<ProjectXOrderStatus> GetOrderStatusAsync(int orderId)
    public async Task<ProjectXMarketData> GetCurrentPriceAsync(string contractId)
}
```

### 1.3 ProjectXBridge.cs (NEW FILE)
```csharp
public class ProjectXBridge
{
    private ProjectXApiClient client;
    private Dictionary<string, int> entryUUIDToProjectXOrderId;
    
    public async Task<bool> ProjectXEnterLong(int quantity, string entryUUID)
    public async Task<bool> ProjectXEnterShort(int quantity, string entryUUID)
    public async Task<bool> ProjectXExitLong(int quantity, string exitUUID, string entryUUID)
    public async Task<bool> ProjectXExitShort(int quantity, string exitUUID, string entryUUID)
    
    public async Task<ProjectXPositionInfo> GetProjectXPositionByUUID(string entryUUID)
    public async Task EmergencyExitPosition(string entryUUID)
}
```

---

## 2. Entry/Exit Integration

### 2.1 OrderLiteActions.cs
**Update entry logic (line 224, 237):**
```csharp
// OLD:
EnterLong(1, 1, entryUUID);

// NEW:
if (selectedBroker == brokerSelection.BlueSky_projectx)
{
    await ExecuteProjectXEntryLong(1, entryUUID);
}
else
{
    EnterLong(1, 1, entryUUID);
}

private async Task ExecuteProjectXEntryLong(int quantity, string entryUUID)
{
    // 1. Submit ProjectX order (async)
    Task<bool> projectXTask = projectXBridge.ProjectXEnterLong(quantity, entryUUID);
    
    // 2. Submit NT sim order (immediate)
    EnterLong(1, quantity, entryUUID);
    
    // 3. Monitor in background
    _ = Task.Run(() => MonitorProjectXOrder(projectXTask, entryUUID));
}
```

### 2.2 PositionManagement.cs
**Update entry logic (line 87, 103):**
```csharp
if (selectedBroker == brokerSelection.BlueSky_projectx)
{
    await ExecuteProjectXEntryLong(simEntry.quantity, simEntry.EntryOrderUUID);
}
else
{
    EnterLong(strategyDefaultQuantity, simEntry.quantity, simEntry.EntryOrderUUID);
}
```

### 2.3 OrderManagement.cs
**Update exit logic (line 1819, 1840):**
```csharp
if (selectedBroker == brokerSelection.BlueSky_projectx)
{
    await ExecuteProjectXExitLong(orderRecordMaster.EntryOrder.Quantity, 
                                  orderRecordMaster.ExitOrderUUID, 
                                  orderRecordMaster.EntryOrderUUID);
}
else
{
    ExitLong(1, orderRecordMaster.EntryOrder.Quantity, 
             orderRecordMaster.ExitOrderUUID, 
             orderRecordMaster.EntryOrderUUID);
}
```

---

## 3. Profit Synchronization

### 3.1 OrderObjectStatsThread.cs
**Insert after line 281:**
```csharp
// ========== PROJECTX PROFIT SYNC CHECK ==========
if (selectedBroker == brokerSelection.BlueSky_projectx)
{
    await UpdateProjectXProfit(simStop);
    
    double ntProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
    double pxProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProjectXInfo.calculatedProfit;
    
    bool isProfitSynced = CheckProfitSync(ntProfit, pxProfit, simStop.EntryOrderUUID);
    
    if (!isProfitSynced)
    {
        Print($"🚨 PROFIT DRIFT: {simStop.EntryOrderUUID} NT:{ntProfit:F2} PX:{pxProfit:F2}");
        await HandleProfitDrift(simStop, ntProfit, pxProfit);
    }
}

private async Task UpdateProjectXProfit(simulatedStop simStop)
{
    var projectXPosition = await projectXBridge.GetProjectXPositionByUUID(simStop.EntryOrderUUID);
    if (projectXPosition != null)
    {
        double realProfit = CalculateProjectXProfit(projectXPosition, simStop);
        
        // Update the ProjectX info object
        var pxInfo = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProjectXInfo;
        pxInfo.positionId = projectXPosition.positionId;
        pxInfo.contractId = projectXPosition.contractId;
        pxInfo.size = projectXPosition.size;
        pxInfo.entryPrice = projectXPosition.entryPrice;
        pxInfo.currentPrice = projectXPosition.currentPrice;
        pxInfo.unrealizedPnL = projectXPosition.unrealizedPnL;
        pxInfo.calculatedProfit = realProfit;
        pxInfo.lastUpdate = DateTime.Now;
        pxInfo.isActive = true;
    }
}

private bool CheckProfitSync(double ntProfit, double pxProfit, string entryUUID)
{
    double profitDifference = Math.Abs(ntProfit - pxProfit);
    double toleranceThreshold = 50.0; // $50 tolerance
    return profitDifference <= toleranceThreshold;
}

private async Task HandleProfitDrift(simulatedStop simStop, double ntProfit, double pxProfit)
{
    double driftAmount = Math.Abs(ntProfit - pxProfit);
    
    if (driftAmount > 200.0) // $200 critical threshold
    {
        await projectXBridge.EmergencyExitPosition(simStop.EntryOrderUUID);
        await alertService.SendUrgentAlert($"Critical drift: {simStop.EntryOrderUUID}");
    }
}
```

---

## 4. Configuration

### 4.1 MainStrategy.cs
**Add ProjectX configuration properties:**
```csharp
[Display(Name = "ProjectX API Key", GroupName = "Broker Settings", Order = 1)]
public string ProjectXApiKey { get; set; } = "";

[Display(Name = "ProjectX Username", GroupName = "Broker Settings", Order = 2)]  
public string ProjectXUsername { get; set; } = "";

[Display(Name = "ProjectX Account ID", GroupName = "Broker Settings", Order = 3)]
public int ProjectXAccountId { get; set; } = 0;

[Display(Name = "ProjectX Contract ID", GroupName = "Broker Settings", Order = 4)]
public string ProjectXContractId { get; set; } = ""; // e.g., "CON.F.US.RTY.Z24"
```

**Add initialization in OnStateChange:**
```csharp
else if (State == State.SetDefaults)
{
    if (selectedBroker == brokerSelection.BlueSky_projectx)
    {
        projectXBridge = new ProjectXBridge();
        await projectXBridge.InitializeAsync(ProjectXUsername, ProjectXApiKey, ProjectXAccountId);
    }
}
```

---

## 5. Monitoring & Fail-Safe

### 5.1 ProjectX Order Monitoring
```csharp
private async Task MonitorProjectXOrder(Task<bool> projectXTask, string entryUUID)
{
    try
    {
        // Wait for ProjectX order submission (max 5 seconds)
        bool projectXSubmitted = await Task.WhenAny(projectXTask, Task.Delay(5000)) == projectXTask 
                               && await projectXTask;
        
        if (!projectXSubmitted)
        {
            Print($"⚠️ ProjectX order failed: {entryUUID}");
            await EmergencyExitNTPosition(entryUUID);
            return;
        }
        
        // Monitor ProjectX fill (up to 30 seconds)
        bool projectXFilled = await WaitForProjectXFill(entryUUID, 30000);
        
        if (!projectXFilled)
        {
            Print($"❌ ProjectX order NOT filled: {entryUUID}");
            await EmergencyExitNTPosition(entryUUID);
        }
    }
    catch (Exception ex)
    {
        Print($"🚨 ProjectX monitoring error: {ex.Message}");
        await EmergencyExitNTPosition(entryUUID);
    }
}

private async Task EmergencyExitNTPosition(string entryUUID)
{
    // Find NT position and close immediately
    var position = GetNTPositionByEntryUUID(entryUUID);
    if (position != null)
    {
        if (position.MarketPosition == MarketPosition.Long)
            ExitLong(1, position.Quantity, $"EXIT_{entryUUID}", entryUUID);
        else
            ExitShort(1, position.Quantity, $"EXIT_{entryUUID}", entryUUID);
            
        await alertService.SendUrgentAlert($"ProjectX failed - NT position closed: {entryUUID}");
    }
}
```

---

## 6. Implementation Checklist

### Phase 1: Core Infrastructure
- [ ] Update `brokerSelection` enum in SharedCustomClasses.cs
- [ ] Add `OrderStatsProfit_projectx` to OrderPriceStats
- [ ] Create ProjectXApiClient.cs with authentication & order management
- [ ] Create ProjectXBridge.cs with entry/exit methods

### Phase 2: Integration Points  
- [ ] Update OrderLiteActions.cs entry logic (lines 224, 237)
- [ ] Update PositionManagement.cs entry logic (lines 87, 103)
- [ ] Update OrderManagement.cs exit logic (lines 1819, 1840)
- [ ] Add ProjectX configuration properties to MainStrategy.cs

### Phase 3: Profit Synchronization
- [ ] Insert ProjectX profit sync logic in OrderObjectStatsThread.cs (after line 281)
- [ ] Implement UpdateProjectXProfit() method
- [ ] Implement CheckProfitSync() with tolerance checks
- [ ] Implement HandleProfitDrift() with emergency exits

### Phase 4: Monitoring & Safety
- [ ] Implement MonitorProjectXOrder() for fill confirmation
- [ ] Implement EmergencyExitNTPosition() for failed orders
- [ ] Add alerting for profit drift and order failures
- [ ] Test with demo ProjectX environment

---

## 7. Key Safety Features

- **Dual Execution**: NT simulation + ProjectX real orders
- **Fail-Safe Exits**: NT position closed if ProjectX fails
- **Profit Sync Monitoring**: Real-time drift detection
- **Emergency Stops**: Automatic exits on critical drift
- **Order Fill Monitoring**: 30-second timeout for fills
- **Alerting System**: Urgent notifications for all failures

---

## 8. File Modification Summary

| File | Changes | Lines |
|------|---------|-------|
| SharedCustomClasses.cs | Add ProjectX enum + data structures | ~50 |
| ProjectXApiClient.cs | **NEW** - API client implementation | ~200 |
| ProjectXBridge.cs | **NEW** - Integration bridge | ~150 |  
| OrderLiteActions.cs | Update entry logic | ~30 |
| PositionManagement.cs | Update entry logic | ~20 |
| OrderManagement.cs | Update exit logic | ~20 |
| OrderObjectStatsThread.cs | Add profit sync logic | ~80 |
| MainStrategy.cs | Add configuration properties | ~20 |

**Total**: ~570 lines of code across 8 files