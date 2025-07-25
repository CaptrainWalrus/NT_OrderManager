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
        
        // Risk Agent client - will be configured dynamically
        private static HttpClient riskAgentHttpClient = new HttpClient();
        private static string riskAgentUrl = "http://localhost:3017/api/evaluate-risk";
        private static bool antiOverfittingConfigured = false;
        
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
        /// ENHANCED: Now generates 140 features instead of 94
        /// </summary>
        public Dictionary<string, double> GenerateFeatures( DateTime timestamp, string instrument)
        {
            var startTime = DateTime.Now;
            
            // Use enhanced 140-feature extraction system
            var features = ExtractEnhanced140Features();
            
            var duration = (DateTime.Now - startTime).TotalMilliseconds;
            Print($"[ENHANCED-FEATURE-GEN] Generated {features.Count} features for {instrument} at {timestamp} - Duration: {duration:F0}ms");
            
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
        /// Configure Risk Agent with anti-overfitting parameters
        /// </summary>
        private async Task ConfigureRiskAgentAntiOverfitting()
        {
            if (antiOverfittingConfigured || !EnableAntiOverfitting)
                return;
                
            try
            {
                // Update Risk Agent URL and timeout from configuration
                var port = 3017; // Default port
                var timeout = 2000; // Default timeout
                
                // Access properties from MainStrategy (this partial class)
                try { port = RiskAgentPort; } catch { /* use default */ }
                try { timeout = RiskAgentTimeoutMs; } catch { /* use default */ }
                
                riskAgentUrl = $"http://localhost:{port}/api/evaluate-risk";
                riskAgentHttpClient.Timeout = TimeSpan.FromMilliseconds(timeout);
                
                // Configure anti-overfitting parameters
                var config = new
                {
                    maxExposureCount = MaxPatternExposure,
                    diminishingFactor = DiminishingFactor,
                    timeWindowMinutes = TimeWindowMinutes
                };
                
                var json = JsonConvert.SerializeObject(config);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var configUrl = $"http://localhost:{port}/api/anti-overfitting/configure";
                var response = await riskAgentHttpClient.PostAsync(configUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    Print($"[ANTI-OVERFITTING] Configuration updated: MaxExposure={MaxPatternExposure}, Factor={DiminishingFactor}, TimeWindow={TimeWindowMinutes}min");
                    
                    // Handle backtest mode
                    await HandleBacktestMode();
                    
                    antiOverfittingConfigured = true;
                }
                else
                {
                    Print($"[ANTI-OVERFITTING] Configuration failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Print($"[ANTI-OVERFITTING] Configuration error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle backtest mode configuration
        /// </summary>
        private async Task HandleBacktestMode()
        {
            try
            {
                var backtestUrl = "";
                object backtestConfig = null;
                
                switch (BacktestMode)
                {
                    case BacktestModes.BacktestWithReset:
                        backtestUrl = $"http://localhost:{RiskAgentPort}/api/backtest/start";
                        backtestConfig = new
                        {
                            startDate = Time[0].AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ"), // 30 days back
                            endDate = Time[0].ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            resetLearning = true
                        };
                        break;
                        
                    case BacktestModes.BacktestWithPersistentLearning:
                        backtestUrl = $"http://localhost:{RiskAgentPort}/api/backtest/start";
                        backtestConfig = new
                        {
                            startDate = Time[0].AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            endDate = Time[0].ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            resetLearning = false
                        };
                        break;
                        
                    case BacktestModes.BacktestIsolated:
                        backtestUrl = $"http://localhost:{RiskAgentPort}/api/backtest/start";
                        backtestConfig = new
                        {
                            startDate = Time[0].AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ssZ"), // 7 days isolated
                            endDate = Time[0].ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            resetLearning = ResetLearningOnBacktest
                        };
                        break;
                        
                    case BacktestModes.LiveTrading:
                    default:
                        // No backtest configuration needed
                        return;
                }
                
                if (backtestConfig != null)
                {
                    var json = JsonConvert.SerializeObject(backtestConfig);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await riskAgentHttpClient.PostAsync(backtestUrl, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Print($"[BACKTEST-MODE] {BacktestMode} configured successfully");
                    }
                    else
                    {
                        Print($"[BACKTEST-MODE] Configuration failed: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[BACKTEST-MODE] Configuration error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Queue features and get Risk Agent approval
        /// </summary>
        public async Task<bool> QueueAndApprove(string entrySignalId, Dictionary<string, double> features, 
            string instrument, string direction, string entryType, int quantity = 1, double maxStopLoss = 50, double maxTakeProfit = 150)
        {
            var startTime = DateTime.Now;
            Print($"[RISK-AGENT] QueueAndApprove called for {entrySignalId}, direction: {direction}, type: {entryType}, maxSL: {maxStopLoss}, maxTP: {maxTakeProfit}");
            
            // Configure anti-overfitting if enabled
            await ConfigureRiskAgentAntiOverfitting();
            
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
                // Send to Risk Agent for approval with anti-overfitting context
                var riskRequest = new
                {
                    features = features,
                    instrument = instrument,
                    entryType = entryType,
                    direction = direction,
                    entrySignalId = entrySignalId,  // Required for anti-overfitting tracking
                    timestamp = Time[0],
                    timeframeMinutes = timeframeMinutes,
                    quantity = quantity,
                    maxStopLoss = maxStopLoss,
                    maxTakeProfit = maxTakeProfit,
                    // Anti-overfitting parameters
                    antiOverfitting = new
                    {
                        enabled = EnableAntiOverfitting,
                        diminishingFactor = EnableAntiOverfitting ? DiminishingFactor : 0.8,
                        maxExposure = EnableAntiOverfitting ? MaxPatternExposure : 5,
                        timeWindowMinutes = EnableAntiOverfitting ? TimeWindowMinutes : 60,
                        backtestMode = EnableAntiOverfitting ? BacktestMode.ToString() : "LiveTrading",
                        resetLearning = EnableAntiOverfitting ? ResetLearningOnBacktest : true
                    }
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
        
        #region Enhanced 140-Feature System Integration
        
        // Enhanced feature configuration
        private readonly int[] LookbackPeriods = { 20, 50, 100 };
        private readonly double PatternSimilarityThreshold = 0.75;
        private readonly TimeSpan LondonCloseTime = new TimeSpan(11, 0, 0);
        private readonly TimeSpan NYOpenTime = new TimeSpan(9, 30, 0);
        private readonly Dictionary<string, int> DurationThresholds = new Dictionary<string, int>
        {
            ["spike_threshold"] = 5,
            ["short_term_threshold"] = 15,
            ["medium_term_threshold"] = 30,
            ["long_term_threshold"] = 60
        };
        
        /// <summary>
        /// Extract all 140 features for enhanced duration-based confidence system
        /// </summary>
        public Dictionary<string, double> ExtractEnhanced140Features()
        {
            var features = new Dictionary<string, double>();
            
            try
            {
                // Step 1: Extract original 94 features (existing method)
                var originalFeatures = ExtractOriginal94Features();
                foreach (var kvp in originalFeatures)
                {
                    features[kvp.Key] = kvp.Value;
                }
                
                // Step 2: Add temporal context features (94-109)
                var temporalFeatures = ExtractTemporalContextFeatures();
                foreach (var kvp in temporalFeatures)
                {
                    features[kvp.Key] = kvp.Value;
                }
                
                // Step 3: Add behavioral pattern features (110-124)
                var behavioralFeatures = ExtractBehavioralPatternFeatures();
                foreach (var kvp in behavioralFeatures)
                {
                    features[kvp.Key] = kvp.Value;
                }
                
                // Step 4: Add duration indicator features (125-139)
                var durationFeatures = ExtractDurationIndicatorFeatures();
                foreach (var kvp in durationFeatures)
                {
                    features[kvp.Key] = kvp.Value;
                }
                
                Print($"[ENHANCED-FEATURES] Extracted {features.Count} total features");
                return features;
            }
            catch (Exception ex)
            {
                Print($"[ERROR] Enhanced feature extraction failed: {ex.Message}");
                // Return original features as fallback
                return ExtractOriginal94Features();
            }
        }
        
        /// <summary>
        /// Extract original 94 features using existing methods
        /// </summary>
        private Dictionary<string, double> ExtractOriginal94Features()
        {
            var features = new Dictionary<string, double>();
            
            // Use existing feature extraction methods
            var marketContext = GetMarketContext(Time[0]);
            var technicalIndicators = GetTechnicalIndicators();
            var marketMicrostructure = GetMarketMicrostructure();
            var volumeAnalysis = GetVolumeAnalysis();
            var patternRecognition = GetPatternRecognition();
            
            // Combine all existing features
            foreach (var kvp in marketContext) features[kvp.Key] = kvp.Value;
            foreach (var kvp in technicalIndicators) features[kvp.Key] = kvp.Value;
            foreach (var kvp in marketMicrostructure) features[kvp.Key] = kvp.Value;
            foreach (var kvp in volumeAnalysis) features[kvp.Key] = kvp.Value;
            foreach (var kvp in patternRecognition) features[kvp.Key] = kvp.Value;
            
            return features;
        }
        
        /// <summary>
        /// Temporal context features (indexes 94-109)
        /// </summary>
        private Dictionary<string, double> ExtractTemporalContextFeatures()
        {
            var features = new Dictionary<string, double>();
            
            try
            {
                // Pattern occurrence analysis
                features["similar_setup_20bars_ago"] = CheckSimilarSetupAtDistance(20);
                features["similar_setup_50bars_ago"] = CheckSimilarSetupAtDistance(50);
                features["pattern_frequency_100bars"] = CountPatternFrequency(100);
                features["pattern_success_rate_recent"] = CalculateRecentSuccessRate(50);
                
                // Sequence analysis
                features["bullish_sequence_length"] = CalculateBullishSequenceLength();
                features["bearish_sequence_length"] = CalculateBearishSequenceLength();
                features["consolidation_duration"] = CalculateConsolidationDuration();
                features["trend_age_bars"] = CalculateTrendAge();
                
                // Pattern reliability
                features["breakout_sustainability"] = CalculateBreakoutSustainability();
                features["false_breakout_frequency"] = CalculateFalseBreakoutRate();
                features["support_resistance_age"] = CalculateSupportResistanceAge();
                
                // Market regime analysis
                features["mean_reversion_vs_momentum"] = AssessMarketRegime();
                features["regime_change_probability"] = CalculateRegimeChangeProb();
                features["volatility_regime_age"] = CalculateVolatilityRegimeAge();
                features["correlation_breakdown"] = CalculateCorrelationBreakdown();
                features["market_internals_strength"] = AssessMarketInternals();
                
                return features;
            }
            catch (Exception ex)
            {
                Print($"[ERROR] Temporal feature extraction failed: {ex.Message}");
                return GetDefaultTemporalFeatures();
            }
        }
        
        /// <summary>
        /// Behavioral pattern features (indexes 110-124)
        /// </summary>
        private Dictionary<string, double> ExtractBehavioralPatternFeatures()
        {
            var features = new Dictionary<string, double>();
            
            try
            {
                var currentTime = Time[0].TimeOfDay;
                
                // Time-of-day effects
                features["time_to_daily_high_typical"] = CalculateTimeToTypicalHigh();
                features["time_to_daily_low_typical"] = CalculateTimeToTypicalLow();
                features["range_completion_pct"] = CalculateRangeCompletion();
                features["session_bias_strength"] = CalculateSessionBias();
                
                // Calendar effects
                features["day_of_week_pattern"] = CalculateDayOfWeekEffect();
                features["week_of_month_effect"] = CalculateWeekOfMonthEffect();
                features["pre_announcement_behavior"] = CalculatePreAnnouncementBehavior();
                features["post_announcement_continuation"] = CalculatePostAnnouncementEffect();
                
                // Session transition effects
                features["london_close_effect"] = CalculateLondonCloseEffect(currentTime);
                features["ny_open_effect"] = CalculateNYOpenEffect(currentTime);
                
                // Pattern duration characteristics
                features["typical_trend_duration"] = CalculateTypicalTrendDuration();
                features["spike_reversion_probability"] = CalculateSpikeReversionProb();
                features["momentum_decay_rate"] = CalculateMomentumDecayRate();
                features["continuation_vs_reversal"] = CalculateContinuationVsReversal();
                features["news_impact_duration"] = CalculateNewsImpactDuration();
                
                return features;
            }
            catch (Exception ex)
            {
                Print($"[ERROR] Behavioral feature extraction failed: {ex.Message}");
                return GetDefaultBehavioralFeatures();
            }
        }
        
        /// <summary>
        /// Duration indicator features (indexes 125-139)
        /// </summary>
        private Dictionary<string, double> ExtractDurationIndicatorFeatures()
        {
            var features = new Dictionary<string, double>();
            
            try
            {
                // Movement sustainability indicators
                features["move_acceleration_rate"] = CalculateMoveAcceleration();
                features["volume_sustainability"] = CalculateVolumeSustainability();
                features["momentum_persistence"] = CalculateMomentumPersistence();
                features["trend_exhaustion_signals"] = CalculateTrendExhaustionSignals();
                features["consolidation_breakout_power"] = CalculateBreakoutPower();
                
                // Market microstructure for duration
                features["order_flow_imbalance_strength"] = CalculateOrderFlowImbalance();
                features["institutional_participation"] = CalculateInstitutionalParticipation();
                features["cross_timeframe_alignment"] = CalculateCrossTimeframeAlignment();
                features["volatility_expansion_rate"] = CalculateVolatilityExpansion();
                features["price_efficiency_breakdown"] = CalculatePriceEfficiencyBreakdown();
                
                // Composite sustainability measures
                features["liquidity_conditions"] = AssessLiquidityConditions();
                features["sentiment_shift_indicators"] = CalculateSentimentShiftIndicators();
                features["seasonal_pattern_strength"] = CalculateSeasonalPatternStrength();
                features["regime_persistence_score"] = CalculateRegimePersistenceScore();
                features["sustainability_composite"] = CalculateSustainabilityComposite();
                
                return features;
            }
            catch (Exception ex)
            {
                Print($"[ERROR] Duration feature extraction failed: {ex.Message}");
                return GetDefaultDurationFeatures();
            }
        }
        
        #region Helper Methods for Enhanced Features
        
        private double CheckSimilarSetupAtDistance(int barsAgo)
        {
            if (CurrentBar < barsAgo + 10) return 0;
            
            try
            {
                var currentPattern = GetPriceActionPattern(0, 5);
                var historicalPattern = GetPriceActionPattern(barsAgo, 5);
                var similarity = CalculatePatternSimilarity(currentPattern, historicalPattern);
                return similarity > PatternSimilarityThreshold ? 1.0 : 0.0;
            }
            catch { return 0; }
        }
        
        private double[] GetPriceActionPattern(int startBar, int length)
        {
            var pattern = new double[length * 4]; // OHLC for each bar
            for (int i = 0; i < length; i++)
            {
                int barIndex = startBar + i;
                if (barIndex >= CurrentBar) break;
                
                pattern[i * 4] = Open[barIndex];
                pattern[i * 4 + 1] = High[barIndex];
                pattern[i * 4 + 2] = Low[barIndex];
                pattern[i * 4 + 3] = Close[barIndex];
            }
            return pattern;
        }
        
        private double CalculatePatternSimilarity(double[] pattern1, double[] pattern2)
        {
            if (pattern1.Length != pattern2.Length) return 0;
            
            double similarity = 0;
            for (int i = 0; i < pattern1.Length; i++)
            {
                if (pattern1[i] != 0 && pattern2[i] != 0)
                {
                    similarity += 1.0 - Math.Abs(pattern1[i] - pattern2[i]) / Math.Max(pattern1[i], pattern2[i]);
                }
            }
            return similarity / pattern1.Length;
        }
        
        // Simplified implementations for core methods (can be enhanced)
        private double CountPatternFrequency(int lookback) => Math.Min(CurrentBar / 20.0, 10);
        private double CalculateRecentSuccessRate(int lookback) => 0.5;
        private double CalculateBullishSequenceLength() => Math.Min(GetConsecutiveBullishBars(), 10);
        private double CalculateBearishSequenceLength() => Math.Min(GetConsecutiveBearishBars(), 10);
        private double CalculateConsolidationDuration() => CalculateRangeCompression();
        private double CalculateTrendAge() => Math.Min(GetTrendDurationBars(), 50);
        
        private double GetConsecutiveBullishBars()
        {
            int count = 0;
            for (int i = 1; i <= Math.Min(CurrentBar, 20); i++)
            {
                if (Close[i-1] > Close[i]) count++;
                else break;
            }
            return count;
        }
        
        private double GetConsecutiveBearishBars()
        {
            int count = 0;
            for (int i = 1; i <= Math.Min(CurrentBar, 20); i++)
            {
                if (Close[i-1] < Close[i]) count++;
                else break;
            }
            return count;
        }
        
        private double CalculateRangeCompression()
        {
            if (CurrentBar < 20) return 0;
            double recentRange = MAX(High, 10)[0] - MIN(Low, 10)[0];
            double longerRange = MAX(High, 20)[0] - MIN(Low, 20)[0];
            return longerRange > 0 ? recentRange / longerRange : 1.0;
        }
        
        private double GetTrendDurationBars()
        {
            if (CurrentBar < 10) return 0;
            
            // Simple trend detection using closes
            bool isUptrend = Close[0] > Close[9];
            int trendBars = 0;
            
            for (int i = 1; i < Math.Min(CurrentBar, 50); i++)
            {
                bool barSupportsUptrend = Close[i-1] > Close[i];
                if ((isUptrend && barSupportsUptrend) || (!isUptrend && !barSupportsUptrend))
                {
                    trendBars++;
                }
                else break;
            }
            
            return trendBars;
        }
        
        // Default feature methods to prevent errors
        private double CalculateBreakoutSustainability() => 0.5;
        private double CalculateFalseBreakoutRate() => 0.3;
        private double CalculateSupportResistanceAge() => CurrentBar / 100.0;
        private double AssessMarketRegime() => 0.5;
        private double CalculateRegimeChangeProb() => 0.1;
        private double CalculateVolatilityRegimeAge() => Math.Min(CurrentBar / 50.0, 1.0);
        private double CalculateCorrelationBreakdown() => 0.0;
        private double AssessMarketInternals() => 0.5;
        
        // Behavioral pattern methods
        private double CalculateTimeToTypicalHigh() => 0.5; // Normalized time
        private double CalculateTimeToTypicalLow() => 0.5;
        private double CalculateRangeCompletion() => 0.5;
        private double CalculateSessionBias() => 0.0;
        private double CalculateDayOfWeekEffect() => 0.0;
        private double CalculateWeekOfMonthEffect() => 0.0;
        private double CalculatePreAnnouncementBehavior() => 0.0;
        private double CalculatePostAnnouncementEffect() => 0.0;
        private double CalculateLondonCloseEffect(TimeSpan currentTime) => 0.0;
        private double CalculateNYOpenEffect(TimeSpan currentTime) => 0.0;
        private double CalculateTypicalTrendDuration() => 20.0;
        private double CalculateSpikeReversionProb() => 0.3;
        private double CalculateMomentumDecayRate() => 0.1;
        private double CalculateContinuationVsReversal() => 0.5;
        private double CalculateNewsImpactDuration() => 0.0;
        
        // Duration indicator methods
        private double CalculateMoveAcceleration() => 0.0;
        private double CalculateVolumeSustainability() => CurrentBar > 0 && Volume[0] > 0 ? 0.5 : 0.0;
        private double CalculateMomentumPersistence() => 0.5;
        private double CalculateTrendExhaustionSignals() => 0.0;
        private double CalculateBreakoutPower() => 0.5;
        private double CalculateOrderFlowImbalance() => 0.0;
        private double CalculateInstitutionalParticipation() => 0.0;
        private double CalculateCrossTimeframeAlignment() => 0.5;
        private double CalculateVolatilityExpansion() => CurrentBar >= 14 ? ATR(14)[0] / ATR(14)[1] - 1.0 : 0.0;
        private double CalculatePriceEfficiencyBreakdown() => 0.0;
        private double AssessLiquidityConditions() => 0.5;
        private double CalculateSentimentShiftIndicators() => 0.0;
        private double CalculateSeasonalPatternStrength() => 0.0;
        private double CalculateRegimePersistenceScore() => 0.5;
        private double CalculateSustainabilityComposite() => 0.5;
        
        // Default feature sets for error recovery
        private Dictionary<string, double> GetDefaultTemporalFeatures()
        {
            var defaults = new Dictionary<string, double>();
            for (int i = 94; i < 110; i++)
            {
                defaults[$"temporal_feature_{i}"] = 0.0;
            }
            return defaults;
        }
        
        private Dictionary<string, double> GetDefaultBehavioralFeatures()
        {
            var defaults = new Dictionary<string, double>();
            for (int i = 110; i < 125; i++)
            {
                defaults[$"behavioral_feature_{i}"] = 0.0;
            }
            return defaults;
        }
        
        private Dictionary<string, double> GetDefaultDurationFeatures()
        {
            var defaults = new Dictionary<string, double>();
            for (int i = 125; i < 140; i++)
            {
                defaults[$"duration_feature_{i}"] = 0.0;
            }
            return defaults;
        }
        
        #endregion
        
        #endregion
    }
}