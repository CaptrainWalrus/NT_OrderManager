# MGC Pattern Backtesting Guide

## Overview

This guide explains how to backtest the XGBoost-discovered MGC patterns using the custom backtesting framework integrated with NinjaTrader's Risk Agent system.

## Architecture

### 1. Pattern Discovery → Implementation → Backtesting Flow

```
Python ML Discovery → TraditionalStrategies.cs → Risk Agent → MGCBacktestStrategy → Analysis
```

### 2. Key Components

#### TraditionalStrategies.cs - CheckMGCPatternFilter()
- Implements the discovered MGC patterns from XGBoost analysis
- Pattern conditions based on:
  - Close-to-high ratio
  - RSI levels (45-60 for long, 40-55 for short)
  - EMA spacing (9 vs 21)
  - ATR percentage (volatility filter)
  - Bollinger Band positioning
  - Volume confirmation

#### Risk Agent Integration
- All signals go through Risk Agent approval
- Risk Agent provides:
  - Confidence scores based on historical performance
  - Dynamic stop loss and take profit levels
  - Position sizing recommendations
- Uses agentic memory to learn from past trades

#### MGCBacktestStrategy.cs
- Extends CurvesStrategy for full integration
- Tracks pattern occurrences vs trades taken
- Records detailed trade metrics
- Exports results in JSON and CSV formats

## Running Backtests

### 1. Single Pattern Test
```csharp
// In strategy parameters
TraditionalStrategyFilter = TraditionalStrategyType.MGC_PATTERN_FILTER;
RiskAgentConfidenceThreshold = 0.6; // Minimum confidence to take trades
```

### 2. Confidence Threshold Testing
The strategy can test multiple confidence thresholds to find optimal settings:
- 0.5 - Very aggressive (more trades, lower quality)
- 0.6 - Balanced approach
- 0.7 - Conservative (fewer trades, higher quality)
- 0.8 - Very conservative

### 3. Output Location
Results are saved to: `C:\temp\mgc_backtest_results\`
- JSON files: Complete session data with pattern statistics
- CSV files: Individual trade records for analysis

## Analysis Tools

### Python Analysis Script (analyze_mgc_backtest.py)

Run the analysis:
```bash
python analyze_mgc_backtest.py --path C:/temp/mgc_backtest_results
```

The script provides:
1. **Pattern Performance Analysis**
   - Win rates by direction (long vs short)
   - Average P&L per pattern type
   - Pattern occurrence vs trade frequency

2. **Confidence Impact Analysis**
   - Performance by Risk Agent confidence level
   - Optimal confidence thresholds
   - Visual charts of confidence vs win rate

3. **Time-Based Analysis**
   - Performance by hour of day
   - Identifies optimal trading times

4. **Risk/Reward Analysis**
   - Actual R:R ratios vs recommendations
   - Stop loss and take profit effectiveness

5. **Equity Curve Visualization**
   - Cumulative P&L over time
   - Drawdown analysis

## Interpreting Results

### Key Metrics to Watch

1. **Pattern Occurrence Rate**
   - How often do patterns appear?
   - What percentage result in trades?

2. **Win Rate by Confidence**
   - Does higher confidence = higher win rate?
   - What's the optimal threshold?

3. **Risk-Adjusted Returns**
   - Profit factor (gross profit / gross loss)
   - Expectancy per trade
   - Maximum drawdown

4. **Pattern Degradation**
   - Do patterns perform worse over time?
   - Is the Risk Agent adapting appropriately?

### Example Output Interpretation

```
[MGC_LONG] Pattern Statistics:
  Occurrences: 423
  Trades Taken: 87 (20.6% of occurrences)
  Win Rate: 58.6% (51W/36L)
  Avg Win: $67.45
  Avg Loss: $42.30
  Profit Factor: 1.82
  Expectancy: $22.34
```

This shows:
- Pattern appears frequently (423 times)
- Risk Agent filters out 79.4% as low confidence
- Remaining trades have positive expectancy
- 1.82 profit factor is healthy (>1.5 is good)

## Optimization Workflow

### 1. Baseline Test
- Run with default settings (0.6 confidence threshold)
- Establish baseline performance metrics

### 2. Confidence Optimization
- Test thresholds from 0.5 to 0.8
- Find sweet spot between trade frequency and quality

### 3. Pattern Refinement
- Analyze losing trades for common characteristics
- Adjust pattern parameters in TraditionalStrategies.cs
- Re-test with refined patterns

### 4. Risk Parameter Tuning
- Analyze actual vs recommended SL/TP
- Adjust Risk Agent parameters if needed
- Test different position sizing approaches

## Advanced Analysis

### Feature Importance
The backtest exports all feature values for each trade. Use these to:
- Identify which features best predict success
- Find feature ranges that work best
- Discover new pattern combinations

### Regime Analysis
- Compare performance across different market conditions
- Identify when patterns work best (trending vs ranging)
- Adjust pattern usage based on market regime

### Cross-Validation
- Test patterns on different time periods
- Ensure patterns aren't overfit to discovery period
- Validate on out-of-sample data

## Troubleshooting

### Common Issues

1. **No Trades Generated**
   - Check if patterns are too restrictive
   - Lower confidence threshold temporarily
   - Verify Risk Agent is running

2. **Poor Performance**
   - Pattern may be overfit to discovery data
   - Market regime may have changed
   - Risk parameters may need adjustment

3. **High Drawdown**
   - Position sizing may be too aggressive
   - Stop losses may be too wide
   - Consider adding regime filters

## Best Practices

1. **Always Compare to Baseline**
   - Run control test with no pattern filter
   - Ensure patterns add value over random entry

2. **Use Walk-Forward Analysis**
   - Test on rolling windows
   - Continuously update pattern parameters

3. **Monitor Pattern Decay**
   - Track performance over time
   - Be ready to retire patterns that stop working

4. **Combine with Other Strategies**
   - MGC patterns work best as part of ensemble
   - Don't rely on single pattern type

## Next Steps

1. Run initial backtest with current pattern implementation
2. Analyze results using Python script
3. Identify areas for improvement
4. Iterate on pattern parameters
5. Implement additional discovered patterns
6. Create ensemble strategy combining multiple patterns

Remember: The goal is not just high win rate, but consistent positive expectancy with manageable risk.