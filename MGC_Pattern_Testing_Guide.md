# MGC Pattern Testing Guide - Using Existing Architecture

## Understanding the Architecture

### Signal Flow
```
CurvesStrategy.OnBarUpdate() 
    → BuildNewSignal() 
        → TraditionalStrategies.CheckAllTraditionalStrategies()
            → CheckMGCPatternFilter() (when MGC instrument detected)
                → Risk Agent Approval
                    → Trade Execution
```

### Key Components

1. **TraditionalStrategies.cs** - Static class with pattern detection methods
   - `CheckMGCPatternFilter()` - MGC-specific pattern detection
   - Returns `patternFunctionResponse` with signal details
   - NO strategy logic, just pattern detection

2. **CurvesStrategy.cs** - Actual trading strategy
   - Inherits from MainStrategy
   - Has OnBarUpdate() for bar processing
   - Calls BuildNewSignal() to get entry signals

3. **Risk Agent Integration**
   - All signals go through Risk Agent approval
   - Provides dynamic SL/TP based on historical performance
   - Confidence scoring for trade quality

## Testing MGC Patterns

### Option 1: Test MGC Patterns Exclusively

In CurvesStrategy parameters:
```csharp
// Force only MGC pattern testing
TraditionalStrategyFilter = TraditionalStrategyType.MGC_PATTERN_FILTER;
RiskAgentConfidenceThreshold = 0.6; // Minimum confidence to trade
```

### Option 2: Test MGC as Part of Ensemble

In CurvesStrategy parameters:
```csharp
// Test all patterns including MGC
TraditionalStrategyFilter = TraditionalStrategyType.ALL;
RiskAgentConfidenceThreshold = 0.6;
```

This will:
- Check all traditional strategies including MGC
- Require 85% directional consensus among signals
- Select highest confidence signal
- MGC patterns will only fire on MGC instruments

### Option 3: Create Minimal Test Strategy

```csharp
public class MGCPatternTest : MainStrategy
{
    protected override void OnStateChange()
    {
        base.OnStateChange();
        
        if (State == State.SetDefaults)
        {
            Name = "MGCPatternTest";
            Description = "Test MGC patterns only";
            
            // Force MGC patterns only
            TraditionalStrategyFilter = TraditionalStrategyType.MGC_PATTERN_FILTER;
            RiskAgentConfidenceThreshold = 0.6;
            
            // Use micro contract settings
            microContractStoploss = 50;
            microContractTakeProfit = 150;
        }
    }
    
    // BuildNewSignal() is inherited from MainStrategy
    // It will automatically use TraditionalStrategies
}
```

## Tracking Results

### 1. Built-in NinjaTrader Performance Tab
- Shows all trades with entry/exit details
- P&L, win rate, profit factor
- Can export to Excel

### 2. Strategy Prints
The pattern already includes detailed logging:
```
[TRADITIONAL-SINGLE] APPROVED: MGC_PATTERN_FILTER with SL: 40, TP: 130, Confidence: 0.750
[SIGNAL-APPROVED] 75.0% - MGC_PATTERN_FILTER long SL 40.00 TP 130.00
```

### 3. Custom Tracking in Strategy
Add to CurvesStrategy if needed:
```csharp
private Dictionary<string, int> patternCounts = new Dictionary<string, int>();

protected override void OnPositionUpdate(Position position, double averagePrice, 
    int quantity, MarketPosition marketPosition)
{
    base.OnPositionUpdate(position, averagePrice, quantity, marketPosition);
    
    if (position.MarketPosition == MarketPosition.Flat)
    {
        // Track pattern performance
        var pnl = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
        Print($"[MGC-RESULT] {Time[0]} PnL: ${pnl:F2}");
    }
}
```

## Pattern Parameters in CheckMGCPatternFilter()

Current implementation checks:
- **Close-to-high ratio**: <= 0.1 (bullish candle)
- **RSI**: Between 30-70 (not overbought/oversold)
- **EMA spacing**: 0.3-0.65% difference between EMA9 and EMA21
- **Bollinger Band position**: Specific ranges for upper/lower band
- **ATR percentage**: 0.015-0.025 (moderate volatility)
- **Volume confirmation**: >= 0.8x of 20-bar average
- **Trend filter**: EMA20 vs EMA50 alignment

## Optimization Process

### 1. Baseline Test
Run CurvesStrategy with:
- `TraditionalStrategyFilter = MGC_PATTERN_FILTER`
- `RiskAgentConfidenceThreshold = 0.6`
- Note win rate, profit factor, trade frequency

### 2. Confidence Threshold Testing
Test different thresholds:
- 0.5 - More trades, lower quality
- 0.6 - Balanced
- 0.7 - Conservative
- 0.8 - Very selective

### 3. Pattern Refinement
If performance is poor:
1. Export trades from NinjaTrader
2. Analyze losing trades for patterns
3. Adjust thresholds in `CheckMGCPatternFilter()`
4. Re-test

### 4. Risk Parameter Analysis
The Risk Agent automatically adjusts SL/TP, but you can:
- Modify the multipliers (currently 0.8 for SL, 0.87 for TP)
- Analyze if Risk Agent improvements help/hurt

## Common Testing Scenarios

### Scenario 1: Too Few Trades
- Pattern conditions may be too restrictive
- Try widening ranges slightly
- Check if MGC instrument is properly detected

### Scenario 2: Poor Win Rate
- Pattern may need refinement
- Check if all conditions are necessary
- Analyze time-of-day effects

### Scenario 3: Good Win Rate, Poor Profit Factor
- Risk/reward may need adjustment
- Check if stops are too tight
- Analyze Risk Agent's SL/TP recommendations

## Important Notes

1. **Pattern Detection Only**: `CheckMGCPatternFilter()` only detects patterns, it doesn't execute trades
2. **Risk Agent Integration**: All patterns go through Risk Agent for final approval
3. **Instrument Filtering**: Pattern only fires on instruments containing "MGC" in the name
4. **No State Management**: Pattern detection is stateless - each bar is evaluated independently

## Next Steps

1. Run baseline test using CurvesStrategy with MGC filter
2. Collect 100+ trades for statistical significance  
3. Analyze results and identify improvement areas
4. Iterate on pattern parameters
5. Compare performance vs other traditional strategies