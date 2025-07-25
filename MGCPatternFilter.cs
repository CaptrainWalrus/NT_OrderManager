using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class YourStrategy : Strategy
    {
        // Pattern validation method based on discovered MGC patterns
        private bool ValidateMGCPattern()
        {
            // The discovered pattern shows these key characteristics:
            // 1. close_to_high: 0.0000 to 0.1000 (price near high of bar)
            // 2. rsi_overbought: 0 (not overbought)
            // 3. ema_9_21_diff: 0.3062 to 0.6228 (specific EMA spacing)
            
            // Calculate features
            double closeToHigh = (High[0] - Close[0]) / (High[0] - Low[0] + 0.0001);
            double rsi = RSI(14, 1)[0];
            double ema9 = EMA(9)[0];
            double ema21 = EMA(21)[0];
            double emaDiff = (ema9 - ema21) / Close[0] * 100; // As percentage
            
            // Bollinger Band features
            double bbUpper = Bollinger(2, 20).Upper[0];
            double priceToBBUpper = (Close[0] - bbUpper) / bbUpper;
            
            // Pattern validation
            bool patternValid = true;
            
            // Feature 1: Close should be near high (bullish candle)
            if (closeToHigh > 0.1)
                patternValid = false;
            
            // Feature 2: RSI should not be overbought
            if (rsi > 70)
                patternValid = false;
            
            // Feature 3: EMA spacing should be in range
            if (emaDiff < 0.3 || emaDiff > 0.65)
                patternValid = false;
            
            // Feature 4: Price relative to BB upper
            if (priceToBBUpper < -0.04 || priceToBBUpper > 0.42)
                patternValid = false;
            
            // Additional safety filters
            double atrPct = ATR(14)[0] / Close[0];
            if (atrPct > 0.025) // Avoid high volatility
                patternValid = false;
            
            // Log pattern details
            if (patternValid)
            {
                Print(string.Format(
                    "MGC Pattern Valid - Time: {0}, CloseToHigh: {1:F3}, RSI: {2:F1}, EMA Diff: {3:F3}, ATR%: {4:F3}",
                    Time[0], closeToHigh, rsi, emaDiff, atrPct
                ));
            }
            
            return patternValid;
        }
        
        // Conservative filter based on market conditions
        private bool ConservativeMGCFilter()
        {
            // Time filter - avoid overnight and early morning
            if (ToTime(Time[0]) < ToTime(8, 30, 0) || ToTime(Time[0]) > ToTime(15, 0, 0))
                return false;
            
            // Trend filter - ensure we're not fighting the trend
            if (EMA(20)[0] < EMA(50)[0]) // Downtrend
                return false;
            
            // Volume filter - need some volume
            if (Volume[0] < SMA(Volume, 20)[0] * 0.8)
                return false;
            
            // Volatility filter
            double atr14 = ATR(14)[0];
            double atrPct = atr14 / Close[0];
            
            // For MGC, typical good volatility is 1.5-2.5%
            if (atrPct < 0.015 || atrPct > 0.030)
                return false;
            
            return true;
        }
        
        // Main pattern entry method
        protected bool ShouldEnterMGCPattern()
        {
            // First check conservative filters
            if (!ConservativeMGCFilter())
                return false;
            
            // Then validate specific pattern
            if (!ValidateMGCPattern())
                return false;
            
            // Additional checks can go here
            // For example, check recent trades to avoid overtrading
            
            return true;
        }
        
        // Example usage in OnBarUpdate
        
        // Helper method to analyze pattern performance
        private void LogPatternMetrics()
        {
            // This can be called periodically to track pattern performance
            double closeToHigh = (High[0] - Close[0]) / (High[0] - Low[0] + 0.0001);
            double rsi = RSI(14, 1)[0];
            double ema9 = EMA(9)[0];
            double ema21 = EMA(21)[0];
            double emaDiff = (ema9 - ema21) / Close[0] * 100;
            double atrPct = ATR(14)[0] / Close[0];
            
            Print(string.Format(
                "Pattern Metrics - CTH: {0:F3}, RSI: {1:F1}, EMA: {2:F3}, ATR%: {3:F3}",
                closeToHigh, rsi, emaDiff, atrPct
            ));
        }
    }
    
    // Data class for pattern rules (optional - for loading from JSON)
    public class MGCPatternRule
    {
        public string Feature { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public double Importance { get; set; }
        
        public bool Validate(double value)
        {
            return value >= MinValue && value <= MaxValue;
        }
    }
}