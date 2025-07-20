using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    public partial class MainStrategy : Strategy
    {
        // Feature queue for pending signals
        private static ConcurrentDictionary<string, PendingFeatureSet> pendingFeatures = new ConcurrentDictionary<string, PendingFeatureSet>();
        
        // Risk Agent client
        private static HttpClient riskAgentHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static string riskAgentUrl = "http://localhost:3017/api/evaluate-risk";
        
        /// <summary>
        /// Get the timeframe in minutes from NinjaTrader BarsArray
        /// </summary>
        public int GetTimeframeMinutes()
        {
            try
            {
                // Get timeframe from primary bars array
                if (BarsArray != null && BarsArray.Length > 0 && BarsArray[0] != null)
                {
                    var barsPeriod = BarsArray[0].BarsPeriod;
                    if (barsPeriod != null)
                    {
                        return barsPeriod.BaseBarsPeriodValue;
                    }
                }
                
                // Fallback: try to get from current Bars
                if (Bars != null && Bars.BarsPeriod != null)
                {
                    return Bars.BarsPeriod.BaseBarsPeriodValue;
                }
                
                Print("[TIMEFRAME] Warning: Could not determine timeframe, defaulting to 1 minute");
                return 1; // Default to 1-minute
            }
            catch (Exception ex)
            {
                Print($"[TIMEFRAME] Error getting timeframe: {ex.Message}, defaulting to 1 minute");
                return 1; // Default to 1-minute on error
            }
        }
        
        /// <summary>
        /// Generate all features matching ME service implementation
        /// </summary>
        public Dictionary<string, double> GenerateFeatures( DateTime timestamp, string instrument)
        {
            var startTime = DateTime.Now;
            var features = new Dictionary<string, double>();
            
            // Get all feature components
            var marketContext = GetMarketContext(timestamp);
            var technicalIndicators = GetTechnicalIndicators();
            var marketMicrostructure = GetMarketMicrostructure();
            var volumeAnalysis = GetVolumeAnalysis();
            var patternRecognition = GetPatternRecognition();
            
            // Combine all features
            foreach (var kvp in marketContext) features[kvp.Key] = kvp.Value;
            foreach (var kvp in technicalIndicators) features[kvp.Key] = kvp.Value;
            foreach (var kvp in marketMicrostructure) features[kvp.Key] = kvp.Value;
            foreach (var kvp in volumeAnalysis) features[kvp.Key] = kvp.Value;
            foreach (var kvp in patternRecognition) features[kvp.Key] = kvp.Value;
            
            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            Print($"[FEATURE-GEN] Generated {features.Count} features for {instrument} at {timestamp} - Duration: {duration:F0}ms");
            
            return features;
        }
        
        /// <summary>
        /// Market context features
        /// </summary>
        private Dictionary<string, double> GetMarketContext(DateTime timestamp)
        {
            var features = new Dictionary<string, double>();
            
            // Basic price features
            features["close_price"] = Close[0];
            features["open_price"] = Open[0];
            features["high_price"] = High[0];
            features["low_price"] = Low[0];
            
            // Price changes
            features["price_change_1"] = CurrentBar > 0 ? Close[0] - Close[1] : 0;
            features["price_change_5"] = CurrentBar > 4 ? Close[0] - Close[5] : 0;
            features["price_change_10"] = CurrentBar > 9 ? Close[0] - Close[10] : 0;
            
            // Volatility
            double volatility20 = 0;
            if (CurrentBar > 19)
            {
                var returns = new List<double>();
                for (int i = 0; i < 19; i++)
                {
                    returns.Add((Close[i] - Close[i + 1]) / Close[i + 1]);
                }
                volatility20 = returns.Any() ? returns.Select(r => r * r).Average() : 0;
                volatility20 = Math.Sqrt(volatility20) * Math.Sqrt(252); // Annualized
            }
            features["volatility_20"] = volatility20;
            
            // Time features
            features["hour_of_day"] = timestamp.Hour;
            features["minute_of_hour"] = timestamp.Minute;
            features["day_of_week"] = (double)timestamp.DayOfWeek;
            
            return features;
        }
        
        /// <summary>
        /// Technical indicator features using NT built-in indicators
        /// </summary>
        private Dictionary<string, double> GetTechnicalIndicators()
        {
            var features = new Dictionary<string, double>();
            
            // EMA features - using NT's EMA indicator
            var ema9 = EMA(Close, 9);
            var ema21 = EMA(Close, 21);
            var ema50 = EMA(Close, 50);
            
            features["ema_9"] = ema9[0];
            features["ema_21"] = ema21[0];
            features["ema_50"] = ema50[0];
            features["ema_9_21_diff"] = ema9[0] - ema21[0];
            features["ema_21_50_diff"] = ema21[0] - ema50[0];
            features["price_to_ema9"] = Close[0] - ema9[0];
            features["price_to_ema21"] = Close[0] - ema21[0];
            
            // RSI - using NT's RSI indicator
            var rsi = RSI(Close, 14, 3);
            features["rsi_14"] = rsi[0];
            features["rsi_oversold"] = rsi[0] < 30 ? 1 : 0;
            features["rsi_overbought"] = rsi[0] > 70 ? 1 : 0;
            
            // Bollinger Bands - using NT's Bollinger indicator
            var bb = Bollinger(Close, 2, 20);
            features["bb_upper"] = bb.Upper[0];
            features["bb_middle"] = bb.Middle[0];
            features["bb_lower"] = bb.Lower[0];
            features["bb_width"] = bb.Upper[0] - bb.Lower[0];
            features["price_to_bb_upper"] = Close[0] - bb.Upper[0];
            features["price_to_bb_lower"] = Close[0] - bb.Lower[0];
            
            // ATR - using NT's ATR indicator
            var atr = ATR(Close, 14);
            features["atr_14"] = atr[0];
            features["atr_percentage"] = Close[0] > 0 ? (atr[0] / Close[0]) * 100 : 0;
            
            // MACD - using NT's MACD indicator
            var macd = MACD(Close, 12, 26, 9);
            features["macd"] = macd[0];
            features["macd_signal"] = macd.Avg[0];
            features["macd_histogram"] = macd.Diff[0];
            
            return features;
        }
        
        /// <summary>
        /// Market microstructure features
        /// </summary>
        private Dictionary<string, double> GetMarketMicrostructure()
        {
            var features = new Dictionary<string, double>();
            
            // Spread analysis
            double spread = High[0] - Low[0];
            double spreadPercentage = Close[0] > 0 ? (spread / Close[0]) * 100 : 0;
            features["spread"] = spread;
            features["spread_percentage"] = spreadPercentage;
            
            // Bar characteristics
            double bodySize = Math.Abs(Close[0] - Open[0]);
            double barRange = High[0] - Low[0];
            features["body_size"] = bodySize;
            features["body_ratio"] = barRange > 0 ? bodySize / barRange : 0;
            
            // Wick analysis
            double upperWick = Close[0] > Open[0] 
                ? High[0] - Close[0] 
                : High[0] - Open[0];
            double lowerWick = Close[0] > Open[0] 
                ? Open[0] - Low[0] 
                : Close[0] - Low[0];
            
            features["upper_wick"] = upperWick;
            features["lower_wick"] = lowerWick;
            features["upper_wick_ratio"] = barRange > 0 ? upperWick / barRange : 0;
            features["lower_wick_ratio"] = barRange > 0 ? lowerWick / barRange : 0;
            features["wick_imbalance"] = upperWick - lowerWick;
            
            // Price levels
            features["close_to_high"] = High[0] - Close[0];
            features["close_to_low"] = Close[0] - Low[0];
            features["high_low_ratio"] = Low[0] > 0 ? High[0] / Low[0] : 1;
            
            return features;
        }
        
        /// <summary>
        /// Volume analysis features
        /// </summary>
        private Dictionary<string, double> GetVolumeAnalysis()
        {
            var features = new Dictionary<string, double>();
            
            // Current volume
            features["volume"] = Volume[0];
            features["volume_delta"] = CurrentBar > 0 ? Volume[0] - Volume[1] : 0;
            
            // Volume averages
            double avgVolume5 = 0, avgVolume10 = 0, avgVolume20 = 0;
            if (CurrentBar >= 5)
            {
                for (int i = 0; i < 5; i++) avgVolume5 += Volume[i];
                avgVolume5 /= 5;
            }
            if (CurrentBar >= 10)
            {
                for (int i = 0; i < 10; i++) avgVolume10 += Volume[i];
                avgVolume10 /= 10;
            }
            if (CurrentBar >= 20)
            {
                for (int i = 0; i < 20; i++) avgVolume20 += Volume[i];
                avgVolume20 /= 20;
            }
            
            features["volume_sma_5"] = avgVolume5;
            features["volume_sma_10"] = avgVolume10;
            features["volume_sma_20"] = avgVolume20;
            features["volume_ratio_5"] = avgVolume5 > 0 ? Volume[0] / avgVolume5 : 1;
            features["volume_ratio_10"] = avgVolume10 > 0 ? Volume[0] / avgVolume10 : 1;
            features["volume_spike_ratio"] = avgVolume20 > 0 ? Volume[0] / avgVolume20 : 1;
            
            // Volume patterns
            features["volume_trend_5"] = CurrentBar >= 5 ? 
                (avgVolume5 - avgVolume10) / Math.Max(avgVolume10, 1) : 0;
            
            return features;
        }
        
        /// <summary>
        /// Pattern recognition features
        /// </summary>
        private Dictionary<string, double> GetPatternRecognition()
        {
            var features = new Dictionary<string, double>();
            
            // Candle patterns
            bool isBullish = Close[0] > Open[0];
            bool isBearish = Close[0] < Open[0];
            features["is_bullish_candle"] = isBullish ? 1 : 0;
            features["is_bearish_candle"] = isBearish ? 1 : 0;
            features["is_doji"] = Math.Abs(Close[0] - Open[0]) < (ATR(14)[0] * 0.1) ? 1 : 0;
            
            // Momentum
            if (CurrentBar >= 5)
            {
                double momentum5 = Close[0] - Close[5];
                features["momentum_5"] = momentum5;
                features["momentum_5_normalized"] = Close[5] > 0 ? momentum5 / Close[5] : 0;
            }
            
            if (CurrentBar >= 10)
            {
                double momentum10 = Close[0] - Close[10];
                features["momentum_10"] = momentum10;
                features["momentum_10_normalized"] = Close[10] > 0 ? momentum10 / Close[10] : 0;
            }
            
            // Price action patterns
            features["higher_high"] = CurrentBar > 0 && High[0] > High[1] ? 1 : 0;
            features["lower_low"] = CurrentBar > 0 && Low[0] < Low[1] ? 1 : 0;
            features["inside_bar"] = CurrentBar > 0 && 
                High[0] <= High[1] && Low[0] >= Low[1] ? 1 : 0;
            
            // Support/Resistance proximity (simplified)
            if (CurrentBar >= 20)
            {
                double recentHigh = MAX(High, 20)[0];
                double recentLow = MIN(Low, 20)[0];
                features["distance_to_high_20"] = recentHigh - Close[0];
                features["distance_to_low_20"] = Close[0] - recentLow;
                features["position_in_range_20"] = (recentHigh - recentLow) > 0 ? 
                    (Close[0] - recentLow) / (recentHigh - recentLow) : 0.5;
            }
            
            // Trajectory features based on recent price action
            var trajectoryFeatures = GetTrajectoryFeatures();
            foreach (var kvp in trajectoryFeatures) features[kvp.Key] = kvp.Value;
            
            return features;
        }
        
        /// <summary>
        /// Generate trajectory pattern features from recent price action
        /// These analyze recent price movement patterns to predict likely trajectory types
        /// </summary>
        private Dictionary<string, double> GetTrajectoryFeatures()
        {
            var features = new Dictionary<string, double>();
            
            // Initialize all trajectory features to 0
            features["pattern_v_recovery"] = 0.0;
            features["pattern_steady_climb"] = 0.0;
            features["pattern_failed_breakout"] = 0.0;
            features["pattern_whipsaw"] = 0.0;
            features["pattern_grinder"] = 0.0;
            features["traj_max_drawdown_norm"] = 0.0;
            features["traj_recovery_speed_norm"] = 0.0;
            features["traj_trend_strength_norm"] = 0.0;
            
            // Need at least 10 bars for trajectory analysis
            if (CurrentBar < 10) return features;
            
            try
            {
                // Analyze last 10 bars for trajectory patterns
                var recentPrices = new List<double>();
                var recentLows = new List<double>();
                var recentHighs = new List<double>();
                
                for (int i = 9; i >= 0; i--)
                {
                    recentPrices.Add(Close[i]);
                    recentLows.Add(Low[i]);
                    recentHighs.Add(High[i]);
                }
                
                // Calculate trajectory metrics
                double startPrice = recentPrices[0];
                double endPrice = recentPrices[recentPrices.Count - 1];
                double maxPrice = recentHighs.Max();
                double minPrice = recentLows.Min();
                double totalRange = maxPrice - minPrice;
                
                if (totalRange > 0 && startPrice > 0)
                {
                    // V-Recovery Pattern: Deep dip followed by strong recovery
                    double maxDrawdown = (startPrice - minPrice) / startPrice;
                    double recovery = (endPrice - minPrice) / (maxPrice - minPrice);
                    if (maxDrawdown > 0.02 && recovery > 0.7) // 2% drawdown, 70% recovery
                    {
                        features["pattern_v_recovery"] = Math.Min(1.0, maxDrawdown * recovery * 3);
                    }
                    
                    // Steady Climb Pattern: Consistent upward movement with low volatility
                    double priceChange = (endPrice - startPrice) / startPrice;
                    double volatility = CalculateVolatility(recentPrices);
                    if (priceChange > 0 && volatility < 0.01) // Positive move, low volatility
                    {
                        features["pattern_steady_climb"] = Math.Min(1.0, priceChange / volatility * 0.1);
                    }
                    
                    // Failed Breakout Pattern: Initial spike followed by retreat
                    int maxIndex = recentHighs.IndexOf(maxPrice);
                    if (maxIndex < 7 && endPrice < startPrice * 0.98) // Peak early, end below start
                    {
                        double failureStrength = (maxPrice - endPrice) / (maxPrice - startPrice);
                        features["pattern_failed_breakout"] = Math.Min(1.0, failureStrength);
                    }
                    
                    // Whipsaw Pattern: Multiple reversals, high volatility
                    int reversals = CountReversals(recentPrices);
                    if (reversals >= 3 && volatility > 0.015) // 3+ reversals, high volatility
                    {
                        features["pattern_whipsaw"] = Math.Min(1.0, (reversals - 2) * volatility * 50);
                    }
                    
                    // Grinder Pattern: Slow, choppy movement with small range
                    if (Math.Abs(priceChange) < 0.005 && volatility > 0.002) // Small move, some chop
                    {
                        features["pattern_grinder"] = Math.Min(1.0, volatility / Math.Max(Math.Abs(priceChange), 0.001));
                    }
                    
                    // Normalized trajectory metrics
                    features["traj_max_drawdown_norm"] = Math.Min(1.0, maxDrawdown * 20); // Normalize drawdown
                    features["traj_recovery_speed_norm"] = recovery; // Already 0-1
                    features["traj_trend_strength_norm"] = Math.Min(1.0, Math.Abs(priceChange) * 100); // Trend strength
                }
            }
            catch (Exception ex)
            {
                Print($"[TRAJECTORY] Error calculating trajectory features: {ex.Message}");
            }
            
            return features;
        }
        
        /// <summary>
        /// Calculate price volatility (coefficient of variation)
        /// </summary>
        private double CalculateVolatility(List<double> prices)
        {
            if (prices.Count < 2) return 0;
            
            double mean = prices.Average();
            double variance = prices.Select(p => Math.Pow(p - mean, 2)).Average();
            double stdDev = Math.Sqrt(variance);
            
            return mean > 0 ? stdDev / mean : 0;
        }
        
        /// <summary>
        /// Count price reversals in the series
        /// </summary>
        private int CountReversals(List<double> prices)
        {
            if (prices.Count < 3) return 0;
            
            int reversals = 0;
            for (int i = 1; i < prices.Count - 1; i++)
            {
                bool isLocalHigh = prices[i] > prices[i - 1] && prices[i] > prices[i + 1];
                bool isLocalLow = prices[i] < prices[i - 1] && prices[i] < prices[i + 1];
                
                if (isLocalHigh || isLocalLow)
                {
                    reversals++;
                }
            }
            
            return reversals;
        }
        
        /// <summary>
        /// Queue features and get Risk Agent approval
        /// </summary>
        public async Task<bool> QueueAndApprove(string entrySignalId, Dictionary<string, double> features, 
            string instrument, string direction, string entryType, int quantity = 1, double maxStopLoss = 50, double maxTakeProfit = 150)
        {
            var startTime = DateTime.Now;
            Print($"[RISK-AGENT] QueueAndApprove called for {entrySignalId}, direction: {direction}, type: {entryType}, maxSL: {maxStopLoss}, maxTP: {maxTakeProfit}");
            
            // Create pending feature set
            var timeframeMinutes = GetTimeframeMinutes();
            var pending = new PendingFeatureSet
            {
                EntrySignalId = entrySignalId,
                Features = features,
                Instrument = instrument,
                Direction = direction,
                EntryType = entryType,
                Timestamp = Time[0],
                TimeframeMinutes = timeframeMinutes,
                Quantity = quantity,
                MaxStopLoss = maxStopLoss,
                MaxTakeProfit = maxTakeProfit
            };
            
            // Add to queue
            pendingFeatures[entrySignalId] = pending;
            Print($"[RISK-AGENT] Added {entrySignalId} to pending features queue");
            
            try
            {
                // Send to Risk Agent for approval
                var riskRequest = new
                {
                    features = features,
                    instrument = instrument,
                    entryType = entryType,
                    direction = direction,
                    timestamp = Time[0],
                    timeframeMinutes = timeframeMinutes,
                    quantity = quantity,
                    maxStopLoss = maxStopLoss,
                    maxTakeProfit = maxTakeProfit
                };
                
                var json = JsonConvert.SerializeObject(riskRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await riskAgentHttpClient.PostAsync(riskAgentUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    Print($"[RISK-AGENT] Response: {responseJson}");
                    dynamic result = JsonConvert.DeserializeObject(responseJson);
                    
                    if (result?.approved == true)
                    {
                        // Store risk parameters in pending features - respect max limits
                        var riskSL = (double)(result.suggested_sl ?? Math.Min(10, maxStopLoss));
                        var riskTP = (double)(result.suggested_tp ?? Math.Min(20, maxTakeProfit));
                        var recPullback = (double)(result.rec_pullback ?? 10); // Default $10 soft floor
                        
                        pending.StopLoss = Math.Min(riskSL, maxStopLoss);
                        pending.TakeProfit = Math.Min(riskTP, maxTakeProfit);
                        pending.Confidence = (double)(result.confidence ?? 0.65);
                        pending.RecPullback = recPullback;
                        Print($"[RISK-AGENT] APPROVED {entrySignalId} with SL: {pending.StopLoss} (max: {maxStopLoss}), TP: {pending.TakeProfit} (max: {maxTakeProfit}), RecPullback: {pending.RecPullback}, Confidence: {pending.Confidence:F3}");
                        
                        var approvedDuration = (DateTime.Now - startTime).TotalMilliseconds;
                        Print($"[RISK-AGENT] QueueAndApprove COMPLETED (approved) for {entrySignalId} - Duration: {approvedDuration:F0}ms");
                        return true;
                    }
                    else
                    {
                        Print($"[RISK-AGENT] DENIED {entrySignalId}");
                        var deniedDuration = (DateTime.Now - startTime).TotalMilliseconds;
                        Print($"[RISK-AGENT] QueueAndApprove COMPLETED (denied) for {entrySignalId} - Duration: {deniedDuration:F0}ms");
                    }
                }
                else
                {
                    Print($"[RISK-AGENT] HTTP Error: {response.StatusCode}");
                    Print($"[RISK-AGENT] WARNING: Risk Agent unavailable - using default risk parameters");
                    
                    // Use default risk parameters when Risk Agent is unavailable - respect max limits
                    pending.StopLoss = Math.Min(10, maxStopLoss);
                    pending.TakeProfit = Math.Min(20, maxTakeProfit);
                    pending.RecPullback = 10; // Default $10 soft floor
                    
                    var httpErrorDuration = (DateTime.Now - startTime).TotalMilliseconds;
                    Print($"[RISK-AGENT] QueueAndApprove COMPLETED (http error/default) for {entrySignalId} - Duration: {httpErrorDuration:F0}ms");
                    return true; // Allow signal to proceed with defaults
                }
            }
            catch (Exception ex)
            {
                Print($"[RISK-AGENT] Exception: {ex.Message}");
                Print($"[RISK-AGENT] WARNING: Using default risk parameters due to error");
                
                // Use default risk parameters on error - respect max limits
                pending.StopLoss = Math.Min(10, maxStopLoss);
                pending.TakeProfit = Math.Min(20, maxTakeProfit);
                pending.RecPullback = 10; // Default $10 soft floor
                
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                Print($"[RISK-AGENT] QueueAndApprove COMPLETED (default/error) for {entrySignalId} - Duration: {duration:F0}ms");
                return true; // Allow signal to proceed with defaults
            }
            
            var finalDuration = (DateTime.Now - startTime).TotalMilliseconds;
            Print($"[RISK-AGENT] QueueAndApprove COMPLETED (rejected) for {entrySignalId} - Duration: {finalDuration:F0}ms");
			return false;
        }
        
        /// <summary>
        /// Check if features are pending for an entry signal
        /// </summary>
        public bool HasPendingFeatures(string entrySignalId)
        {
            return pendingFeatures.ContainsKey(entrySignalId);
        }
        
        /// <summary>
        /// Get pending features for an entry signal
        /// </summary>
        public PendingFeatureSet GetPendingFeatures(string entrySignalId)
        {
            pendingFeatures.TryGetValue(entrySignalId, out var features);
            return features;
        }
        
        /// <summary>
        /// Remove features from pending (called after position entry)
        /// </summary>
        public void RemovePendingFeatures(string entrySignalId)
        {
            pendingFeatures.TryRemove(entrySignalId, out _);
        }
        
        /// <summary>
        /// Clean up old pending features (housekeeping)
        /// </summary>
        public void CleanupOldPendingFeatures()
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-30); // Remove features older than 30 minutes
            var toRemove = pendingFeatures.Where(kvp => kvp.Value.Timestamp < cutoffTime).Select(kvp => kvp.Key).ToList();
            foreach (var key in toRemove)
            {
                pendingFeatures.TryRemove(key, out _);
            }
        }
    }
}