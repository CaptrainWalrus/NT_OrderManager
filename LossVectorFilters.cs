using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// Loss Vector Filters - Blocks trades based on market conditions that historically lead to losses
    /// Focuses on WHY trades fail, not just WHEN they fail
    /// </summary>
    public class LossVectorFilters
    {
        private readonly MainStrategy strategy;
        private readonly Action<string> logger;
        
        // Filter configuration - will be loaded from strategy config
        public bool EnableLossVectorFilters { get; set; } = true;
        
        // Market Structure Filters (WHY losses occur)
        public bool FilterLowLiquidity { get; set; } = false;
        public double FilterLowLiquidityThreshold { get; set; } = 1000; // Volume threshold
        
        public bool FilterHighVolatility { get; set; } = false;
        public double FilterHighVolatilityATR { get; set; } = 3.0; // ATR multiplier
        
        public bool FilterWideSpread { get; set; } = false;
        public double FilterWideSpreadTicks { get; set; } = 2.0; // Ticks
        
        // Momentum Filters
        public bool FilterAgainstTrend { get; set; } = false;
        public int FilterAgainstTrendEMAPeriod { get; set; } = 21;
        
        public bool FilterOverextended { get; set; } = false;
        public double FilterOverextendedRSI { get; set; } = 70; // RSI threshold
        
        // Session Transition Filters
        public bool FilterSessionOpen { get; set; } = false;
        public int FilterSessionOpenMinutes { get; set; } = 15; // Minutes after open
        
        public bool FilterSessionClose { get; set; } = false;
        public int FilterSessionCloseMinutes { get; set; } = 30; // Minutes before close
        
        // Performance-Based Filters
        public bool FilterConsecutiveLosses { get; set; } = false;
        public int FilterConsecutiveLossesCount { get; set; } = 3;
        
        public bool FilterDailyDrawdown { get; set; } = false;
        public double FilterDailyDrawdownAmount { get; set; } = 500; // Dollar amount
        
        // Correlation Filters
        public bool FilterLowCorrelation { get; set; } = false;
        public double FilterLowCorrelationThreshold { get; set; } = 0.5; // Correlation coefficient
        
        public LossVectorFilters(MainStrategy strategy, Action<string> logAction = null)
        {
            this.strategy = strategy;
            this.logger = logAction ?? ((msg) => { });
        }
        
        /// <summary>
        /// Load filter settings from strategy configuration
        /// </summary>
        public void LoadFromConfig(StrategyConfig config)
        {
            if (config?.LossVectorFilters == null) return;
            
            // Parse each filter setting using config helper methods
            EnableLossVectorFilters = config.GetFilterBool("EnableLossVectorFilters", true);
            
            // Market Structure
            FilterLowLiquidity = config.GetFilterBool("FilterLowLiquidity", false);
            FilterLowLiquidityThreshold = config.GetFilterDouble("FilterLowLiquidityThreshold", 1000);
            
            FilterHighVolatility = config.GetFilterBool("FilterHighVolatility", false);
            FilterHighVolatilityATR = config.GetFilterDouble("FilterHighVolatilityATR", 3.0);
            
            FilterWideSpread = config.GetFilterBool("FilterWideSpread", false);
            FilterWideSpreadTicks = config.GetFilterDouble("FilterWideSpreadTicks", 2.0);
            
            // Momentum
            FilterAgainstTrend = config.GetFilterBool("FilterAgainstTrend", false);
            FilterAgainstTrendEMAPeriod = config.GetFilterInt("FilterAgainstTrendEMAPeriod", 21);
            
            FilterOverextended = config.GetFilterBool("FilterOverextended", false);
            FilterOverextendedRSI = config.GetFilterDouble("FilterOverextendedRSI", 70);
            
            // Session
            FilterSessionOpen = config.GetFilterBool("FilterSessionOpen", false);
            FilterSessionOpenMinutes = config.GetFilterInt("FilterSessionOpenMinutes", 15);
            
            FilterSessionClose = config.GetFilterBool("FilterSessionClose", false);
            FilterSessionCloseMinutes = config.GetFilterInt("FilterSessionCloseMinutes", 30);
            
            // Performance
            FilterConsecutiveLosses = config.GetFilterBool("FilterConsecutiveLosses", false);
            FilterConsecutiveLossesCount = config.GetFilterInt("FilterConsecutiveLossesCount", 3);
            
            FilterDailyDrawdown = config.GetFilterBool("FilterDailyDrawdown", false);
            FilterDailyDrawdownAmount = config.GetFilterDouble("FilterDailyDrawdownAmount", 500);
            
            // Correlation
            FilterLowCorrelation = config.GetFilterBool("FilterLowCorrelation", false);
            FilterLowCorrelationThreshold = config.GetFilterDouble("FilterLowCorrelationThreshold", 0.5);
            
            Log($"Loaded {CountActiveFilters()} active loss vector filters");
        }
        
        /// <summary>
        /// Evaluate all active filters and determine if trade should be blocked
        /// Returns true if trade should be BLOCKED
        /// </summary>
        public bool ShouldFilterTrade(out string filterReason)
        {
            filterReason = "";
            
            if (!EnableLossVectorFilters)
                return false;
            
            // Check each filter in priority order
            
            // 1. Daily Drawdown (highest priority - account protection)
            if (FilterDailyDrawdown && CheckDailyDrawdown())
            {
                filterReason = $"Daily drawdown limit reached (${FilterDailyDrawdownAmount})";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 2. Consecutive Losses (prevent tilt trading)
            if (FilterConsecutiveLosses && CheckConsecutiveLosses())
            {
                filterReason = $"Consecutive losses limit reached ({FilterConsecutiveLossesCount})";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 3. Low Liquidity (wide spreads, slippage)
            if (FilterLowLiquidity && CheckLowLiquidity())
            {
                filterReason = $"Low liquidity detected (Volume < {FilterLowLiquidityThreshold})";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 4. High Volatility (unpredictable moves)
            if (FilterHighVolatility && CheckHighVolatility())
            {
                filterReason = $"High volatility detected (ATR > {FilterHighVolatilityATR}x normal)";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 5. Wide Spread (poor entry prices)
            if (FilterWideSpread && CheckWideSpread())
            {
                filterReason = $"Wide spread detected (> {FilterWideSpreadTicks} ticks)";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 6. Against Trend (fighting momentum)
            if (FilterAgainstTrend && CheckAgainstTrend())
            {
                filterReason = "Trade against primary trend";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 7. Overextended (late to move)
            if (FilterOverextended && CheckOverextended())
            {
                filterReason = $"Market overextended (RSI > {FilterOverextendedRSI})";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 8. Session Open (chaotic first minutes)
            if (FilterSessionOpen && CheckSessionOpen())
            {
                filterReason = $"Too close to session open (< {FilterSessionOpenMinutes} min)";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 9. Session Close (position risk overnight)
            if (FilterSessionClose && CheckSessionClose())
            {
                filterReason = $"Too close to session close (< {FilterSessionCloseMinutes} min)";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // 10. Low Correlation (signals misaligned)
            if (FilterLowCorrelation && CheckLowCorrelation())
            {
                filterReason = $"Low correlation with reference (< {FilterLowCorrelationThreshold})";
                Log($"[FILTER-BLOCKED] {filterReason}");
                return true;
            }
            
            // All filters passed
            return false;
        }
        
        // Individual filter check methods
        
        private bool CheckDailyDrawdown()
        {
            return Math.Abs(strategy.dailyProfit) >= FilterDailyDrawdownAmount;
        }
        
        private bool CheckConsecutiveLosses()
        {
            // Access consecutive losses from strategy
            // This would need to be tracked in MainStrategy
            return false; // Placeholder - implement based on strategy tracking
        }
        
        private bool CheckLowLiquidity()
        {
            if (strategy.Volume == null || strategy.CurrentBar < 1) return false;
            return strategy.Volume[0] < FilterLowLiquidityThreshold;
        }
        
        private bool CheckHighVolatility()
        {
            if (strategy.ATR(14) == null || strategy.CurrentBar < 14) return false;
            
            // Calculate average ATR over last 50 bars
            double avgATR = 0;
            int lookback = Math.Min(50, strategy.CurrentBar);
            for (int i = 0; i < lookback; i++)
            {
                avgATR += strategy.ATR(14)[i];
            }
            avgATR /= lookback;
            
            // Check if current ATR is above threshold
            return strategy.ATR(14)[0] > (avgATR * FilterHighVolatilityATR);
        }
        
        private bool CheckWideSpread()
        {
            var tickSize = strategy.TickSize;
            var spread = (strategy.GetCurrentAsk() - strategy.GetCurrentBid()) / tickSize;
            return spread > FilterWideSpreadTicks;
        }
        
        private bool CheckAgainstTrend()
        {
            if (strategy.EMA(FilterAgainstTrendEMAPeriod) == null || 
                strategy.CurrentBar < FilterAgainstTrendEMAPeriod) return false;
            
            // For now, simple check - enhance with actual signal direction
            double ema = strategy.EMA(FilterAgainstTrendEMAPeriod)[0];
            double price = strategy.Close[0];
            
            // This would need signal direction from BuildNewSignal
            // Placeholder logic
            return false;
        }
        
        private bool CheckOverextended()
        {
            if (strategy.RSI(14, 3) == null || strategy.CurrentBar < 14) return false;
            
            double rsi = strategy.RSI(14, 3)[0];
            return rsi > FilterOverextendedRSI || rsi < (100 - FilterOverextendedRSI);
        }
        
        private bool CheckSessionOpen()
        {
          
            if (strategy.sessionIterator == null) return false;
            
            var timeSinceOpen = strategy.Time[0] - strategy.sessionIterator.ActualSessionBegin;
            return timeSinceOpen.TotalMinutes < FilterSessionOpenMinutes;
        }
        
        private bool CheckSessionClose()
        {
            var sessionIterator = strategy.sessionIterator;
            if (sessionIterator == null) return false;
            
            var timeToClose = sessionIterator.ActualSessionEnd - strategy.Time[0];
            return timeToClose.TotalMinutes < FilterSessionCloseMinutes;
        }
        
        private bool CheckLowCorrelation()
        {
            // Placeholder - would need correlation calculation with reference instrument
            return false;
        }
        
        public int CountActiveFilters()
        {
            int count = 0;
            if (FilterLowLiquidity) count++;
            if (FilterHighVolatility) count++;
            if (FilterWideSpread) count++;
            if (FilterAgainstTrend) count++;
            if (FilterOverextended) count++;
            if (FilterSessionOpen) count++;
            if (FilterSessionClose) count++;
            if (FilterConsecutiveLosses) count++;
            if (FilterDailyDrawdown) count++;
            if (FilterLowCorrelation) count++;
            return count;
        }
        
        private void Log(string message)
        {
            logger?.Invoke($"[LossVectorFilters] {message}");
        }
    }
}