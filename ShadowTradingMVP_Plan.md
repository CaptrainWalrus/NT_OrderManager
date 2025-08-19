# Shadow Trading System MVP Plan

## Overview
Build a lightweight shadow trading system that runs 5 simple strategies virtually, tracks their performance, and activates the best performers for real trading.

## Architecture

### 1. Core Components

#### A. Shadow Trade Infrastructure
- **Location**: `/order-manager/ShadowTrading/`
- **Files**:
  - `ShadowPosition.cs` - Lightweight virtual position tracking
  - `ShadowPerformance.cs` - Strategy performance metrics
  - `SimpleShadowStrategies.cs` - 5 distinct strategy implementations

#### B. Integration Points
- **BuildNewSignal()** - Add shadow signal generation
- **OnBarUpdate()** - Check and close 5-bar old shadow positions
- **New Method**: `ProcessShadowTrades()` - Manage shadow lifecycle

### 2. Five Simple Strategies

1. **Three Bar Reversal** (Mean Reversion)
   - Entry: 3 consecutive red bars → Long
   - Exit: After 5 bars or 2 green bars

2. **Momentum Burst** (Trend Following)
   - Entry: Bar > 2x ATR with volume surge → Long
   - Exit: After 5 bars or momentum dies

3. **Range Breakout** (Volatility)
   - Entry: Break above 20-bar high → Long
   - Exit: After 5 bars or back inside range

4. **Fade The Gap** (Mean Reversion)
   - Entry: Gap up > 0.5% → Short
   - Exit: After 5 bars or gap fills

5. **Volume Accumulation** (Order Flow)
   - Entry: 3 bars rising volume + price → Long
   - Exit: After 5 bars or volume drops

### 3. Implementation Steps

#### Phase 1: Disconnect Existing System (Temporary)
```csharp
// In BuildNewSignal()
bool useShadowMode = true; // Toggle flag

if (!useShadowMode) {
    // Existing TraditionalStrategies code
    patternFunctionResponse traditionalSignal = traditionalStrategies.CheckAllTraditionalStrategies(...);
}
```

#### Phase 2: Shadow Position Tracking
```csharp
public class ShadowPosition {
    public string StrategyId { get; set; }
    public int EntryBar { get; set; }
    public double EntryPrice { get; set; }
    public string Direction { get; set; }
    public double Result { get; set; }
    public bool IsComplete { get; set; }
}

// In MainStrategy.cs
private List<ShadowPosition> activeShadowPositions = new List<ShadowPosition>();
private Dictionary<string, ShadowPerformance> strategyPerformance = new Dictionary<string, ShadowPerformance>();
```

#### Phase 3: Shadow Signal Generation
```csharp
// In BuildNewSignal() - after disconnecting traditional strategies
if (useShadowMode) {
    // Check all 5 simple strategies
    var shadowStrategies = new SimpleShadowStrategies(this);
    
    if (shadowStrategies.ThreeBarReversal()) {
        CreateShadowPosition("THREE_BAR_REVERSAL", "LONG");
    }
    
    if (shadowStrategies.MomentumBurst()) {
        CreateShadowPosition("MOMENTUM_BURST", "LONG");
    }
    
    // ... check other strategies
}
```

#### Phase 4: Shadow Exit Management
```csharp
private void ProcessShadowTrades() {
    var completedPositions = new List<ShadowPosition>();
    
    foreach (var shadow in activeShadowPositions) {
        // Check if 5 bars elapsed
        if (CurrentBar - shadow.EntryBar >= 5) {
            shadow.Result = shadow.Direction == "LONG" ? 
                Close[0] - shadow.EntryPrice : 
                shadow.EntryPrice - Close[0];
            shadow.IsComplete = true;
            completedPositions.Add(shadow);
            
            // Update performance
            UpdateStrategyPerformance(shadow);
        }
    }
    
    // Remove completed positions
    activeShadowPositions.RemoveAll(p => p.IsComplete);
}
```

#### Phase 5: Performance Tracking
```csharp
public class ShadowPerformance {
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public double TotalPoints { get; set; }
    public Queue<double> RecentResults = new Queue<double>(30);
    
    public double WinRate => TotalTrades > 0 ? 
        (double)WinningTrades / TotalTrades : 0;
    
    public double AverageResult => RecentResults.Count > 0 ? 
        RecentResults.Average() : 0;
        
    public bool IsActive { get; set; } = false;
}
```

#### Phase 6: Strategy Activation Logic
```csharp
private void EvaluateShadowStrategies() {
    // Every 50 bars, check performance
    if (CurrentBar % 50 != 0) return;
    
    // Require minimum trades
    var eligibleStrategies = strategyPerformance
        .Where(kvp => kvp.Value.TotalTrades >= 10)
        .Where(kvp => kvp.Value.WinRate > 0.6)
        .OrderByDescending(kvp => kvp.Value.AverageResult)
        .Take(2); // Activate top 2
    
    // Deactivate all first
    foreach (var perf in strategyPerformance.Values) {
        perf.IsActive = false;
    }
    
    // Activate top performers
    foreach (var kvp in eligibleStrategies) {
        kvp.Value.IsActive = true;
        Print($"[SHADOW] Activating {kvp.Key} - WR: {kvp.Value.WinRate:P0} Avg: {kvp.Value.AverageResult:F2}");
    }
}
```

#### Phase 7: Real Trade Execution
```csharp
// Modified BuildNewSignal() for real trades
if (useShadowMode) {
    // Check only ACTIVE strategies for real trades
    var activeStrategy = strategyPerformance
        .Where(kvp => kvp.Value.IsActive)
        .FirstOrDefault(kvp => {
            switch(kvp.Key) {
                case "THREE_BAR_REVERSAL":
                    return shadowStrategies.ThreeBarReversal();
                case "MOMENTUM_BURST":
                    return shadowStrategies.MomentumBurst();
                // ... other strategies
            }
            return false;
        });
    
    if (activeStrategy.Key != null) {
        // Return real signal
        var signal = new patternFunctionResponse {
            newSignal = FunctionResponses.EnterLong,
            signalType = activeStrategy.Key,
            recStop = 2 * ATR(14)[0],
            recTarget = 5 * ATR(14)[0]
        };
        return signal;
    }
}
```

### 4. Integration Timeline

1. **Hour 1**: Create shadow infrastructure files
2. **Hour 2**: Implement 5 simple strategies
3. **Hour 3**: Add shadow position tracking to MainStrategy
4. **Hour 4**: Implement performance tracking
5. **Hour 5**: Add activation logic
6. **Hour 6**: Test with historical data

### 5. Key Design Decisions

1. **No Database**: Everything in-memory for MVP
2. **Fixed 5-bar exits**: Simplifies tracking
3. **No stop loss tracking**: Assume 2xATR for virtual trades
4. **Simple strategies**: Easy to understand and debug
5. **Minimal integration**: Only touches BuildNewSignal() and OnBarUpdate()

### 6. Success Metrics

- Shadow system tracks 100+ trades per day
- Clear performance differentiation between strategies
- Top 2 strategies maintain >60% win rate when activated
- Minimal performance impact (<1ms per bar)

### 7. Future Enhancements

- Add more strategies (10-20)
- Dynamic position sizing based on confidence
- Time-of-day filtering
- Correlation analysis between strategies
- Persistent storage of performance stats