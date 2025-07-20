using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.IO;
using System.Net.Http.Headers;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

// Helper extension for DateTime to Unix milliseconds conversion
public static class DateTimeExtensions
{
    public static long ToUnixTimeMilliseconds(this DateTime dateTime)
    {
        return (long)(dateTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    // CurvesV2 signal response format
    public class CurvesV2Response
    {
	    public bool success { get; set; }
	    public string instrument { get; set; }
	    public double bullScore { get; set; } // Match API field name and type
	    public double bearScore { get; set; } // Match API field name and type
	    public string contextId { get; set; }
	    public string timestamp { get; set; }
		
    }

    public class SignalData
    {
		public string contextId { get; set; }
        public int bull { get; set; }
        public int bear { get; set; }
        public List<PatternMatch> matches { get; set; }
        public string lastUpdated { get; set; }
        public string source { get; set; }
        public double avgSlope { get; set; }
        public int slopeImpact { get; set; }
    }

    public class PatternMatch
    {
        public string id { get; set; }
        public string patternName { get; set; }
        public string patternType { get; set; }
        public double confidence { get; set; }
        public double? entry { get; set; }
        public double? target { get; set; }
        public double? stop { get; set; }
    }

    public class TradeResult
    {
        public string pattern_id { get; set; }
        public long entry_time { get; set; }
        public double entry_price { get; set; }
        public long exit_time { get; set; }
        public double exit_price { get; set; }
        public double pnl { get; set; }
        public double pnl_points { get; set; }
        public string direction { get; set; }
        public string status { get; set; }
    }

    // Pattern performance timeline record for NinjaTrader to report trade outcomes
	   public class PatternPerformanceRecord
	{
	    // Core identifier (renamed from signalContextId for consistency)
	    public string contextId { get; set; }
	    
	    // Timestamps
	    public long timestamp_ms { get; set; }       // When record was created
	    public long bar_timestamp_ms { get; set; }   // Bar timestamp for decay
	    
	    // Performance metrics
	    public double maxGain { get; set; }          // Maximum gain during trade
	    public double maxLoss { get; set; }          // Maximum loss during trade
	    
	    // Direction
	    public bool isLong { get; set; }             // Position direction (true=long, false=short)
	    
	    // Optional: can pass the instrument explicitly
	    public string instrument { get; set; }       // Optional override instrument
	}

    public class BarData
    {
        public long timestamp { get; set; }
        public double open { get; set; }
        public double high { get; set; }
        public double low { get; set; }
        public double close { get; set; }
        public double volume { get; set; }
        public string timeframe { get; set; }
    }

        public class BarDataPacket
        {
            public string Instrument { get; set; }
            public DateTime Timestamp { get; set; }
            public double Open { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public double Close { get; set; }
            public double Volume { get; set; }
            public string Timeframe { get; set; }
        }
        
    // Add a new class for divergence response
    public class DivergenceResponse
    {
        public double divergenceScore { get; set; }
        public int barsSinceEntry { get; set; }
        public bool shouldExit { get; set; }
        public int consecutiveBars { get; set; }
        public int confirmationBarsRequired { get; set; }
        public double thompsonScore { get; set; } = 0.5; // Default neutral Thompson score
        public object components { get; set; }
    }

    // Add class for DTW match data
    public class DtwMatchData
    {
        public double score { get; set; }
        public double avgDistance { get; set; }
        public int validPeriods { get; set; }
    }

    // Dynamic matching configuration classes
    public class MatchingConfig
    {
        public double? ZScoreThreshold { get; set; }
        public bool? ReliabilityPenaltyEnabled { get; set; }
        public double? MaxThresholdPenalty { get; set; }
        public double? AtmosphericThreshold { get; set; }
        public CosineSimilarityThresholds CosineSimilarityThresholds { get; set; }
        
        // NEW: Risk Management Configuration
        public RiskManagementConfig RiskManagement { get; set; }
    }

    public class CosineSimilarityThresholds
    {
        public double? DefaultThreshold { get; set; }
        public double? EmaRibbon { get; set; }
        public double? SensitiveEmaRibbon { get; set; }
    }

    // NEW: Risk Management Configuration
    public class RiskManagementConfig
    {
        public double MaxTolerance { get; set; } = 100.0;        // $100 max stop
        public double DefaultStopPct { get; set; } = 0.60;       // 60% default
        public double DefaultPullbackPct { get; set; } = 0.20;   // 20% default
        public Dictionary<string, PatternRiskConfig> PatternPreferences { get; set; } = new Dictionary<string, PatternRiskConfig>();
        public ScalingFactors ScalingFactors { get; set; } = new ScalingFactors();
    }

    public class PatternRiskConfig
    {
        public double StopPct { get; set; }
        public double PullbackPct { get; set; }
        public bool ConfidenceScaling { get; set; } = true;
    }

    public class ScalingFactors
    {
        public double HighConfidenceBoost { get; set; } = 1.1;    // 10% looser stops for high confidence
        public double LowConfidencePenalty { get; set; } = 0.8;   // 20% tighter stops for low confidence
        public double HighConfluenceBoost { get; set; } = 1.2;    // 20% more patient exits for confluence
        public double LowConfluencePenalty { get; set; } = 0.8;   // 20% quicker exits for low confluence
    }

    // NEW: Signal Approval Service Classes
    public class SignalApprovalRequest
    {
        [JsonProperty("instrument")]
        public string Instrument { get; set; }
        
        [JsonProperty("direction")]
        public string Direction { get; set; }
        
        [JsonProperty("entry_price")]
        public double EntryPrice { get; set; }
        
        [JsonProperty("features")]
        public FeatureSet Features { get; set; }
        
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }

    public class FeatureSet
    {
        [JsonProperty("momentum_5")]
        public double Momentum5 { get; set; }
        
        [JsonProperty("price_change_pct_5")]
        public double PriceChangePct5 { get; set; }
        
        [JsonProperty("bb_width")]
        public double BbWidth { get; set; }
        
        [JsonProperty("bb_position")]
        public double BbPosition { get; set; }
        
        [JsonProperty("volume_spike_3bar")]
        public double VolumeSpike3Bar { get; set; }
        
        [JsonProperty("ema_spread_pct")]
        public double EmaSpreadPct { get; set; }
        
        [JsonProperty("rsi")]
        public double Rsi { get; set; }
        
        [JsonProperty("atr_pct")]
        public double AtrPct { get; set; }
        
        [JsonProperty("range_expansion")]
        public double RangeExpansion { get; set; }
    }

    public class SignalApprovalResponse
    {
        [JsonProperty("approved")]
        public bool Approved { get; set; }
        
        [JsonProperty("confidence")]
        public double Confidence { get; set; }
        
        [JsonProperty("reasons")]
        public string[] Reasons { get; set; }
        
        [JsonProperty("suggested_tp")]
        public double SuggestedTp { get; set; }
        
        [JsonProperty("suggested_sl")]
        public double SuggestedSl { get; set; }
        
        [JsonProperty("rec_pullback")]
        public double RecPullback { get; set; }
        
        [JsonProperty("max_contracts")]
        public int MaxContracts { get; set; }
        
        [JsonProperty("feature_scores")]
        public object FeatureScores { get; set; }
        
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }

    public class CurvesV2Service : IDisposable
    {
        public static string CurrentContextId { get; set; }
		public string sessionID;
        private readonly HttpClient client;
        private readonly string baseUrl;
        private readonly Action<string> logger;
        private bool disposed = false;
        private readonly object disposeLock = new object();
        private readonly OrganizedStrategy.CurvesV2Config config;
        
        // Pattern performance timeline tracking
        private readonly Dictionary<string, string> signalContextToEntryMap = new Dictionary<string, string>();
        private readonly object signalContextLock = new object();
        
        // Add missing fields
        private bool IsDisposed() => disposed;
        private bool IsShuttingDown() => disposed;
        private DateTime lastSignalCheck = DateTime.MinValue;
        private CancellationTokenSource signalPollCts;
        private int signalPollIntervalMs = 5000; // 5 seconds between polling
    
        // Add concurrent request tracking
        private int concurrentRequests = 0;
        private const int MAX_CONCURRENT_REQUESTS = 10;
        
        // Training mode flag - when true, skip RF exit calls during data collection
        private bool isTrainingMode = false;
    
        // WebSocket related fields
        private ClientWebSocket webSocket;
        private bool webSocketConnected = false;
        public int ErrorCounter = 0;
        private DateTime webSocketConnectStartTime = DateTime.MinValue;
        // Static properties for signal data
        		
		// Add this new class to represent Signal Pool responses
		public class SignalPoolResponse
		{
		    public bool success { get; set; }
		    public List<SignalPoolSignal> signals { get; set; }
		}
		
		public class SignalPoolSignal
		{
		    public string signalId { get; set; }
		    public string patternId { get; set; }
		    public string subtype { get; set; }
		    public string instrument { get; set; }
		    public string type { get; set; }
            public string direction { get; set; } // Added direction property to support signals with "long" or "short" directions
		    public long receivedTimestamp { get; set; }
		    public long expiryTimestamp { get; set; }
		    public long barTimestamp { get; set; }  // Add this field to match the Signal Pool JSON
		    public int barCount { get; set; }  // Add this field to match the Signal Pool JSON
		    public string patternType { get; set; }
			public bool isPurchased { get; set; }
			public double oppositionStrength { get; set; }
			public double confluenceScore { get; set; }
			public double effectiveScore { get; set;}
			public double rawScore { get; set;}
			public DtwMatchData dtwMatch { get; set; } // Add DTW match data
			
			// NEW: Dynamic risk management modifiers
			public double stopModifier { get; set; }    // Percentage of max tolerance for stop loss (0.45 = 45%)
			public double pullbackModifier { get; set; } // Percentage for pullback exits (0.20 = 20%)
			
			// Enhanced RF outputs for position sizing and risk management
			public int posSize { get; set; } = 1;    // Position size multiplier (1.55x = 155% of default)
			public double risk { get; set; } = 50.0;      // Risk amount in dollars ($55)
			public double target { get; set; } = 100.0;   // Target amount in dollars ($160)
			public double pullback { get; set; } = 15.0;  // Pullback percentage (15% = 0.15 as decimal)
		}
		
		// Add a new property to get Signal Pool URL
		private readonly string signalPoolUrl;
		public string lastSPJSON { get; private set; }
		public static string CurrentSubtype { get; private set; }
		public static long CurrentRequestTimestampEpoch { get; private set; }
		public static double CurrentBullStrength { get; private set; }
        public static double CurrentBearStrength { get; private set; }
		public static double CurrentOppositionStrength { get; private set; }
		public static double CurrentConfluenceScore { get; private set; }
		public static double CurrentEffectiveScore { get; private set; }
		public static double CurrentRawScore { get; private set; }
        public static double CurrentStopModifier { get; private set; }
        public static double CurrentPullbackModifier { get; private set; }
		
		// Enhanced RF outputs for position sizing and risk management
		public static int CurrentPosSize { get; private set; } = 1;
		public static double CurrentRisk { get; private set; } = 50.0;
		public static double CurrentTarget { get; private set; } = 100.0;
		public static double CurrentPullback { get; private set; } = 15.0;
		public static string CurrentPatternType { get; private set; }
        public static string PatternName { get; private set; }
        public static DateTime LastSignalTimestamp { get; private set; }
        public static double CurrentAvgSlope { get; private set; }
        public static int CurrentSlopeImpact { get; private set; }
        public static List<PatternMatch> CurrentMatches { get; private set; } = new List<PatternMatch>();
        public static bool SignalsAreFresh => (DateTime.Now - LastSignalTimestamp).TotalSeconds < 30;
        
        // Add DTW Service URL for divergence tracking
        private readonly string meServiceUrl = "http://localhost:5000"; // Use local ME service for orange line API
        
        // Storage Agent URL for direct data storage
        private readonly string storageUrl = "http://localhost:3015"; // Direct NT->Storage flow
        
        // Send outcome data directly to Storage Agent (bypassing ME service)
        public async Task<bool> SendOutcomeToStorageAsync(string entrySignalId, Dictionary<string, double> features, PositionOutcomeData outcomeData, string instrument, string direction, string entryType)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // DEBUG: Check what features we're getting
                int featureCount = features?.Count ?? 0;
                Log($"[STORAGE-DEBUG] SendOutcomeToStorage - Features: {featureCount}, EntrySignalId: {entrySignalId}");
                var payload = new
                {
                    entrySignalId = entrySignalId,
                    instrument = instrument,
                    direction = direction,
                    entryType = entryType,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    features = features,
                    outcome = new
                    {
                        pnl = outcomeData.PnLDollars,
                        pnlPoints = outcomeData.PnLPoints,
                        exitPrice = outcomeData.ExitPrice,
                        exitReason = outcomeData.ExitReason,
                        holdingBars = outcomeData.HoldingBars,
                        entryTime = outcomeData.EntryTime,
                        exitTime = outcomeData.ExitTime,
                        profitByBar = outcomeData.profitByBar // KEY: Include trajectory data
                    }
                };
                
                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                string endpoint = $"{storageUrl}/api/store-vector";
                
                var response = await client.PostAsync(endpoint, content);
                
                if (response.IsSuccessStatusCode)
                {
                    Log($"[STORAGE-DIRECT] Successfully sent outcome to Storage Agent: {entrySignalId}");
                    return true;
                }
                else
                {
                    Log($"[STORAGE-DIRECT] Failed to send outcome: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[STORAGE-DIRECT] Error sending outcome to Storage Agent: {ex.Message}");
                return false;
            }
        }
        
        // Divergence tracking properties
        public static double CurrentDivergenceScore { get; private set; } = 0;
        public static int CurrentBarsSinceEntry { get; private set; } = 0;
        public static bool CurrentShouldExit { get; private set; } = false;
        public static int CurrentConsecutiveBars { get; private set; } = 0;
        public static int CurrentConfirmationBarsRequired { get; private set; } = 3;
        
        // Connection status property
        public bool IsConnected => !disposed && client != null;

        // Backtest visualization service URL
        private readonly string visualizationUrl = "http://localhost:3007";
        private readonly HttpClient visualizationClient;

        // Add static property for the active pattern ID
        public static string CurrentPatternId { get; private set; }

        // Add divergence registry to track positions
        private readonly Dictionary<string, RegisteredPosition> activePositions = new Dictionary<string, RegisteredPosition>();
		
		public Dictionary<string,double> divergenceScores = new Dictionary<string,double>();
		
		// NEW: Separate dictionary for RF exit scores to avoid collisions
		public Dictionary<string,double> rfExitScores = new Dictionary<string,double>();
		
		        // Add Thompson score caching
        public Dictionary<string,double> thompsonScores = new Dictionary<string,double>();
        
        // Orange Line data caching
  
		
		// Add cache for async divergence requests to prevent duplicate HTTP calls
		private readonly Dictionary<string, Task<double>> pendingDivergenceRequests = new Dictionary<string, Task<double>>();
		private readonly object divergenceLock = new object();
		
		// Add error tracking
		private readonly Dictionary<string, int> positionErrorCounts = new Dictionary<string, int>();
		private readonly Dictionary<string, DateTime> positionErrorCooldowns = new Dictionary<string, DateTime>();
		private const int MAX_ERRORS_BEFORE_DEREGISTER = 5;
		private const int ERROR_COOLDOWN_SECONDS = 10;
		private const int MAX_BARS_TO_CHECK = 50; // Limit the number of bars to check for divergence

		public bool useRemoteService = false;
        // Add mapping for entry signal IDs to pattern IDs
        private readonly Dictionary<string, string> entrySignalToPatternId = new Dictionary<string, string>();
        
        // Reference to MainStrategy's MasterSimulatedStops collection for accurate position tracking
        private List<simulatedStop> MasterSimulatedStops;

        // Method to set the reference to MainStrategy's MasterSimulatedStops collection
        public void SetMasterSimulatedStops(List<simulatedStop> masterSimulatedStops)
        {
            this.MasterSimulatedStops = masterSimulatedStops;
        }

        // Thread-safe method to get pattern ID for an entry signal
        public bool TryGetPatternId(string entrySignalId, out string patternId)
        {
            lock (divergenceLock)
            {
                return entrySignalToPatternId.TryGetValue(entrySignalId, out patternId);
            }
        }

        // Class to represent a position in the divergence tracking system
        private class RegisteredPosition
        {
            public string PatternUuid { get; set; }
            public string Instrument { get; set; }
            public DateTime EntryTimestamp { get; set; }
            public int BarsSinceEntry { get; set; }
            public DateTime RegistrationTimestamp { get; set; }
        }

        // Add configuration for adaptive divergence
        private static double FallbackDivergenceThreshold = 15.0; // Match user's current threshold
        
        // Add flag to disable WebSocket for local testing
        private bool useWebSocketConnection = false; // Set to false for local HTTP-only mode
        
        // Method to check if adaptive divergence mode is likely enabled
        public static bool IsAdaptiveModeActive()
        {
            // If we have confirmation bars data, adaptive mode is likely active
            return CurrentConfirmationBarsRequired > 0 && CurrentConfirmationBarsRequired != 3;
        }
        
        // Method to set fallback threshold (can be called from NinjaTrader strategy)
        public static void SetFallbackDivergenceThreshold(double threshold)
        {
            FallbackDivergenceThreshold = threshold;
        }

        // Add heartbeat tracking
        public DateTime lastHeartbeat = DateTime.MinValue;
        private const int HEARTBEAT_INTERVAL_SECONDS = 30; // Send heartbeat every 30 seconds
        
        // Strategy state tracking for conditional sync/async behavior
        private bool isHistoricalMode = false;
        private readonly object stateLock = new object();

        private readonly object barSendLock = new object();

        // Constructor
        public CurvesV2Service(CurvesV2Config config, Action<string> logger = null)
        {
            this.config = config;
            this.baseUrl = config.GetSignalApiEndpoint();
            this.logger = logger ?? ((msg) => { NinjaTrader.Code.Output.Process(msg, PrintTo.OutputTab1); });
            this.disposed = false;
            
            // Initialize HttpClient with proper connection management
            var handler = new HttpClientHandler()
            {
                MaxConnectionsPerServer = 20, // Increase connection pool size
                UseProxy = false // Disable proxy for better performance
            };
            
            client = new HttpClient(handler);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(120); // Extended timeout for backfill operations
            client.DefaultRequestHeaders.ConnectionClose = false; // Keep connections alive
            
            // Initialize visualization client
            visualizationClient = new HttpClient();
            visualizationClient.DefaultRequestHeaders.Accept.Clear();
            visualizationClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            visualizationClient.Timeout = TimeSpan.FromSeconds(2); // Short timeout for visualization

			sessionID = Guid.NewGuid().ToString();
			CurrentContextId = null;
            
            // Initialize signalPoolUrl from config
            this.signalPoolUrl = config.GetSignalPoolApiEndpoint();
            
            Log("[INFO] CurvesV2Service (HTTP Refactor) Initialized.");
        }

        // Logging Helper
        private void Log(string message)
        {
            // Only log [DIVERGENCE] logs that are related to registration, and all non-DTW logs
            if (message.Contains("[DIVERGENCE]") && 
                (message.Contains("Registering position") || 
                 message.Contains("registered position") || 
                 message.Contains("ERROR: Invalid parameters for registration")))
        {
            logger?.Invoke($"CurvesV2 [HTTP]: {message}");
            }
            else if (!message.Contains("[DTW_SVC_") && 
                     !message.Contains("applyRollingZScore_ema_features") && 
                     !message.Contains("calculateEmaSpreads") && 
                     !message.Contains("calculateEmaRibbonWidth") && 
                     !message.Contains("calculateEmaRibbonCurvature"))
            {
                logger?.Invoke($"CurvesV2 [HTTP]: {message}");
            }
        }

        // Send unified record to Storage Agent
        public async Task<bool> SendToStorageAgent(UnifiedTradeRecord unifiedRecord, bool doNotStore = false, bool storeAsRecent = false)
        {
            if (IsDisposed()) return false;
            
            try
            {
                string storageUrl = "http://localhost:3015";
                
                // Three-state routing logic:
                // - DoNotStore=true → OUT_OF_SAMPLE (JSON files)
                // - DoNotStore=false, StoreAsRecent=true → RECENT (LanceDB)
                // - DoNotStore=false, StoreAsRecent=false → TRAINING (LanceDB)
                
                string dataType;
                string endpoint;
                
                if (doNotStore)
                {
                    // OUT_OF_SAMPLE: Send to live performance endpoint (JSON files)
                    dataType = "OUT_OF_SAMPLE";
                    endpoint = $"{storageUrl}/api/live-performance";
                }
                else if (storeAsRecent)
                {
                    // RECENT: Store in LanceDB for live graduation learning
                    dataType = "RECENT";
                    endpoint = $"{storageUrl}/api/store-vector";
                }
                else
                {
                    // TRAINING: Store in LanceDB as historical training data
                    dataType = "TRAINING";
                    endpoint = $"{storageUrl}/api/store-vector";
                }
                
                // Create dedicated storage client with shorter timeout for better performance
                using (var storageClient = new HttpClient())
                {
                    storageClient.Timeout = TimeSpan.FromSeconds(10); // Much shorter timeout for storage operations
                    
                    object storageRecord;
                    
                    if (dataType == "OUT_OF_SAMPLE")
                    {
                        // OUT_OF_SAMPLE: Simplified format for live performance tracking (JSON files)
                        storageRecord = new
                        {
                            instrument = unifiedRecord.Instrument,
                            entryType = unifiedRecord.EntryType,
                            pnl = unifiedRecord.PnLDollars,
                            pnlPerContract = unifiedRecord.PnLDollars, // Assuming 1 contract for now
                            timestamp = unifiedRecord.Timestamp,
                            exitReason = unifiedRecord.ExitReason
                        };
                    }
                    else
                    {
                        // TRAINING/RECENT: Full format for LanceDB storage
                        storageRecord = new
                        {
                            entrySignalId = unifiedRecord.EntrySignalId,
                            instrument = unifiedRecord.Instrument,
                            timestamp = unifiedRecord.Timestamp,
                            entryType = unifiedRecord.EntryType,
                            direction = unifiedRecord.Direction,
                            sessionId = unifiedRecord.SessionId,  // Include session ID for backtest separation
                            dataType = dataType,  // TRAINING or RECENT
                            features = unifiedRecord.Features,
                            recordType = "UNIFIED",  // Important: mark as unified record
                            status = "UNIFIED",      // Also set status
                            riskUsed = new
                            {
                                stopLoss = unifiedRecord.StopLoss,
                                takeProfit = unifiedRecord.TakeProfit
                            },
                            outcome = new
                            {
                                pnl = unifiedRecord.PnLDollars,
                                pnlPoints = unifiedRecord.PnLPoints,
                                holdingBars = unifiedRecord.HoldingBars,
                                exitReason = unifiedRecord.ExitReason,
                                maxProfit = unifiedRecord.MaxProfit,
                                maxLoss = unifiedRecord.MaxLoss,
                                wasGoodExit = unifiedRecord.WasGoodExit,
                                exitPrice = unifiedRecord.ExitPrice,
                                profitByBar = unifiedRecord.profitByBar
                            }
                        };
                    }
                
                var json = JsonConvert.SerializeObject(storageRecord, Formatting.Indented);
                
                // Log the payload we're sending
                //Log($"[UNIFIED-STORAGE] ========== PAYLOAD TO STORAGE AGENT ==========");
                //Log($"[UNIFIED-STORAGE] Endpoint: {endpoint}");
                //Log($"[UNIFIED-STORAGE] Payload JSON:");
               // Log(json);
                //Log($"[UNIFIED-STORAGE] ===========================================");
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
						Log($"[UNIFIED-PAYLOAD] {JsonConvert.SerializeObject(json)}");
					
                        //Log($"[UNIFIED-STORAGE] Sending POST request to {endpoint}");
                        var response = await storageClient.PostAsync(endpoint, content);
                    
                    //Log($"[UNIFIED-STORAGE] Response Status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                       // Log($"[UNIFIED-STORAGE] Success Response: {responseBody}");
                        Log($"[UNIFIED-STORAGE] Successfully stored {dataType} record for {unifiedRecord.EntrySignalId}");
                        return true;
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        Log($"[UNIFIED-STORAGE] Failed to store record - Status: {response.StatusCode}");
                        Log($"[UNIFIED-STORAGE] Error Response: {error}");
                        return false;
                    }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[UNIFIED-STORAGE] Error storing unified record: {ex.Message}");
                return false;
            }
        }

        // Send performance summary to storage agent when backtest completes
        public async Task<bool> SendPerformanceSummary(NinjaTrader.NinjaScript.StrategyBase strategy)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionID))
                {
                    Log("[PERFORMANCE-SUMMARY] No sessionID available - skipping performance summary");
                    return false;
                }

                Log($"[PERFORMANCE-SUMMARY] Sending performance summary for session {sessionID}");

                // Extract performance metrics from NinjaTrader's SystemPerformance
                var performance = strategy.SystemPerformance;
                var allTrades = performance.AllTrades;
                var tradesPerf = allTrades.TradesPerformance;
                
                var summary = new
                {
                    sessionId = sessionID,
                    timestamp = DateTimeToUnixMs(DateTime.Now),
                    instrument = strategy.Instrument.MasterInstrument.Name,
                    accountName = strategy.Account?.Name ?? "Backtest",
                    startTime = DateTimeToUnixMs(strategy.Time[strategy.BarsArray[0].Count - 1]),
                    endTime = DateTimeToUnixMs(strategy.Time[0]),
                    
                    // Core performance metrics
                    totalTrades = allTrades.Count,
                    winningTrades = allTrades.WinningTrades.Count,
                    losingTrades = allTrades.LosingTrades.Count,
                    winRate = allTrades.Count > 0 ? (double)allTrades.WinningTrades.Count / allTrades.Count : 0,
                    
                    // Financial metrics
                    netProfit = tradesPerf.NetProfit,
                    grossProfit = tradesPerf.GrossProfit,
                    grossLoss = tradesPerf.GrossLoss,
                    profitFactor = tradesPerf.ProfitFactor,
                    
               
                    avgTrade = allTrades.Count > 0 ? tradesPerf.NetProfit / allTrades.Count : 0,
                    
     
                    // Strategy parameters (if MainStrategy)
                    strategyParameters = strategy is MainStrategy mainStrategy ? new
                    {
                        takeProfitTicks = mainStrategy.microContractTakeProfit,
                        stopLossTicks = mainStrategy.microContractStoploss,
                        softTakeProfit = mainStrategy.softTakeProfitMult,
                        riskAgentConfidenceThreshold = mainStrategy.RiskAgentConfidenceThreshold
                    } : null
                };

                string jsonPayload = JsonConvert.SerializeObject(summary);
                string storageUrl = "http://localhost:3015";
                string endpoint = $"{storageUrl}/api/sessions/{sessionID}/performance";

                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Log($"[PERFORMANCE-SUMMARY] Successfully sent performance summary - Win Rate: {summary.winRate:P2}, Net Profit: ${summary.netProfit:F2}");
                        return true;
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        Log($"[PERFORMANCE-SUMMARY] Failed to send summary - Status: {response.StatusCode}, Error: {error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[PERFORMANCE-SUMMARY] Error sending performance summary: {ex.Message}");
                return false;
            }
        }

        // IDisposable Implementation
        public void Dispose()
        {
            lock (disposeLock)
            {
                if (disposed) return;
                try
                {
                    Log("[INFO] Disposing CurvesV2Service...");
                    
                    // Reset static data *before* disposing instance resources
                    Log("[INFO] Resetting static data during disposal...");
                    ResetStaticData(); 
                    
                    client?.Dispose();
                    visualizationClient?.Dispose();
                    disposed = true;
                    Log("[INFO] CurvesV2Service disposed.");
                    }
                    catch (Exception ex)
                    {
                    Log($"[ERROR] Error during service disposal: {ex.Message}");
                    disposed = true;
                }
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Converts DateTime to Unix timestamp (milliseconds)
        /// </summary>
        private long DateTimeToUnixMs(DateTime dateTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(dateTime.ToUniversalTime() - epoch).TotalMilliseconds;
        }

        // Health Check
        public async Task<bool> CheckHealthAsync()
        {
             lock(disposeLock) { if(disposed) return false; }

            try
            {
                Log("[INFO] Checking server health...");
                string healthEndpoint = $"{baseUrl}/health";
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var response = await client.GetAsync(healthEndpoint, timeoutCts.Token);
                    bool isHealthy = response.IsSuccessStatusCode;
                    response.Dispose();
                    Log($"[INFO] Health check result: {response.StatusCode} (Success: {isHealthy})");
                    return isHealthy;
                }
                }
            catch (TaskCanceledException)
            {
                bool wasDisposed; 
                lock(disposeLock) { wasDisposed = disposed; }
                if (!wasDisposed) {
                   Log($"[WARN] Health check timed out - service may be unavailable");
                }
                // In historical mode, return true to allow backtest to continue
                if (IsHistoricalMode())
                {
                    Log("[INFO] Historical mode: Continuing despite health check timeout");
                    return true;
                }
                return false;
            }
            catch (HttpRequestException httpEx)
            {
                bool wasDisposed; 
                lock(disposeLock) { wasDisposed = disposed; }
                if (!wasDisposed) {
                   Log($"[WARN] Health check failed - HTTP error: {httpEx.Message}");
                }
                // In historical mode, return true to allow backtest to continue
                if (IsHistoricalMode())
                {
                    Log("[INFO] Historical mode: Continuing despite HTTP error");
                    return true;
                }
                return false;
            }
                catch (Exception ex)
                {
                bool wasDisposed; 
                lock(disposeLock) { wasDisposed = disposed; }
                if (!wasDisposed) {
                   Log($"[ERROR] Health check failed: {ex.GetType().Name} - {ex.Message}");
                }
                // In historical mode, return true to allow backtest to continue
                if (IsHistoricalMode())
                {
                    Log("[INFO] Historical mode: Continuing despite health check error");
                    return true;
                }
                return false;
            }
        }

        // Heartbeat method to keep connections alive during 1-minute trading intervals
        public async Task<bool> SendHeartbeatAsync(bool useRemoteService = false)
        {
            lock(disposeLock) { if(disposed) return false; }

            try
            {
                // Build endpoint URLs for all services
                string miUrl = useRemoteService ? "https://curves-market-ingestion.onrender.com" : baseUrl;
                string meUrl = useRemoteService ? "https://matching-engine-service.onrender.com" : baseUrl;;
                string spUrl = useRemoteService ? "https://curves-signal-pool-service.onrender.com" : signalPoolUrl;

                var heartbeatTasks = new List<Task<bool>>();

                // Send heartbeat to all services concurrently
                heartbeatTasks.Add(SendHeartbeatToService(miUrl, "MI"));
                heartbeatTasks.Add(SendHeartbeatToService(meUrl, "ME"));
                heartbeatTasks.Add(SendHeartbeatToService(spUrl, "SP"));

                // Wait for all heartbeats to complete
                var results = await Task.WhenAll(heartbeatTasks);
                
                // Update last heartbeat time
                lastHeartbeat = DateTime.Now;
                
                // Return true if at least one service responded
                bool anySuccess = results.Any(r => r);
                return anySuccess;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        // Helper method to send heartbeat to individual service
        private async Task<bool> SendHeartbeatToService(string serviceUrl, string serviceName)
        {
            try
            {
                string endpoint = $"{serviceUrl}/health";
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    var response = await client.GetAsync(endpoint, timeoutCts.Token);
                    response.Dispose();
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                // Silently fail individual service heartbeats
                return false;
            }
        }

        // Synchronous heartbeat method
        // FIRE-AND-FORGET VERSION: SendHeartbeat - Non-blocking heartbeat
        public bool SendHeartbeat(bool useRemoteService)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // Fire-and-forget heartbeat in background
                Task.Run(async () => {
                    try
                    {
                        await SendHeartbeatAsync(useRemoteService);
                    }
                    catch (Exception ex)
                    {
                        // Silently handle heartbeat errors - don't let them affect trading
                    }
                });
                
                // Return success immediately (fire-and-forget)
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        // Check if heartbeat should be sent (called from strategy)
        public bool ShouldSendHeartbeat()
        {
            if (IsDisposed()) return false;
            
            var timeSinceLastHeartbeat = DateTime.Now - lastHeartbeat;
            return timeSinceLastHeartbeat.TotalSeconds >= HEARTBEAT_INTERVAL_SECONDS;
        }

        // Combined method to check and send heartbeat if needed - FIRE AND FORGET VERSION
        public bool CheckAndSendHeartbeat(bool useRemoteService)
        {
            if (!ShouldSendHeartbeat()) return true; // No heartbeat needed
            
            // Fire-and-forget heartbeat - do NOT block the main thread
            Task.Run(async () => {
                try 
                {
                    await SendHeartbeatAsync(useRemoteService);
                }
                catch (Exception ex)
                {
                    // Silently handle heartbeat errors - don't let them affect trading
                }
            });
            
            // Update last heartbeat time immediately to prevent rapid-fire calls
            lastHeartbeat = DateTime.Now;
            
            return true; // Always return true since we don't wait for completion
        }



     
        // Test connection to server
        public async Task<bool> TestConnectionAsync()
        {
            if (IsDisposed()) return false;

            try
            {
                var response = await client.GetAsync($"{baseUrl}/api/signals");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log($"Connection test failed: {ex.Message}");
                return false;
            }
        }

        // Send trade result to CurvesV2 server
        public async Task<bool> SendTradeResultAsync(string instrument, TradeResult tradeResult)
        {
            if (IsDisposed()) return false;

            try
            {
                string jsonPayload = JsonConvert.SerializeObject(tradeResult);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send to the trade results endpoint
                var endpoint = config.GetTradeResultEndpoint(instrument);
                var response = await client.PostAsync(endpoint, content);
                
                if (response.IsSuccessStatusCode)
                {
                    Log($"Trade result sent successfully for {instrument}, Pattern: {tradeResult.pattern_id}");
                    return true;
                }
                else
                {
                    Log($"Failed to send trade result: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error sending trade result: {ex.Message}");
                return false;
            }
        }

        // Send historical bars to CurvesV2 server
        public async Task<bool> SendHistoricalBarsAsync(List<object> bars, string instrument)
        {
            if (IsDisposed()) return false;

            try
            {
                string baseInstrument = instrument.Split(' ')[0];  // Extract ES or NQ
                const int batchSize = 25;  // Further reduced batch size to prevent overwhelming
                
                // Find min/max timestamps for logging
                DateTime? minTimestamp = null;
                DateTime? maxTimestamp = null;
                
                foreach (dynamic bar in bars)
                {
                    DateTime timestamp = bar.timestamp;
                    if (minTimestamp == null || timestamp < minTimestamp)
                        minTimestamp = timestamp;
                    if (maxTimestamp == null || timestamp > maxTimestamp)
                        maxTimestamp = timestamp;
                }
                
                Log($"Sending {bars.Count} historical bars for {baseInstrument} in batches of {batchSize}. " +
                    $"Timeframe: {minTimestamp?.ToString("yyyy-MM-dd HH:mm:ss")} to {maxTimestamp?.ToString("yyyy-MM-dd HH:mm:ss")}");
                
                // Create a dedicated HttpClient for IBI with longer timeout
                using (var ibiClient = new HttpClient())
                {
                    ibiClient.Timeout = TimeSpan.FromSeconds(60); // Increased timeout for file operations
                    ibiClient.DefaultRequestHeaders.Accept.Clear();
                    ibiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    
                    // SEQUENTIAL processing - wait for each batch to complete
                    for (int i = 0; i < bars.Count; i += batchSize)
                    {
                        // Get current batch
                        var batch = bars.Skip(i).Take(batchSize).ToList();
                        
                        // Restructure data to match server expectations
                        var data = new
                        {
                            instrument = baseInstrument,
                            bars = batch.Select(bar => {
                                dynamic b = bar;
                                return new {
                                    timestamp = (long)(b.timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds,
                                    high = (double)b.high,
                                    low = (double)b.low,
                                    open = (double)b.open,
                                    close = (double)b.close,
                                    volume = (int)b.volume
                                };
                            }).ToList()
                        };

                        var json = JsonConvert.SerializeObject(data);
                        
                        // HARDCODED URL for IBI Offline Analysis Tool
                        string endpoint = $"http://localhost:4002/api/historical/{baseInstrument}";
                        
                        Log($"Sending batch {(i/batchSize) + 1}/{(int)Math.Ceiling((double)bars.Count/batchSize)} ({batch.Count} bars) to IBI Analysis Tool");

                        var content = new StringContent(
                            json,
                            Encoding.UTF8,
                            "application/json"
                        );

                        // WAIT for each request to complete before sending the next
                        var response = await ibiClient.PostAsync(endpoint, content);
                        var responseContent = await response.Content.ReadAsStringAsync();
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            Log($"IBI Analysis Tool returned error: {response.StatusCode} - {responseContent}");
                            return false;
                        }
                        
                        // Parse response for status information
                        try
                        {
                            dynamic responseObj = JsonConvert.DeserializeObject(responseContent);
                            if (responseObj != null)
                            {
                                Log($"Batch {(i/batchSize) + 1} accepted: {responseObj.message}");
                            }
                        }
                        catch
                        {
                            // Ignore parsing errors, just continue
                        }
                        
                        // LONGER delay between batches to ensure file operations complete
                        if (i + batchSize < bars.Count) // Don't delay after the last batch
                        {
                            await Task.Delay(1000); // Increased to 1 second between batches
                        }
                        
                        // Log progress every 10 batches
                        if ((i / batchSize) % 10 == 0 && i > 0)
                        {
                            Log($"Sent {i} of {bars.Count} bars...");
                        }
                    }
                }
                
                Log($"Successfully sent all {bars.Count} historical bars for {baseInstrument} to IBI Analysis Tool");
                
                // Wait longer before triggering profile generation to ensure all file writes are complete
                Log($"Waiting 5 seconds for file operations to complete...");
                await Task.Delay(5000);
                
                // Automatically trigger profile generation if we sent enough data
                if (bars.Count >= 1000)
                {
                    Log($"Triggering IBI profile generation for {baseInstrument}...");
                    var generateEndpoint = $"http://localhost:4002/api/generate-profile/{baseInstrument}";
                    
                    using (var generateClient = new HttpClient())
                    {
                        generateClient.Timeout = TimeSpan.FromSeconds(120); // Very long timeout for profile generation
                        var generateResponse = await generateClient.PostAsync(generateEndpoint, new StringContent("", Encoding.UTF8, "application/json"));
                        
                        if (generateResponse.IsSuccessStatusCode)
                        {
                            var profileContent = await generateResponse.Content.ReadAsStringAsync();
                            Log($"IBI Profile generated successfully: {profileContent}");
                        }
                        else
                        {
                            var errorContent = await generateResponse.Content.ReadAsStringAsync();
                            Log($"Failed to generate IBI profile: {generateResponse.StatusCode} - {errorContent}");
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to send historical bars to IBI Analysis Tool: {ex.Message}");
                return false;
            }
        }
		
        // Update the SendBar method to use WebSocket for backtest if available
        public bool SendBar(bool UseRemoteService, string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, bool isHistorical = false, string timeframe = "1m")
        {
            // For compatibility, simply delegate to our new methods
            return SendBarFireAndForget(UseRemoteService, instrument, timestamp, open, high, low, close, volume, timeframe);
        }

		/// send currentbars for Qdrant
        // Ultra-simple bar sender that TRULY never blocks - MODIFIED FOR DEBUG: ALWAYS USE HTTP
        public bool SendBarFireAndForget(bool useRemoteService, string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m", string serviceName = "SendBarFireAndForget")
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
		    return false;
           
            // Check if we're at the concurrent request limit
            if (concurrentRequests >= MAX_CONCURRENT_REQUESTS)
            {
                // Log when we hit the limit to help diagnose connection pool exhaustion
                Log($"[CONNECTION POOL] Hit max concurrent requests ({MAX_CONCURRENT_REQUESTS}) - dropping request for {instrument} [SERVICE: {serviceName}]");
                return false;
            }
                
            try
            {
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
				CurrentRequestTimestampEpoch = epochMs;

                // Build the URL for the endpoint
				string microServiceUrl = useRemoteService == true ? "https://curves-market-ingestion.onrender.com" : baseUrl;
				string endpoint = $"{microServiceUrl}/api/ingest/bars/{instrument}";
                
                // Create payload
                var payloadObject = new {
                     strategyId = sessionID,  // NEW: Add sessionID as strategyId
                     timestamp = epochMs,
                     open = open,
                     high = high,
                     low = low,
                     close = close,
                     volume = volume,
                     timeframe = timeframe,
                     symbol = instrument
                };
                
                string jsonPayload = JsonConvert.SerializeObject(payloadObject);
                
                // Increment concurrent request counter
                Interlocked.Increment(ref concurrentRequests);
                
                // Diagnostic logging for connection monitoring
                if (concurrentRequests > MAX_CONCURRENT_REQUESTS / 2)
                {
                    Log($"[CONNECTIONS] Active: {concurrentRequests}/{MAX_CONCURRENT_REQUESTS}");
                }
                
                // Create the task but with a safety timeout wrapper
                var sendTask = Task.Run(async () => {
                            try
                            {
                                if (IsDisposed() || IsShuttingDown()) return;
                        
                        using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) // 3 second timeout per request
                        {
                            // Fire and forget with timeout
                            var response = await client.PostAsync(endpoint, content, cts.Token);
							
                            response.Dispose(); // Important: dispose the response to free the connection
                        }
                    }
                    catch (TaskCanceledException)
                                            {
                        // This is expected when requests timeout - don't log
                                        }
                                        catch (Exception ex)
                                        {
                        if (!IsDisposed() && !IsShuttingDown())
                        {
                            Log($"[DEBUG HTTP] SendBarFireAndForget: Error for {instrument}: {ex.Message}");
                                        }
                            }
                    finally
                            {
                        // Always decrement the counter
                        Interlocked.Decrement(ref concurrentRequests);
                            }
                });
                
                // Add a safety timeout to prevent indefinite hangs
                Task.Run(async () => {
                    await Task.Delay(5000); // Wait 5 seconds
                    if (!sendTask.IsCompleted)
                    {
                        Log($"[ERROR] Bar send operation timed out after 5s - possible connection pool exhaustion for {instrument}");
                        // Force decrement if the task is hung
                        Interlocked.Decrement(ref concurrentRequests);
                            }
                });
                    
                    return true;
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref concurrentRequests); // Ensure we decrement on exception
                Log($"[DEBUG HTTP] SendBarFireAndForget: Unexpected error for {instrument}: {ex.Message}");
                return false;
            }
        }
		


		// Simplified CheckSignalsFireAndForget method - just gets and stores bull/bear values
		public double CheckSignalsFireAndForget(bool useRemoteService, DateTime dt, string instrument, int? pattern_size = null, double? minRawScore = null, double? effectiveScoreRequirement = null, string patternId = null, string subtype = null)
		{
		    if (IsDisposed() || string.IsNullOrEmpty(instrument) || client == null)
		        return 0;
			
			// Check concurrent request limit
			if (concurrentRequests >= MAX_CONCURRENT_REQUESTS)
				return 0;

			double score = 0;
			
			if(useRemoteService == false)
			{
				// Reset values immediately (optimistic)
				CurrentOppositionStrength = 0;
				CurrentConfluenceScore = 0;
				CurrentRawScore = 0;
				CurrentEffectiveScore = 0;
				CurrentPatternId = null;
			}
			try
			{
				// Pre-build endpoint once (avoid StringBuilder overhead)
				string microServiceUrl = useRemoteService ? "https://curves-signal-pool-service.onrender.com" : signalPoolUrl;
				string endpoint = $"{microServiceUrl}/api/signals/available?strategyId={sessionID}&instrument={instrument}";
				
				// Increment counter and fire-and-forget
				Interlocked.Increment(ref concurrentRequests);
				//Log($" Requested {endpoint}");
				// TRUE fire-and-forget - no blocking
				var checkTask = Task.Run(async () => {
					HttpResponseMessage response = null;
					try
					{
						if (IsDisposed() || IsShuttingDown()) return;
						
						using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) // Reduced timeout
						{
							response = await client.GetAsync(endpoint, timeoutCts.Token);
						}

						if (response.IsSuccessStatusCode)
						{
							
							string responseText = await response.Content.ReadAsStringAsync();
							var signalPoolResponse = JsonConvert.DeserializeObject<SignalPoolResponse>(responseText);
							lastSPJSON = responseText;
						
							if (signalPoolResponse?.success == true && signalPoolResponse.signals?.Count > 0) 
							{
								
								// Quick categorization without complex LINQ
								double bullScore = 0, bearScore = 0;
								var firstSignal = signalPoolResponse.signals[0];
								
								foreach (var signal in signalPoolResponse.signals)
								{
									bool isBull = signal.type == "bull" || 
												 (signal.direction?.ToLower() == "long") || 
												 (signal.type?.ToLower().Contains("bull") == true);
									
									//double effectiveScore = Math.Max(signal.effectiveScore, signal.rawScore) * 100;
									
									if (isBull)
										score = signal.rawScore;
									else if (!isBull)
										score = -signal.rawScore;
								}
								
								// Update static properties atomically
								CurrentBullStrength = bullScore;
								CurrentBearStrength = bearScore;
								CurrentPatternType = firstSignal.patternType;
								CurrentSubtype = firstSignal.subtype;
								CurrentPatternId = firstSignal.patternId;
							
								// Safely get max values with default of 0 if no signals
								CurrentOppositionStrength = signalPoolResponse.signals.Any() ? signalPoolResponse.signals.Max(s => s.oppositionStrength) : 0;
								CurrentConfluenceScore = signalPoolResponse.signals.Any() ? signalPoolResponse.signals.Max(s => s.confluenceScore) : 0;
								CurrentEffectiveScore = signalPoolResponse.signals.Any() ? signalPoolResponse.signals.Max(s => s.effectiveScore) : 0;
								CurrentStopModifier = signalPoolResponse.signals.Any() ? signalPoolResponse.signals.Max(s => s.stopModifier) : 0;
                                CurrentPullbackModifier = signalPoolResponse.signals.Any() ? signalPoolResponse.signals.Max(s => s.pullbackModifier) : 0;
                                CurrentRawScore = signalPoolResponse.signals.Any() ? signalPoolResponse.signals.Max(s => s.rawScore) : 0;
                                
								LastSignalTimestamp = DateTime.UtcNow;
								CurrentContextId = "signal-" + DateTime.Now.Ticks;
							}
							else
							{
								//Log($" Request Not Success {endpoint}");
								// Reset on no signals
								CurrentPatternType = null;
								CurrentSubtype = null;
								CurrentPatternId = null;
								CurrentBullStrength = 0;
								CurrentBearStrength = 0;
							}
						}
					}
					catch (TaskCanceledException) 
					{
						// Expected for timeouts - don't log
					}
					catch (Exception ex)
					{
						if (!IsDisposed() && !IsShuttingDown())
						{
							Log($"[ERROR] Checking signals for {instrument}: {ex.Message}");
							if (ErrorCounter < 11)
								ErrorCounter++;
						}
					}
					finally
					{
						response?.Dispose();
						Interlocked.Decrement(ref concurrentRequests);
					}
				});
				
				// Add a safety timeout to prevent indefinite hangs
				Task.Run(async () => {
					await Task.Delay(5000); // Wait 5 seconds
					if (!checkTask.IsCompleted)
					{
						Log($"[ERROR] Signal check operation timed out after 5s - possible connection pool exhaustion");
						// Force decrement if the task is hung
						Interlocked.Decrement(ref concurrentRequests);
					}
				});
				
				return score;
			}
			catch (Exception ex)
			{
				Interlocked.Decrement(ref concurrentRequests);
				Log($"[ERROR] CheckSignalsFireAndForget setup error: {ex.Message}");
				return 0;
			}
		}

		// NEW: Synchronous version that blocks and returns enhanced signal data for BuildNewSignal
		public (double score, int posSize, double risk, double target, double pullback) CheckSignalsSync(bool useRemoteService, DateTime dt, string instrument, double? minRawScore = null, double? effectiveScoreRequirement = null)
		{
		
		    if (IsDisposed() || string.IsNullOrEmpty(instrument) || client == null)
			{
		        return (0, 0, 0, 0, 0);
			}
			// Check concurrent request limit
			if (concurrentRequests >= MAX_CONCURRENT_REQUESTS)
			{
				
				return (0, 0, 0, 0, 0);
			}
			try
			{
				// Build endpoint with better filtering
				string microServiceUrl = useRemoteService ? "https://curves-signal-pool-service.onrender.com" : signalPoolUrl;
				string endpoint = $"{microServiceUrl}/api/signals/available?strategyId={sessionID}&instrument={instrument}";
				
				// Add minimum score filter if provided
				if (minRawScore.HasValue)
					endpoint += $"&minScore={minRawScore.Value}";
			
				// Synchronous HTTP request with timeout
				using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) // Increased from 2 to 5 seconds
				{
					try
					{
						var response = client.GetAsync(endpoint, timeoutCts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
						
						if (response.IsSuccessStatusCode)
						{
							LastSignalTimestamp = DateTime.UtcNow;
							string responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
							var signalPoolResponse = JsonConvert.DeserializeObject<SignalPoolResponse>(responseText);

							if (signalPoolResponse?.success == true && signalPoolResponse.signals?.Count > 0)
							{
								
								
								// Find the best signal by effective score
								SignalPoolSignal bestSignal = null;
								double bestScore = 0;
								
								foreach (var signal in signalPoolResponse.signals)
								{
								
									// Skip purchased signals
									if (signal.isPurchased) continue;
									
									// Use effective score or raw score, whichever is higher
									double signalScore = Math.Max(signal.effectiveScore, signal.rawScore);
									
									// Apply minimum score filter
									if (effectiveScoreRequirement.HasValue && signalScore < effectiveScoreRequirement.Value)
									{
										
										continue;
									}
									if (Math.Abs(signalScore) > Math.Abs(bestScore))
									{
										bestScore = signalScore;
										bestSignal = signal;
									}
								}
								
								if (bestSignal != null)
								{
									// Update static properties with best signal
									CurrentPatternType = bestSignal.patternType;
									CurrentSubtype = bestSignal.subtype;
									CurrentPatternId = bestSignal.patternId;
									CurrentOppositionStrength = bestSignal.oppositionStrength;
									CurrentConfluenceScore = bestSignal.confluenceScore;
									CurrentEffectiveScore = bestSignal.effectiveScore;
									CurrentRawScore = bestSignal.rawScore;
									CurrentStopModifier = bestSignal.stopModifier;
									CurrentPullbackModifier = bestSignal.pullbackModifier;
									// Enhanced RF outputs
									CurrentPosSize = bestSignal.posSize;
									CurrentRisk = bestSignal.risk;
									CurrentTarget = bestSignal.target;
									CurrentPullback = bestSignal.pullback;
									LastSignalTimestamp = DateTime.UtcNow;
									CurrentContextId = "signal-" + DateTime.Now.Ticks;
									
									// Determine direction and return signed score
									bool isBull = bestSignal.type == "bull" || (bestSignal.type == "neutral" && bestSignal.direction == "long");
									bool isBear = bestSignal.type == "bear" || (bestSignal.type == "neutral" && bestSignal.direction == "short");
									
									double finalScore = Math.Max(bestSignal.effectiveScore, bestSignal.rawScore);
									
									// Handle RF neutral signals based on direction
									if (bestSignal.type == "neutral" && bestSignal.patternType == "RF_FALLBACK")
									{
										
										// For RF neutral signals, use the direction field
										if (bestSignal.direction == "long" || bestSignal.direction == "BUY")
										{
											CurrentBullStrength = 0.5; // Default score for neutral RF signals
											CurrentBearStrength = 0;
											if (IsHistoricalMode())
											{
												//Log($"[SYNC-SIGNALS] RF NEUTRAL BULL signal: Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
											}
										
											return (0.5, CurrentPosSize, CurrentRisk, CurrentTarget, CurrentPullback); // Positive for bullish
										}
										else if (bestSignal.direction == "short" || bestSignal.direction == "SELL")
										{
											CurrentBullStrength = 0;
											CurrentBearStrength = 0.5; // Default score for neutral RF signals
											if (IsHistoricalMode())
											{
												//Log($"[SYNC-SIGNALS] RF NEUTRAL BEAR signal: Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
											}
										
											return (-0.5, CurrentPosSize, CurrentRisk, CurrentTarget, CurrentPullback); // Negative for bearish
										}
										else
										{
											// True neutral - no direction
											if (IsHistoricalMode())
											{
												//Log($"[SYNC-SIGNALS] RF NEUTRAL signal with no direction: Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
											}
											
											return (0, CurrentPosSize, CurrentRisk, CurrentTarget, CurrentPullback);
										}
									}
									else if (isBull)
									{
										CurrentBullStrength = finalScore;
										CurrentBearStrength = 0;
						
										//Log($"[SYNC-SIGNALS] BULL signal: Score={finalScore:F4}, Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
										return (finalScore, CurrentPosSize, CurrentRisk, CurrentTarget, CurrentPullback); // Positive for bull
									}
									else if (isBear)
									{
										CurrentBullStrength = 0;
										CurrentBearStrength = finalScore;
							
										//Log($"[SYNC-SIGNALS] BEAR signal: Score={finalScore:F4}, Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
										return (-finalScore, CurrentPosSize, CurrentRisk, CurrentTarget, CurrentPullback); // Negative for bear
									}
									else
									{
										// Unknown signal type
										if (IsHistoricalMode())
										{
											//Log($"[SYNC-SIGNALS] Unknown signal type: {bestSignal.type}, Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
										}
									
										return (0, CurrentPosSize, CurrentRisk, CurrentTarget, CurrentPullback);
									}
								}
								else
								{
									//Log($"[SYNC-SIGNALS] bestSignal IS NULL ********* {responseText}");
								}
							}
							else
							{
								//Log($"[FAIL-SYNC-SIGNALS] Requesting: {endpoint}, signalPoolResponse?.success = {signalPoolResponse?.success} && signalPoolResponse.signals?.Count = {signalPoolResponse?.signals?.Count ?? 0}");
								
							}
							
							// No valid signals found
							if (IsHistoricalMode() && signalPoolResponse?.signals?.Count > 0)
							{
								//Log($"[SYNC-SIGNALS] No valid signals after filtering. Total signals: {signalPoolResponse.signals?.Count ?? 0}, minScore filter: {effectiveScoreRequirement}");
							}
							ResetCurrentSignalProperties();
							return (0, 0, 0, 0, 0);
						}
						else
						{
							if (IsHistoricalMode())
							{
								string errorContent = response.Content?.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult() ?? "No content";
								//Log($"[SYNC-SIGNALS] HTTP error: {response.StatusCode}, Content: {errorContent}");
							}
							return (0, 0, 0, 0, 0);
						}
					}
					catch (TaskCanceledException)
					{
						// Timeout occurred - this is normal in historical mode when service is unavailable
						// Don't log this repeatedly to avoid spam
						return (0, 0, 0, 0, 0);
					}
				}
			}
			catch (TaskCanceledException)
			{
				// Timeout - expected in historical mode, don't log
				return (0, 0, 0, 0, 0);
			}
			catch (HttpRequestException httpEx)
			{
				// Network error - log only once per session
				if (ErrorCounter < 1)
				{
					//Log($"[SYNC-SIGNALS] Network error (first occurrence): {httpEx.Message}");
					ErrorCounter++;
				}
				return (0, 0, 0, 0, 0);
			}
			catch (Exception ex)
			{
				// Only log unexpected errors
				if (!ex.Message.Contains("task was canceled"))
				{
					//Log($"[SYNC-SIGNALS] Error: {ex.Message}");
				}
				return (0, 0, 0, 0, 0);
			}
		}
		
		// Helper method to reset signal properties
		private void ResetCurrentSignalProperties()
		{
			CurrentPatternType = null;
			CurrentSubtype = null;
			CurrentPatternId = null;
			CurrentBullStrength = 0;
			CurrentBearStrength = 0;
			CurrentOppositionStrength = 0;
			CurrentConfluenceScore = 0;
			CurrentEffectiveScore = 0;
			CurrentRawScore = 0;
			CurrentStopModifier = 0;
			CurrentPullbackModifier = 0;
			CurrentPosSize = 1;
			CurrentRisk = 50.0;
			CurrentTarget = 100.0;
			CurrentPullback = 15.0;
		}

		
		
        // Add synchronous methods for direct NinjaTrader integration
        
        // Synchronously send a bar and wait for confirmation
        public bool SendBarSync(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            lock (barSendLock)
            {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
            {
                return false;
            }
            
            try
            {
                    // SIMPLIFIED: In historical mode, send via HTTP synchronously with minimal logging
                    if (IsHistoricalMode())
                    {
                        // Create bar data object
                        var barData = new
                        {
                            strategyId = sessionID,  // Add sessionID as strategyId
                            timestamp = DateTimeToUnixMs(timestamp),
                            open = open,
                            high = high,
                            low = low,
                            close = close,
                            volume = volume,
                            timeframe = timeframe,
                            symbol = instrument
                        };
                        
                        // Send synchronously via HTTP with very short timeout
                        string jsonPayload = JsonConvert.SerializeObject(barData);
						if (IsHistoricalMode() && DateTime.Now.Second % 10 == 0) // Log every ~10 seconds
						{
							//Log($"[HISTORICAL] SendBarSync: {instrument} @ {timestamp:yyyy-MM-dd HH:mm:ss}");
						}
                        using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                        {
                            try
                            {
                                // Use very short timeout for backtesting to avoid delays
                                using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                                {
									string endpoint = $"{baseUrl}/api/ingest/bars/{instrument}";
                                    var response = client.PostAsync(endpoint, content, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                                   // Log(response.ToString());
									response.Dispose();
                                    return true; // Always return true in historical mode
                                }
                            }
                            catch
                            {
                                // Silently fail in historical mode - service unavailable is expected
                                return true;
                            }
                        }
                    }
                    
                    // IMPORTANT: Return early for historical mode to avoid WebSocket logic below
                    if (IsHistoricalMode())
                    {
                        return true;
                    }
                    
                    // Real-time mode: Use WebSocket (existing complex logic) - ONLY if enabled
                    if (!useWebSocketConnection)
                    {
                        // HTTP-only mode for local testing - use fire-and-forget HTTP
                        return SendBarFireAndForget(false, instrument, timestamp, open, high, low, close, volume, timeframe, "SendBarSync-HTTP");
                    }
                    
                    // CRITICAL FIX: Skip if already connecting to prevent overlapping connection attempts
                    if (webSocket != null && webSocket.State == WebSocketState.Connecting)
                    {
                        // Check if connection has been attempting for too long (10 seconds)
                        if (webSocketConnectStartTime != DateTime.MinValue && 
                            (DateTime.Now - webSocketConnectStartTime).TotalSeconds > 10)
                        {
                            webSocket.Dispose();
                            webSocket = null;
                            webSocketConnected = false;
                            webSocketConnectStartTime = DateTime.MinValue;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    
                    // Handle aborted/closed connections - reset and retry
                    if (webSocket != null && (webSocket.State == WebSocketState.Aborted || webSocket.State == WebSocketState.Closed))
                    {
                        webSocket.Dispose();
                        webSocket = null;
                        webSocketConnected = false;
                    }
                    
                    if (webSocket == null || webSocket.State != WebSocketState.Open)
                    {
                    // Try to connect asynchronously, but wait synchronously
                        webSocketConnectStartTime = DateTime.Now; // Track when we started connecting
                    var connectTask = ConnectWebSocketAsync();
                        
                        // FIXED: This blocking wait is still needed for SendBarSync method functionality
                    if (!connectTask.Wait(3000)) // Wait up to 3 seconds with timeout
                    {
                        return false;
                    }
                    
                        // FIXED: Remove blocking .Result calls - Use .NET Framework compatible check
                        bool connectionSuccess = connectTask.Status == TaskStatus.RanToCompletion;
                        
                        if (!connectionSuccess)
                    {
                        return false;
                    }
                }
                
                // Ensure WebSocket is open after connection attempt
                if (webSocket == null || webSocket.State != WebSocketState.Open)
                {
                    return false;
                }
                
                // Convert timestamp to milliseconds since epoch
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                
                // Create bar message
                var barMessage = new
                {
                    type = "bar",
                    instrument = instrument,
                    timestamp = epochMs,
                    open = Convert.ToDouble(open),
                    high = Convert.ToDouble(high),
                    low = Convert.ToDouble(low),
                    close = Convert.ToDouble(close),
                    volume = Convert.ToDouble(volume),
                    timeframe = timeframe
                };
                
                // Serialize to JSON
                string jsonMessage = JsonConvert.SerializeObject(barMessage);
                byte[] messageBytes = Encoding.UTF8.GetBytes(jsonMessage);
                
                // Send synchronously with timeout
                var sendTask = webSocket.SendAsync(
                    new ArraySegment<byte>(messageBytes),
                    WebSocketMessageType.Text,
                    true,
                        CancellationToken.None
                );
                
                // Wait for completion with timeout
                if (sendTask.Wait(2000)) // 2 second timeout
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (AggregateException aggEx) when (aggEx.InnerException is OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
            }
        }
       
        // Combined method to send bar and poll signals in one call - Modified to call ASYNC POLL
		/*
        public bool SendBarAndPollSync(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            NinjaTrader.Code.Output.Process("SendBarAndPollSync: Simplified implementation called", PrintTo.OutputTab1);
            
            // Fire and forget the bar data
            bool success = SendBarFireAndForget(instrument, timestamp, open, high, low, close, volume, timeframe);
            
            if (success)
            {
                // Also fire and forget the signals check
                CheckSignalsFireAndForget(timestamp.ToString(),instrument);
                NinjaTrader.Code.Output.Process("SendBarAndPollSync: Both bar and signal requests sent", PrintTo.OutputTab1);
            }
            else
            {
                NinjaTrader.Code.Output.Process("SendBarAndPollSync: Failed to send bar data", PrintTo.OutputTab1);
            }
            
            return success;
        }
*/
        // Check if WebSocket is currently connected
        public bool IsWebSocketConnected()
        {
            if (IsDisposed())
                return false;
                
            return webSocket != null && webSocket.State == WebSocketState.Open && webSocketConnected;
        }

        // Reset static data method
        public static void ResetStaticData()
        {
            CurrentBullStrength = 0;
            CurrentBearStrength = 0;
            PatternName = string.Empty;
            LastSignalTimestamp = DateTime.MinValue;
            CurrentAvgSlope = 0;
            CurrentSlopeImpact = 0;
            CurrentOppositionStrength = 0;
            CurrentConfluenceScore = 0;
            CurrentEffectiveScore = 0;
			CurrentRawScore = 0;
            CurrentStopModifier = 0;
            CurrentPullbackModifier = 0;
            CurrentDivergenceScore = 0;
            CurrentBarsSinceEntry = 0;
            CurrentShouldExit = false;
            CurrentConsecutiveBars = 0;
            CurrentConfirmationBarsRequired = 3;
            CurrentPatternId = null; // Reset the pattern ID

            CurrentPatternType = null;
            CurrentSubtype = null;
            
            // Reset orange line data
   
            
            // NEW: Reset RF exit data
            CurrentRFExitScore = 0;
            CurrentRFShouldExit = false;
            CurrentRFExitReason = "";
            CurrentRFConfidenceChange = 0;
            LastRFExitUpdate = DateTime.MinValue;
            
            if (CurrentMatches != null)
                CurrentMatches.Clear();
            else
                CurrentMatches = new List<PatternMatch>();
        }
        
        // WebSocket connection method
        public async Task<bool> ConnectWebSocketAsync()
        {
            if (IsDisposed()) return false;
            
            try
            {
                // Clean up existing connection if any
                if (webSocket != null)
                {
                    try
                    {
                        if (webSocket.State == WebSocketState.Open)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None);
                        }
                        webSocket.Dispose();
                    }
                    catch { /* Ignore errors during cleanup */ }
                    webSocket = null;
                }
                
                // Create new WebSocket
                webSocket = new ClientWebSocket();
                webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                
                // Build WebSocket URI from base URL (convert http to ws)
                Uri baseUri = new Uri(baseUrl);
                string scheme = baseUri.Scheme == "https" ? "wss" : "ws";
                string wsEndpoint = $"{scheme}://{baseUri.Host}:{baseUri.Port}/ws";
                
                // Connect with timeout
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    try
                {
                    await webSocket.ConnectAsync(new Uri(wsEndpoint), timeoutCts.Token);
                    }
                    catch (Exception connectEx)
                    {
                        // Connection failed - ensure WebSocket is cleaned up
                        Log($"WebSocket ConnectAsync failed: {connectEx.Message}");
                        throw; // Re-throw to be caught by outer catch
                    }
                }
                
                webSocketConnected = true;
                Log($"WebSocket connected to {wsEndpoint}");
                
                // Start listening for messages
                StartWebSocketListening();
                
                return true;
            }
            catch (Exception ex)
            {
                Log($"WebSocket connection error: {ex.Message}");
                webSocketConnected = false;
                
                // CRITICAL: Clean up the failed WebSocket to prevent stuck "Connecting" state
                if (webSocket != null)
                {
                    try { webSocket.Dispose(); } catch { }
                    webSocket = null;
                }
                webSocketConnectStartTime = DateTime.MinValue;
                
                return false;
            }
        }
        
        // WebSocket message listener
        private void StartWebSocketListening()
        {
            if (webSocket == null || webSocket.State != WebSocketState.Open) return;
            
            Task.Run(async () => {
                try
                {
                    var buffer = new byte[4096];
                    while (webSocket.State == WebSocketState.Open && !IsDisposed())
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            webSocketConnected = false;
                            break;
                        }
                        
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            ProcessWebSocketMessage(message);
                        }
                }
            }
            catch (Exception ex)
            {
                    if (!IsDisposed())
                        Log($"WebSocket receive error: {ex.Message}");
                    
                    webSocketConnected = false;
                }
            });
        }
        
        // Process WebSocket messages
        private void ProcessWebSocketMessage(string message)
        {
            try
            {
                var response = JsonConvert.DeserializeObject<dynamic>(message);
                
                if (response.type == "signal")
                {
                                            // Update static properties
                    if (response.data != null)
                    {
                        CurrentBullStrength = response.data.bull ?? 0;
                        CurrentBearStrength = response.data.bear ?? 0;
                        LastSignalTimestamp = DateTime.Now;
                                                
                        // Update matches if available
                        if (response.data.matches != null)
                        {
                            try
                            {
                                CurrentMatches = response.data.matches.ToObject<List<PatternMatch>>();
                                if (CurrentMatches.Count > 0)
                                    PatternName = CurrentMatches[0].patternName;
                            }
                            catch { /* Ignore match parsing errors */ }
                        }
                        
                        Log($"WebSocket signal update: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                Log($"Error processing WebSocket message: {ex.Message}");
            }
        }
        
        // Queue bar method
        public bool QueueBar(bool UseRemoteService, string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            // For now, simply pass through to SendBarFireAndForget
            return SendBarFireAndForget(UseRemoteService, instrument, timestamp, open, high, low, close, volume, timeframe);
        }
        
        // Check health method
        public async Task<bool> CheckHealth()
        {
            return await CheckHealthAsync();
        }

        // Store mapping between signalContextId and entryUuid
        public void StoreSignalContextMapping(string signalContextId, string entryUuid)
        {
            if (string.IsNullOrEmpty(signalContextId) || string.IsNullOrEmpty(entryUuid))
            {
                Log($"[WARN] Invalid signalContextId ({signalContextId}) or entryUuid ({entryUuid}) provided");
                return;
            }

            lock (signalContextLock)
            {
                signalContextToEntryMap[signalContextId] = entryUuid;
                Log($"[INFO] Stored mapping: signalContextId {signalContextId} -> entryUuid {entryUuid}");
            }
        }

        // Get entryUuid for a given signalContextId
        public string GetEntryUuidForSignalContext(string signalContextId)
        {
            if (string.IsNullOrEmpty(signalContextId))
            {
                return null;
            }

            lock (signalContextLock)
            {
                if (signalContextToEntryMap.TryGetValue(signalContextId, out string entryUuid))
                {
                    return entryUuid;
                }
            }

            return null;
        }

        

        
        // Add a method to submit pattern performance record
        public async Task<bool> RecordPatternPerformanceAsync(PatternPerformanceRecord record, bool useRemoteService)
        {
            if (IsDisposed()) return false;

            try
            {
                // Validate required fields
                if (string.IsNullOrEmpty(record.contextId))
                {
                    Log("[ERROR] RecordPatternPerformance: signalContextId is required");
                    return false;
                }

                // NEW: Add strategyId to the record
                var recordWithStrategy = new
                {
                    strategyId = sessionID,
                    contextId = record.contextId,
                    timestamp_ms = record.timestamp_ms,
                    bar_timestamp_ms = record.bar_timestamp_ms,
                    maxGain = record.maxGain,
                    maxLoss = record.maxLoss,
                    isLong = record.isLong,
                    instrument = record.instrument
                };

                // Use endpoint URL pointing to MatchingEngine service Thompson routes
				string meServiceUrl = getMeServiceUrl();
                string endpoint = $"{meServiceUrl}/api/thompson/record-pattern-performance";
              
                // Serialize the record to JSON
                string jsonPayload = JsonConvert.SerializeObject(recordWithStrategy);
                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                {
                    // Send request to the server
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            string responseText = await response.Content.ReadAsStringAsync(); 
                            //Log($"[INFO] Pattern performance record sent successfully: {record.contextId}");
                            
                            // Remove the mapping after successful recording
                            lock (signalContextLock)
                            {
                                signalContextToEntryMap.Remove(record.contextId);
                            }
                            
                            return true;
                        }
                        else
                        {
                            string responseText = await response.Content.ReadAsStringAsync();
                            Log($"[ERROR] Failed to record pattern performance: {response.StatusCode}, Response: {responseText}");
                            return false;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log($"[ERROR] RecordPatternPerformance: Request timed out");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] RecordPatternPerformance: {ex.Message}");
                return false;
            }
        }

        // Add a method to purchase a signal
        public async Task<bool> PurchaseSignalAsync(string patternId, string instrument, string type, long barTimestamp)
        {
            if (IsDisposed()) return false;

            try
            {
                // First check if the signal exists and is fresh enough
                long signalEpochMs = barTimestamp;
                string endpoint = $"{signalPoolUrl}/api/signals/available?strategyId={sessionID}&simulationTime={signalEpochMs}&instrument={instrument}";
                
                using (var checkTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var checkResponse = await client.GetAsync(endpoint, checkTimeoutCts.Token);
                    
                    if (checkResponse.IsSuccessStatusCode)
                    {
                        string responseText = await checkResponse.Content.ReadAsStringAsync();
                        var signalPoolResponse = JsonConvert.DeserializeObject<SignalPoolResponse>(responseText);
                        
                        if (signalPoolResponse != null && signalPoolResponse.signals != null)
                        {
                            // Find the specific signal we want to purchase
                            var targetSignal = signalPoolResponse.signals
                                .FirstOrDefault(s => s.patternId == patternId && s.type == type && !s.isPurchased);
                            
                            if (targetSignal == null)
                                return false;
                            
                            // Check age - only purchase if less than 5 minutes old
                            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            long ageMs = currentTime - targetSignal.receivedTimestamp;
                            double ageMinutes = ageMs / (1000.0 * 60);
                            
                            if (ageMinutes >= 5.0)
                                return false;
                            
                            // Signal exists and is fresh - proceed with purchase
                            Log($"[SIGNAL_POOL] ATTEMPTING PURCHASE: Signal {patternId.Substring(0, 8)}... ({instrument}_{type}) with score: {(targetSignal.rawScore * 100):F2}%");
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                // Build the purchase request
                var purchaseRequest = new
                {
                    strategyId = sessionID,  // NEW: Add sessionID as strategyId
                    traderId = sessionID, // Use the session ID as the trader ID
                    patternId = patternId,
                    instrument = instrument,
                    type = type,
                    barTimestamp = barTimestamp
                };

                // Convert to JSON
                string jsonPayload = JsonConvert.SerializeObject(purchaseRequest);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send purchase request to signal pool
                string purchaseEndpoint = $"{signalPoolUrl}/api/signals/purchase";
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostAsync(purchaseEndpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        Log($"[SIGNAL_POOL] PURCHASE CONFIRMED: Signal {patternId.Substring(0, 8)}... ({instrument}_{type})");
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Error purchasing signal: {ex.Message}");
                return false;
            }
        }

        // Send dynamic matching configuration to MatchingEngine service
        public async Task<bool> SendMatchingConfigAsync(string instrument, MatchingConfig config,bool useRemoteService)
        {
            if (IsDisposed()) return false;

            try
            {
                // Validate required parameters
                if (string.IsNullOrEmpty(instrument) || config == null)
                {
                    Log("[ERROR] SendMatchingConfig: Invalid instrument or config provided");
                    return false;
                }

                // Create the request payload
                var configRequest = new
                {
                    instrument = instrument,
                    matchingConfig = new
                    {
                        zScoreThreshold = config.ZScoreThreshold,
                        reliabilityPenaltyEnabled = config.ReliabilityPenaltyEnabled,
                        maxThresholdPenalty = config.MaxThresholdPenalty,
                        atmosphericThreshold = config.AtmosphericThreshold,
                        cosineSimilarityThresholds = config.CosineSimilarityThresholds != null ? new
                        {
                            defaultThreshold = config.CosineSimilarityThresholds.DefaultThreshold,
                            emaRibbon = config.CosineSimilarityThresholds.EmaRibbon,
                            sensitiveEmaRibbon = config.CosineSimilarityThresholds.SensitiveEmaRibbon
                        } : null,
                        sessionId = sessionID,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };

                // Convert to JSON
                string jsonPayload = JsonConvert.SerializeObject(configRequest);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send to MatchingEngine service
				string meServiceUrl = getMeServiceUrl();
                string endpoint = $"{meServiceUrl}/api/ingest/config/matching";
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        Log($"[CONFIG] Successfully sent matching config for {instrument}");
                        Log($"[CONFIG] Config: ZScore={config.ZScoreThreshold}, Atmospheric={config.AtmosphericThreshold}, ReliabilityPenalty={config.ReliabilityPenaltyEnabled}");
                        return true;
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Log($"[ERROR] Failed to send matching config (HTTP {response.StatusCode}): {errorContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Error sending matching config: {ex.Message}");
                return false;
            }
        }

        // Synchronous wrapper for SendMatchingConfigAsync - CONDITIONAL SYNC/ASYNC
        public bool SendMatchingConfig(string instrument, MatchingConfig config)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] SendMatchingConfig starting sync call for {instrument}");
                    var result = SendMatchingConfigAsync(instrument, config,useRemoteService).ConfigureAwait(false).GetAwaiter().GetResult();
                    Log($"[HISTORICAL-SYNC] SendMatchingConfig completed sync call for {instrument}");
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] SendMatchingConfig starting background call for {instrument}");
                            await SendMatchingConfigAsync(instrument, config,useRemoteService);
                            Log($"[REALTIME-ASYNC] SendMatchingConfig completed background call for {instrument}");
			            }
			            catch (Exception ex)
			            {
	                        Log($"[REALTIME-ASYNC] SendMatchingConfig background call failed for {instrument}: {ex.Message}");
	                    }
	                 });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[CONDITIONAL] Error in SendMatchingConfig synchronous method: {ex.Message}");
                return false;
            }
        }

        // Enhanced registration with entry price and direction for Dynamic Risk Alignment
        public async Task<bool> RegisterPositionAsync(string entrySignalId, string patternSubType, string patternUuid, string instrument, DateTime entryTimestamp, double entryPrice = 0.0, string direction = "long", double[] originalForecast = null, bool doNotStore = false)
        {
            return await RegisterPositionWithDataAsync(entrySignalId, patternSubType, patternUuid, instrument, entryTimestamp, entryPrice, direction, originalForecast, useRemoteService, doNotStore);
        }

        // Legacy registration method for compatibility
        public async Task<bool> RegisterPositionAsync(string entrySignalId, string patternSubType, string patternUuid, string instrument, DateTime entryTimestamp)
        {
            return await RegisterPositionWithDataAsync(entrySignalId, patternSubType, patternUuid, instrument, entryTimestamp, 0.0, "long", null,useRemoteService);
        }

        // Core registration method with full Dynamic Risk Alignment data
        private async Task<bool> RegisterPositionWithDataAsync(string entrySignalId, string patternSubType, string patternUuid, string instrument, DateTime entryTimestamp, double entryPrice, string direction, double[] originalForecast, bool useRemoteService, bool doNotStore = false)
        {
            if (IsDisposed()) return false;

            // Log(`Begin RegisterPositionAsync of ${instrument}`);
            
            // Validate input parameters first
            if (string.IsNullOrEmpty(entrySignalId) || string.IsNullOrEmpty(patternUuid) || string.IsNullOrEmpty(instrument))
            {
                Log($"[DIVERGENCE] ERROR: Invalid parameters for registration - EntrySignalId: {entrySignalId ?? "null"}, PatternUuid: {patternUuid ?? "null"}, Instrument: {instrument ?? "null"}");
                return false;
            }

            // Store the mapping of entry signal ID to pattern ID
            lock (divergenceLock)
            {
                entrySignalToPatternId[entrySignalId] = patternUuid;
            }

            // Try up to 3 times with retry logic
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // Convert timestamp to ISO format
                    string isoTimestamp = entryTimestamp.ToUniversalTime().ToString("o");
                    
                    // Create the request payload for Dynamic Risk Alignment System
                    var requestPayload = new
                    {
                        strategyId = sessionID,  // Session ID for tracking
                        entrySignalId = entrySignalId,
						entryType = patternSubType,
                        patternUuid = patternUuid,
                        instrument = instrument,
                        entryTimestamp = isoTimestamp,
                        entryPrice = entryPrice, // Actual entry price from strategy
                        direction = direction, // Actual direction from strategy  					
                        originalForecast = originalForecast ?? new double[] { }, // Forecast from Curved Prediction Service
                        marketContext = new { // Current market context
                            timestamp = isoTimestamp,
                            sessionId = sessionID
                        },
                        doNotStore = doNotStore // Flag for out-of-sample testing
                    };
                    
                    // Log details on first attempt
                    if (attempt == 1)
                    {
                        //Log($"[DIVERGENCE] Registering position {entrySignalId} for pattern {patternUuid}");
                    }
                    else
                    {
                        Log($"[DIVERGENCE] Retry {attempt}/3: Registering position {entrySignalId}");
                    }
                    
                    // Convert to JSON
                    string jsonPayload = JsonConvert.SerializeObject(requestPayload);
                    StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    
                    // Define the endpoint - use the new ME service endpoint
					string meServiceUrl = getMeServiceUrl();
                    string endpoint = $"{meServiceUrl}/api/positions/register";
                    
                    // Send request with timeout
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) // Reduced timeout
                    {
                        var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Log($"[DYNAMIC-RISK] Successfully registered position {entrySignalId} for intelligent risk tracking");
                            
                            // Add a small delay after registration to let the server process 
                            await Task.Delay(100);
                            
                            // Add to local registry
                            activePositions[entrySignalId] = new RegisteredPosition
                            {
                                PatternUuid = patternUuid,
                                Instrument = instrument,
                                EntryTimestamp = entryTimestamp,
                                BarsSinceEntry = 0,
                                RegistrationTimestamp = DateTime.Now
                            };
                            
                            // NEW: Also register with RF service for RF-based exit monitoring
                            try
                            {
                                bool rfRegistered = await RegisterRFExitPositionAsync(entrySignalId, patternUuid, instrument, entryTimestamp, direction, entryPrice);
                                if (rfRegistered)
                                {
                                    Log($"[RF-EXIT] Successfully registered position {entrySignalId} for RF exit monitoring");
                                }
                                else
                                {
                                    Log($"[RF-EXIT] Warning: Failed to register position {entrySignalId} for RF exit monitoring");
                                }
                            }
                            catch (Exception rfEx)
                            {
                                Log($"[RF-EXIT] Error registering RF exit monitoring for {entrySignalId}: {rfEx.Message}");
                                // Don't fail the overall registration if RF registration fails
                            }
                            
                            return true;
                        }
                        
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            Log($"[DIVERGENCE] Failed to register position (HTTP {response.StatusCode}): {errorContent}");
                            
                            // Only retry server errors (5xx), not client errors (4xx)
                            if ((int)response.StatusCode < 500) 
                            {
                                return false; // Don't retry client errors
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    bool isConnectionError = ex.InnerException is System.Net.Sockets.SocketException ||
                              ex.Message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0;
                              
                    if (isConnectionError)
                    {
                        Log($"[DIVERGENCE] Connection error registering position: {ex.Message}");
                        // Connection errors are retryable - continue to next attempt
                    }
                    else
                    {
                        Log($"[DIVERGENCE] HTTP request error registering position: {ex.Message}");
                        return false; // Only retry connection errors
                    }
                }
                catch (TaskCanceledException)
                {
                    Log($"[DIVERGENCE] Request timed out registering position");
                    // Timeouts are retryable - continue to next attempt
                }
                catch (Exception ex)
                {
                    Log($"[DIVERGENCE] Error registering position: {ex.Message}");
                    return false; // Don't retry unknown errors
                }
                
                // Wait before retrying (only if not the last attempt)
                if (attempt < 3)
                {
                    await Task.Delay(500 * attempt); // Progressive backoff: 500ms, 1000ms
                }
            }
            
            // If we got here, all attempts failed
            Log($"[DIVERGENCE] All {3} attempts to register position {entrySignalId} failed");
            return false;
        }

		#region
		/*
        // Enhanced synchronous registration with entry price and direction - CONDITIONAL SYNC/ASYNC
        public bool RegisterPosition(string entrySignalId, string patternSubType,string patternUuid, string instrument, DateTime entryTimestamp, double entryPrice = 0.0, string direction = "long", double[] originalForecast = null, bool doNotStore = false)
        {
            var startTime = DateTime.Now;
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] RegisterPosition starting sync call for {entrySignalId}");
                    var result = RegisterPositionAsync(entrySignalId, patternSubType, patternUuid, instrument, entryTimestamp, entryPrice, direction, originalForecast, doNotStore).ConfigureAwait(false).GetAwaiter().GetResult();
                    
                    var duration = (DateTime.Now - startTime).TotalMilliseconds;
                    Log($"[HISTORICAL-SYNC] RegisterPosition completed sync call for {entrySignalId} - Duration: {duration:F0}ms");
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] RegisterPosition starting background registration for {entrySignalId}");
                            await RegisterPositionAsync(entrySignalId, patternSubType, patternUuid, instrument, entryTimestamp, entryPrice, direction, originalForecast, doNotStore);
                            Log($"[REALTIME-ASYNC] RegisterPosition completed background registration for {entrySignalId}");
            }
            catch (Exception ex)
            {
                            Log($"[REALTIME-ASYNC] RegisterPosition background registration failed for {entrySignalId}: {ex.Message}");
                        }
                    });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[CONDITIONAL] Error in enhanced RegisterPosition: {ex.Message}");
                return false;
            }
        }

        // Legacy synchronous wrapper for compatibility - CONDITIONAL SYNC/ASYNC
        public bool RegisterPosition(string entrySignalId, string patternSubType, string patternUuid, string instrument, DateTime entryTimestamp)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] Legacy RegisterPosition starting sync call for {entrySignalId}");
                    var result = RegisterPositionAsync(entrySignalId, patternSubType, patternUuid, instrument, entryTimestamp).ConfigureAwait(false).GetAwaiter().GetResult();
                    Log($"[HISTORICAL-SYNC] Legacy RegisterPosition completed sync call for {entrySignalId}");
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] Legacy RegisterPosition starting background registration for {entrySignalId}");
                            await RegisterPositionAsync(entrySignalId, patternSubType, patternUuid, instrument, entryTimestamp);
                            Log($"[REALTIME-ASYNC] Legacy RegisterPosition completed background registration for {entrySignalId}");
            }
            catch (Exception ex)
            {
                            Log($"[REALTIME-ASYNC] Legacy RegisterPosition background registration failed for {entrySignalId}: {ex.Message}");
                        }
                    });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[CONDITIONAL] Error in RegisterPosition synchronous method: {ex.Message}");
                return false;
            }
        }

        // Check divergence score for a registered position with threshold parameter
		// UPDATED: Now uses Dynamic Risk Alignment System with binary scoring (0 or 1)
		public double CheckDivergence(string entrySignalId)
		{
		    return CheckDivergence(entrySignalId, 0.0);
		}

		// NEW: Overloaded method that accepts currentPrice - CONDITIONAL SYNC/ASYNC
		public double CheckDivergence(string entrySignalId, double currentPrice)
		{
		    try
		    {
				// CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
				if (IsHistoricalMode())
				{
					// BACKTESTING: Use synchronous calls for sequential bar processing
					Log($"[HISTORICAL-SYNC] CheckDivergence starting sync call for {entrySignalId}");
					var result = CheckDivergenceAsync(entrySignalId, currentPrice).ConfigureAwait(false).GetAwaiter().GetResult();
					Log($"[HISTORICAL-SYNC] CheckDivergence completed sync call for {entrySignalId}");
					return result;
				}
				else
				{
					// REAL-TIME: Use fire-and-forget to prevent freezes
					// Return cached value if available
					if (divergenceScores.ContainsKey(entrySignalId))
					{
						return divergenceScores[entrySignalId];
					}
					
					// Fire-and-forget async update in background
					Task.Run(async () => {
						try
						{
							Log($"[REALTIME-ASYNC] CheckDivergence starting background update for {entrySignalId}");
							await CheckDivergenceAsync(entrySignalId, currentPrice);
							Log($"[REALTIME-ASYNC] CheckDivergence completed background update for {entrySignalId}");
						}
						catch (Exception ex)
						{
							Log($"[REALTIME-ASYNC] CheckDivergence background update failed for {entrySignalId}: {ex.Message}");
						}
					});
					
					// Return cached value or default
					return divergenceScores.ContainsKey(entrySignalId) ? divergenceScores[entrySignalId] : 0;
				}
			}
			catch (Exception ex)
			{
				Log($"[CONDITIONAL] CheckDivergence failed for {entrySignalId}: {ex.Message}");
		        return 0;
		    }
		}

		public async Task<double> CheckDivergenceAsync(string entrySignalId)
		{
		    return await CheckDivergenceAsync(entrySignalId, 0.0);
		}

		// NEW: Overloaded method that accepts currentPrice  
		public async Task<double> CheckDivergenceAsync(string entrySignalId, double currentPrice)
		{
		    if (IsDisposed()) return -9999999;
		    
		    // Check if we already have a pending request for this entry
		    Task<double> pendingTask = null;
		    lock (divergenceLock)
		    {
		        if (pendingDivergenceRequests.ContainsKey(entrySignalId))
		        {
		            pendingTask = pendingDivergenceRequests[entrySignalId];
		        }
		    }
		    
		    // If we found a pending task, await it outside the lock
		    if (pendingTask != null)
		    {
		        return await pendingTask;
		    }
		    
		    // Create and cache the request task for Dynamic Risk Alignment with currentPrice
		    var requestTask = PerformDynamicRiskRequestAsync(entrySignalId, useRemoteService, currentPrice);
		    
		    lock (divergenceLock)
		    {
		        pendingDivergenceRequests[entrySignalId] = requestTask;
		    }
		    
		    try
		    {
		        var result = await requestTask;
		        return result;
		    }
		    finally
		    {
		        // Remove from pending requests when done
		        lock (divergenceLock)
		        {
		            pendingDivergenceRequests.Remove(entrySignalId);
		        }
		    }
		}

		*/
		
		#endregion
		// UPDATED: Dynamic Risk Alignment with forecast recalculation and 3-strike rule
		// Returns binary divergence score from intelligent risk assessment
		private async Task<double> PerformDynamicRiskRequestAsync(string entrySignalId,bool useRemoteService, double currentPrice = 0.0)
		{
		    try
		    {
				string meServiceUrl = getMeServiceUrl();
		        string endpoint = $"{meServiceUrl}/api/positions/{entrySignalId}/divergence";
		        
		        // Add currentPrice parameter if provided
		        if (currentPrice > 0)
		        {
		            endpoint += $"?currentPrice={currentPrice}";
		        }
		        
		        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
		        {
		            var response = await client.GetAsync(endpoint, timeoutCts.Token);
		            
		            if (response.IsSuccessStatusCode)
		            {
		                var content = await response.Content.ReadAsStringAsync();
		                var divergenceResponse = JsonConvert.DeserializeObject<DivergenceResponse>(content);
		                
		                if (divergenceResponse != null)
		                {
		                    // Update static properties for compatibility
		                    CurrentDivergenceScore = divergenceResponse.divergenceScore; // Now 0 or 1
		                    CurrentBarsSinceEntry = divergenceResponse.barsSinceEntry;
		                    CurrentShouldExit = divergenceResponse.shouldExit; // Now based on binary score
		                    CurrentConsecutiveBars = divergenceResponse.consecutiveBars; // Strike count
		                    CurrentConfirmationBarsRequired = divergenceResponse.confirmationBarsRequired; // Max strikes
		                    
		                    // Cache binary divergence score (0 = continue, 1 = exit)
		                    divergenceScores[entrySignalId] = CurrentDivergenceScore;
		                    
		                    // Log with accurate interpretation - use shouldExit from API response, not just score
		                    string action = CurrentShouldExit ? "EXIT" : "CONTINUE";
		                    string priceInfo = currentPrice > 0 ? $", Price=${currentPrice:F2}" : "";
		                    
		                    // Log any score/shouldExit mismatches for debugging
		                    if (CurrentDivergenceScore == 0 && CurrentShouldExit)
		                    {
		                        Log($"[DYNAMIC-RISK] {entrySignalId}: Score={CurrentDivergenceScore} ({action}){priceInfo}, " +
		                            $"Strikes={CurrentConsecutiveBars}/{CurrentConfirmationBarsRequired}, " +
		                            $"ThompsonScore={divergenceResponse.thompsonScore:F3} [SCORE-EXIT-MISMATCH]");
		                    }
		                    else
		                    {
		                        //Log($"[DYNAMIC-RISK] {entrySignalId}: Score={CurrentDivergenceScore} ({action}){priceInfo}, " +
		                         //   $"Strikes={CurrentConsecutiveBars}/{CurrentConfirmationBarsRequired}, " +
		                          //  $"ThompsonScore={divergenceResponse.thompsonScore:F3}");
		                    }
		            
		                    // Cache Thompson score from Dynamic Risk response
		                    if (divergenceResponse.thompsonScore >= 0 && divergenceResponse.thompsonScore <= 1)
		                    {
		                        string patternId = null;
		                        lock (divergenceLock)
		                        {
		                            entrySignalToPatternId.TryGetValue(entrySignalId, out patternId);
		                        }
		                        
		                        if (!string.IsNullOrEmpty(patternId))
		                        {
		                            thompsonScores[patternId] = divergenceResponse.thompsonScore;
		                        }
		                    }
		                    
		                    return CurrentDivergenceScore; // Return binary score (0 or 1)
		                }
		            }
		            else if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
		            {
		                Log($"[DYNAMIC-RISK] Failed to check risk for {entrySignalId}: {response.StatusCode}");
		            }
		        }
		    }
		    catch (Exception ex)
		    {
		        // Don't log timeout/cancellation exceptions as they're expected
		        if (!(ex is TaskCanceledException || ex is OperationCanceledException))
		        {
		            Log($"[DYNAMIC-RISK] Error checking risk for {entrySignalId}: {ex.Message}");
		        }
		    }
		    
		    return 0; // Default: continue (no exit signal)
		}

        // Strategy state management for conditional sync/async behavior
        public void SetStrategyState(bool isHistorical)
        {
            lock (stateLock)
            {
                isHistoricalMode = isHistorical;
            }
            Log($"[STATE] Strategy state set to: {(isHistorical ? "Historical (Sync)" : "Real-time (Async)")}");
        }
        
        /// <summary>
        /// Set training mode - when true, RF exit calls are disabled during data collection
        /// </summary>
        public void SetTrainingMode(bool trainingMode)
        {
            isTrainingMode = trainingMode;
            Log($"[TRAINING] Training mode set to: {(trainingMode ? "ENABLED (RF exit calls disabled)" : "DISABLED (RF exit calls enabled)")}");
        }
        
        public bool IsHistoricalMode()
        {
            lock (stateLock)
            {
                return isHistoricalMode;
            }
		}

        // Helper method to extract pattern ID from entry signal ID
        public string ExtractPatternIdFromEntrySignal(string entrySignalId)
        {
            try
            {
                // Pattern: "patternId_timestamp_Entry"
                if (string.IsNullOrEmpty(entrySignalId)) return null;
                
                var parts = entrySignalId.Split('_');
                if (parts.Length >= 3 && parts[parts.Length - 1] == "Entry")
                {
                    // Reconstruct pattern ID (all parts except timestamp and "Entry")
                    var patternParts = parts.Take(parts.Length - 2).ToArray();
                    return string.Join("_", patternParts);
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        // ENHANCED: Batch method to update BOTH standard divergence AND RF exit scores - call this once per bar
       /* public async Task UpdateAllDivergenceScoresAsync(double currentPrice = 0.0)
        {
            // Use MasterSimulatedStops instead of activePositions to get actual open positions
            if (IsDisposed() || MasterSimulatedStops == null || MasterSimulatedStops.Count == 0) return;
            
            //logger?.Invoke($"[DEBUG DIVERGENCE] UpdateAllDivergenceScoresAsync START - {MasterSimulatedStops.Count} positions");
            
            var updateTasks = new List<Task>();
            
            // Create tasks for all positions in MasterSimulatedStops (actual open positions)
            foreach (var simStop in MasterSimulatedStops.ToList())
            {
                if (!string.IsNullOrEmpty(simStop.EntryOrderUUID))
                {
                    //logger?.Invoke($"[DEBUG DIVERGENCE] Creating tasks for position: {simStop.EntryOrderUUID}");
                    
                    // 1. Standard divergence check (existing)
                    updateTasks.Add(CheckDivergenceAsync(simStop.EntryOrderUUID, currentPrice));
                    
                    // 2. NEW: RF divergence exit check - RE-ENABLED
                    updateTasks.Add(CheckRFDivergenceExitAsync(simStop.EntryOrderUUID, currentPrice));
                }
            }
            
            //logger?.Invoke($"[DEBUG DIVERGENCE] About to await {updateTasks.Count} tasks (standard + RF) with 10s timeout");
            
            // Wait for all updates to complete (with reasonable timeout)
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    //logger?.Invoke($"[DEBUG DIVERGENCE] Starting Task.WhenAll for both divergence systems...");
                    await Task.WhenAll(updateTasks).ConfigureAwait(false);
                    //logger?.Invoke($"[DEBUG DIVERGENCE] Task.WhenAll completed successfully for both systems");
                }
            }
            catch (Exception ex)
            {
                //logger?.Invoke($"[DEBUG DIVERGENCE] ERROR in Task.WhenAll: {ex.Message}");
                Log($"[DIVERGENCE] Error updating batch divergence scores (standard + RF): {ex.Message}");
            }
            
            //logger?.Invoke($"[DEBUG DIVERGENCE] UpdateAllDivergenceScoresAsync END - Both systems updated");
        }

        // Deregister a position when it's closed
        public async Task<bool> DeregisterPositionAsync(string entrySignalId, bool useRemoteService, bool wasGoodExit = false, double finalDivergenceP = 0.0, PositionOutcomeData outcomeData = null)
        {
            if (IsDisposed()) return false;
            
            if (string.IsNullOrEmpty(entrySignalId))
            {
                Log($"[DIVERGENCE] ERROR: Invalid entrySignalId (null or empty) for deregistration");
                return false;
            }

            // Clean up the entry signal to pattern mapping
            lock (divergenceLock)
            {
                entrySignalToPatternId.Remove(entrySignalId);
            }

            // Try up to 2 times (fewer retries for deregistration since it's less critical)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    // Define the endpoint
					string meServiceUrl = getMeServiceUrl();
                    string endpoint = $"{meServiceUrl}/api/positions/{entrySignalId}/deregister";
                    
                    if (attempt > 1)
                    {
                        Log($"[DIVERGENCE] Retry {attempt}/2: Deregistering position {entrySignalId}");
                    }
                    
                    if(divergenceScores.ContainsKey(entrySignalId))
                    {
                        finalDivergenceP = divergenceScores[entrySignalId];
                    }
                    // Create the payload with exit outcome data
                    var exitOutcomeData = new 
                    {
                        strategyId = sessionID,  // NEW: Add sessionID as strategyId
                        wasGoodExit = wasGoodExit,
                        finalDivergence = finalDivergenceP,
                        // NEW: Add position outcome data to existing payload
                        positionOutcome = outcomeData
                    };
                    
                    // Convert to JSON
                    string jsonPayload = JsonConvert.SerializeObject(exitOutcomeData);
                    // Create request message explicitly to include body with DELETE
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Delete,
                        RequestUri = new Uri(endpoint),
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };
                    
                    // Send request with timeout
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) // Reduced timeout
                    {
						
                        var response = await client.SendAsync(request, timeoutCts.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            // Reset the static properties
                            CurrentDivergenceScore = 0;
                            if(divergenceScores.ContainsKey(entrySignalId))
                            {
                                divergenceScores.Remove(entrySignalId);
                            }
                            // NEW: Also clean up RF exit scores
                            if(rfExitScores.ContainsKey(entrySignalId))
                            {
                                rfExitScores.Remove(entrySignalId);
                            }
                            // Remove from local registry
                            if (activePositions.ContainsKey(entrySignalId))
                            {
                                activePositions.Remove(entrySignalId);
                                //Log($"[DIVERGENCE] Successfully deregistered position {entrySignalId} with wasGoodExit={wasGoodExit}, finalDivergence={finalDivergenceP}");
                            }
                            
                            // NEW: Also deregister from RF service (fire-and-forget to avoid blocking)
                           Task.Run(async () => {
                                try
                                {
                                    await DeregisterRFExitPositionAsync(entrySignalId, wasGoodExit);
                                    Log($"[RF-EXIT] Successfully deregistered RF exit monitoring for {entrySignalId}");
                                }
                                catch (Exception rfEx)
                                {
                                    Log($"[RF-EXIT] Error deregistering RF exit monitoring for {entrySignalId}: {rfEx.Message}");
                                    // Don't fail overall deregistration if RF deregistration fails
                                }
                            });
                            
                            return true;
                        }
                        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            // Not found is OK - means already deregistered or never registered
                            Log($"[DIVERGENCE] Position {entrySignalId} not found (already deregistered or never registered)");
                            return true;
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            Log($"[DIVERGENCE] Failed to deregister position (HTTP {response.StatusCode}): {errorContent}");
                            
                            // Only retry server errors
                            if ((int)response.StatusCode < 500) 
                            {
                                return false;
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    bool isConnectionError = ex.InnerException is System.Net.Sockets.SocketException ||
                               ex.Message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0;
                               
                    if (isConnectionError)
                    {
                        Log($"[DIVERGENCE] Connection error deregistering position: {ex.Message}");
                        // Connection errors are retryable - continue to next attempt
                    }
                    else
                    {
                        Log($"[DIVERGENCE] HTTP request error deregistering position: {ex.Message}");
                        return false; // Only retry connection errors
                    }
                }
                catch (TaskCanceledException)
                {
                    Log($"[DIVERGENCE] Request timed out deregistering position");
                    // Timeouts are retryable - continue to next attempt
                }
                catch (Exception ex)
                {
                    Log($"[DIVERGENCE] Error deregistering position: {ex.Message}");
                    return false; // Don't retry unknown errors
                }
                
                // Wait before retrying (only if not the last attempt)
                if (attempt < 2)
                {
                    await Task.Delay(300 * attempt); // Small backoff
                }
            }
            
            // Since deregistration is less critical, log the failure but return success
            // This prevents issues with positions that can't be deregistered
            Log($"[DIVERGENCE] All attempts to deregister position {entrySignalId} failed, but continuing");
            return true;
        }
		*/

		/*
        // Synchronous wrapper for DeregisterPositionAsync - CONDITIONAL SYNC/ASYNC
        public bool DeregisterPosition(string entrySignalId, bool wasGoodExit = false, double finalDivergence = 0.0, PositionOutcomeData outcomeData = null)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] DeregisterPosition starting sync call for {entrySignalId}");
                    var result = DeregisterPositionAsync(entrySignalId, useRemoteService, wasGoodExit, finalDivergence, outcomeData).ConfigureAwait(false).GetAwaiter().GetResult();
                    Log($"[HISTORICAL-SYNC] DeregisterPosition completed sync call for {entrySignalId}");
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    // Immediately clean up local cache
                    if (divergenceScores.ContainsKey(entrySignalId))
                    {
                        divergenceScores.Remove(entrySignalId);
                    }
                    // NEW: Also clean up RF exit scores
                    if (rfExitScores.ContainsKey(entrySignalId))
                    {
                        rfExitScores.Remove(entrySignalId);
                    }
                    
                    // Fire-and-forget async deregistration in background
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] DeregisterPosition starting background deregistration for {entrySignalId}");
                            await DeregisterPositionAsync(entrySignalId,useRemoteService,  wasGoodExit, finalDivergence, outcomeData);
                            Log($"[REALTIME-ASYNC] DeregisterPosition completed background deregistration for {entrySignalId}");
            }
            catch (Exception ex)
            {
                            Log($"[REALTIME-ASYNC] DeregisterPosition background deregistration failed for {entrySignalId}: {ex.Message}");
                        }
                    });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[CONDITIONAL] Error in DeregisterPosition synchronous method: {ex.Message}");
                return false;
            }
        }
		*/
        // Add Thompson score retrieval method
        public async Task<double> GetThompsonScoreAsync(string patternId)
        {
            if (IsDisposed()) return 0.5;
            
            // First check cached Thompson scores from divergence responses
            if (thompsonScores.ContainsKey(patternId))
            {
                return thompsonScores[patternId];
            }
            
            try
            {
                string endpoint = $"{meServiceUrl}/api/patterns/{patternId}/thompson";
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(1000);
                    var response = await client.GetAsync(endpoint);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonContent = await response.Content.ReadAsStringAsync();
                        var thompsonResponse = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                        double score = (double)(thompsonResponse.thompsonScore ?? 0.5);
                        
                        // Cache the result for future use
                        thompsonScores[patternId] = score;
                        return score;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[THOMPSON] Error getting Thompson score for {patternId}: {ex.Message}");
            }
            
            return 0.5; // Default neutral score
        }
		
		public string getMeServiceUrl()
		{
			return meServiceUrl;
		}

        // NEW: RF-based divergence exit check - follows same pattern as CheckDivergence
        public double CheckRFDivergenceExit(string entrySignalId, double currentPrice = 0.0)
        {
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] CheckRFDivergenceExit starting sync call for {entrySignalId}");
                    var result = CheckRFDivergenceExitAsync(entrySignalId, currentPrice).ConfigureAwait(false).GetAwaiter().GetResult();
                    Log($"[HISTORICAL-SYNC] CheckRFDivergenceExit completed sync call for {entrySignalId}");
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    // Return cached value if available
                    if (rfExitScores.ContainsKey(entrySignalId))
                    {
                        return rfExitScores[entrySignalId];
                    }
                    
                    // Fire-and-forget async update in background
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] CheckRFDivergenceExit starting background update for {entrySignalId}");
                            await CheckRFDivergenceExitAsync(entrySignalId, currentPrice);
                            Log($"[REALTIME-ASYNC] CheckRFDivergenceExit completed background update for {entrySignalId}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[REALTIME-ASYNC] CheckRFDivergenceExit background update failed for {entrySignalId}: {ex.Message}");
                        }
                    });
                    
                    // Return cached value or default
                    return rfExitScores.ContainsKey(entrySignalId) ? rfExitScores[entrySignalId] : 0;
                }
            }
            catch (Exception ex)
            {
                Log($"[RF-EXIT] CheckRFDivergenceExit failed for {entrySignalId}: {ex.Message}");
                return 0;
            }
        }

        // NEW: Async RF divergence exit check
        public async Task<double> CheckRFDivergenceExitAsync(string entrySignalId, double currentPrice = 0.0)
        {
            if (IsDisposed()) return 0;
            
            // Skip RF exit calls during training mode (data collection phase)
            if (isTrainingMode) 
            {
                return 0; // Return 0 (no exit signal) during training mode
            }
            
            // Check if this position is blacklisted (failed RF registration)
            lock (rfBlacklistLock)
            {
                if (rfExitBlacklist.Contains(entrySignalId))
                {
                    return 0; // Silently return 0 for blacklisted positions
                }
            }
            
            // Use entrySignalId directly since we have separate dictionary
            
            try
            {
                // Build RF service endpoint URL
                string rfServiceUrl = "http://localhost:3009"; // RF service port
                string endpoint = $"{rfServiceUrl}/api/rf/divergence-exit/{entrySignalId}";
                
                // Add current price if provided
                if (currentPrice > 0)
                {
                    endpoint += $"?currentPrice={currentPrice}";
                }
                
                // Add session ID for tracking
                char separator = endpoint.Contains('?') ? '&' : '?';
                endpoint += $"{separator}sessionId={sessionID}";
                
               
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var response = await client.GetAsync(endpoint, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var rfExitResponse = JsonConvert.DeserializeObject<dynamic>(content);
                        
                        if (rfExitResponse != null)
                        {
                            // Extract exit decision - should return 1 for exit, 0 for continue
                            bool shouldExit = rfExitResponse.shouldExit ?? false;
                            double exitScore = shouldExit ? 1.0 : 0.0;
                            
                            // Cache the result in separate RF exit dictionary
                            rfExitScores[entrySignalId] = exitScore;
                            
                            // NEW: Update static properties for easy access
                            CurrentRFExitScore = exitScore;
                            CurrentRFShouldExit = shouldExit;
                            CurrentRFExitReason = rfExitResponse.reason?.ToString() ?? "";
                            CurrentRFConfidenceChange = rfExitResponse.rfConfidenceChange ?? 0.0;
                            LastRFExitUpdate = DateTime.Now;
                            
                            // Log RF exit decision details
                            if (rfExitResponse.confidence != null && rfExitResponse.reason != null)
                            {
                                double confidence = rfExitResponse.confidence;
                                string reason = rfExitResponse.reason;
                                string action = shouldExit ? "EXIT" : "CONTINUE";
                                if(shouldExit)
								{
                                	Log($"[RF-EXIT] {entrySignalId}: {action} (confidence: {confidence:F3}) - {reason}");
                            	}
							}
                            
                            return exitScore;
                        }
                        else
                        {
                            // Handle null response
                            Log($"[RF-EXIT] Null response from RF service for {entrySignalId}");
                            return 0;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Add to blacklist to prevent repeated checks
                        lock (rfBlacklistLock)
                        {
                            if (!rfExitBlacklist.Contains(entrySignalId))
                            {
                                rfExitBlacklist.Add(entrySignalId);
                                Log($"[RF-EXIT] Position {entrySignalId} not found in RF service - blacklisted to prevent repeated checks");
                            }
                        }
                        return 0;
                    }
                    else
                    {
                        Log($"[RF-EXIT] RF service returned error {response.StatusCode} for {entrySignalId}");
                        return 0;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Log($"[RF-EXIT] RF exit check timed out for {entrySignalId}");
                return 0;
            }
            catch (Exception ex)
            {
                Log($"[RF-EXIT] Error checking RF exit for {entrySignalId}: {ex.Message}");
                return 0;
            }
        }

        // NEW: Register position for RF-based exit monitoring
        public bool RegisterRFExitPosition(string entrySignalId, string patternId, string instrument, DateTime entryTimestamp, string direction, double entryPrice = 0.0)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls
                    var result = RegisterRFExitPositionAsync(entrySignalId, patternId, instrument, entryTimestamp, direction, entryPrice).ConfigureAwait(false).GetAwaiter().GetResult();
                    return result;
                }
                else
                {
                    // REAL-TIME: Fire-and-forget
                    Task.Run(async () => {
                        try
                        {
                            await RegisterRFExitPositionAsync(entrySignalId, patternId, instrument, entryTimestamp, direction, entryPrice);
                        }
                        catch (Exception ex)
                        {
                            Log($"[RF-EXIT] Background registration failed for {entrySignalId}: {ex.Message}");
                        }
                    });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[RF-EXIT] Error in RegisterRFExitPosition: {ex.Message}");
                return false;
            }
        }

        // NEW: Async registration for RF exit monitoring
        public async Task<bool> RegisterRFExitPositionAsync(string entrySignalId, string patternId, string instrument, DateTime entryTimestamp, string direction, double entryPrice = 0.0)
        {
            if (IsDisposed()) return false;
            
            // Skip RF exit calls during backtesting (historical mode)
            if (isHistoricalMode) 
            {
                Log($"[BACKTEST] Skipping RF exit registration for {entrySignalId} (historical mode)");
                return true; // Return success to avoid disrupting normal flow
            }
            
            // Skip RF exit calls during training mode (data collection phase)
            if (isTrainingMode) 
            {
                Log($"[TRAINING] Skipping RF exit registration for {entrySignalId} (training mode active)");
                return true; // Return success to avoid disrupting normal flow
            }
            
            try
            {
                // Build registration payload
                var registrationData = new
                {
                    entrySignalId = entrySignalId,
                    patternId = patternId,
                    instrument = instrument,
                    entryTimestamp = entryTimestamp.ToUniversalTime().ToString("o"),
                    direction = direction,
                    entryPrice = entryPrice,
                    sessionId = sessionID,
                    originalRFPrediction = new {
                        // Include original RF entry decision for comparison
                        decision = direction.ToUpper() == "LONG" ? "BUY" : "SELL",
                        confidence = 0.75, // Use actual confidence from RF entry if available
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                };
                
                string jsonPayload = JsonConvert.SerializeObject(registrationData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // Send to RF service
                string rfServiceUrl = "http://localhost:3009";
                string endpoint = $"{rfServiceUrl}/api/rf/register-exit-position";
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Log($"[RF-EXIT] Successfully registered RF exit monitoring for {entrySignalId} ({direction} {instrument})");
                        return true;
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Log($"[RF-EXIT] Failed to register RF exit position: {response.StatusCode} - {errorContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[RF-EXIT] Error registering RF exit position: {ex.Message}");
                return false;
            }
        }

        // NEW: Deregister position from RF exit monitoring
        public async Task<bool> DeregisterRFExitPositionAsync(string entrySignalId, bool wasGoodExit = false)
        {
            if (IsDisposed()) return false;
            
            // Skip RF exit calls during backtesting (historical mode)
            if (isHistoricalMode) 
            {
                Log($"[BACKTEST] Skipping RF exit deregistration for {entrySignalId} (historical mode)");
                return true; // Return success to avoid disrupting normal flow
            }
            
            // Skip RF exit calls during training mode (data collection phase)
            if (isTrainingMode) 
            {
                Log($"[TRAINING] Skipping RF exit deregistration for {entrySignalId} (training mode active)");
                return true; // Return success to avoid disrupting normal flow
            }
            
            try
            {
                // Build deregistration payload
                var deregistrationData = new
                {
                    entrySignalId = entrySignalId,
                    wasGoodExit = wasGoodExit,
                    sessionId = sessionID,
                    deregistrationTimestamp = DateTime.UtcNow.ToString("o")
                };
                
                string jsonPayload = JsonConvert.SerializeObject(deregistrationData);
                
                // Create DELETE request with body
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Delete,
                    RequestUri = new Uri($"http://localhost:3009/api/rf/deregister-exit-position/{entrySignalId}"),
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var response = await client.SendAsync(request, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Remove from blacklist when successfully deregistered
                        lock (rfBlacklistLock)
                        {
                            rfExitBlacklist.Remove(entrySignalId);
                        }
                        // Success or not found (already deregistered) are both OK
                        return true;
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Log($"[RF-EXIT] Failed to deregister RF exit position: {response.StatusCode} - {errorContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[RF-EXIT] Error deregistering RF exit position: {ex.Message}");
                return false;
            }
        }

        		// NEW: Static properties to track current RF exit signals (similar to existing divergence properties)
		public static double CurrentRFExitScore { get; private set; } = 0;
		public static bool CurrentRFShouldExit { get; private set; } = false;
		public static string CurrentRFExitReason { get; private set; } = "";
		public static double CurrentRFConfidenceChange { get; private set; } = 0;
		public static DateTime LastRFExitUpdate { get; private set; } = DateTime.MinValue;
		
		// Track positions that failed RF registration to avoid repeated checks
		private readonly HashSet<string> rfExitBlacklist = new HashSet<string>();
		private readonly object rfBlacklistLock = new object();

        // NEW: RF Model Scoring - Request model scoring from RF service
        public async Task<RFFilterResponse> RequestModelScoring(object request)
        {
            if (IsDisposed()) 
            {
                return new RFFilterResponse 
                { 
                    model_available = false, 
                    message = "Service disposed" 
                };
            }
            
            try
            {
                string rfServiceUrl = "http://localhost:3009";
                string endpoint = $"{rfServiceUrl}/api/score-with-model";
                
                Log($"[RF-SCORING] Requesting model scoring from RF service");
                
                string jsonPayload = JsonConvert.SerializeObject(request);
                
                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        var rfResponse = JsonConvert.DeserializeObject<RFFilterResponse>(responseText);
                        
                        Log($"[RF-SCORING] Model scoring response received - Available: {rfResponse.model_available}");
                        return rfResponse;
                    }
                    else
                    {
                        Log($"[RF-SCORING] Model scoring request failed: {response.StatusCode}");
                        return new RFFilterResponse 
                        { 
                            model_available = false, 
                            message = $"HTTP {response.StatusCode}" 
                        };
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Log("[RF-SCORING] Model scoring request timed out");
                return new RFFilterResponse 
                { 
                    model_available = false, 
                    message = "Request timeout" 
                };
            }
            catch (Exception ex)
            {
                Log($"[RF-SCORING] Error in RequestModelScoring: {ex.Message}");
                return new RFFilterResponse 
                { 
                    model_available = false, 
                    message = ex.Message 
                };
            }
        }
/*
        // NEW: Convenience method to get both divergence scores for a position
        public DivergenceScores GetAllDivergenceScores(string entrySignalId, double currentPrice = 0.0)
        {
            try
            {
                // Get both scores using the existing methods
                double standardDivergence = CheckDivergence(entrySignalId, currentPrice);
                double rfExitScore = CheckRFDivergenceExit(entrySignalId, currentPrice);
                
                return new DivergenceScores
                {
                    StandardDivergence = standardDivergence,
                    RFExitScore = rfExitScore,
                    ShouldExit = rfExitScore == 1.0 || standardDivergence >= 15.0, // You can customize this logic
                    ExitReason = rfExitScore == 1.0 ? "RF Thesis Change" : 
                                standardDivergence >= 15.0 ? "Standard Divergence" : "Continue",
                    EntrySignalId = entrySignalId,
                    CurrentPrice = currentPrice,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Log($"[DIVERGENCE] Error getting all divergence scores for {entrySignalId}: {ex.Message}");
                return new DivergenceScores
                {
                    StandardDivergence = 0,
                    RFExitScore = 0,
                    ShouldExit = false,
                    ExitReason = "Error",
                    EntrySignalId = entrySignalId,
                    CurrentPrice = currentPrice,
                    Timestamp = DateTime.Now
                };
            }
        }
*/
		/*
        // NEW: Async version for comprehensive divergence checking
        public async Task<DivergenceScores> GetAllDivergenceScoresAsync(string entrySignalId, double currentPrice = 0.0)
        {
            try
            {
                // Get both scores concurrently
                var standardTask = CheckDivergenceAsync(entrySignalId, currentPrice);
                var rfExitTask = CheckRFDivergenceExitAsync(entrySignalId, currentPrice);
                
                await Task.WhenAll(standardTask, rfExitTask);
                
                double standardDivergence = standardTask.Result;
                double rfExitScore = rfExitTask.Result;
                
                return new DivergenceScores
                {
                    StandardDivergence = standardDivergence,
                    RFExitScore = rfExitScore,
                    ShouldExit = rfExitScore == 1.0 || standardDivergence >= 15.0,
                    ExitReason = rfExitScore == 1.0 ? "RF Thesis Change" : 
                                standardDivergence >= 15.0 ? "Standard Divergence" : "Continue",
                    EntrySignalId = entrySignalId,
                    CurrentPrice = currentPrice,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Log($"[DIVERGENCE] Error getting all divergence scores async for {entrySignalId}: {ex.Message}");
                return new DivergenceScores
                {
                    StandardDivergence = 0,
                    RFExitScore = 0,
                    ShouldExit = false,
                    ExitReason = "Error",
                    EntrySignalId = entrySignalId,
                    CurrentPrice = currentPrice,
                    Timestamp = DateTime.Now
                };
            }
        }
		*/
        // NEW: Helper class to return both divergence scores
        public class DivergenceScores
        {
            public double StandardDivergence { get; set; }
            public double RFExitScore { get; set; }
            public bool ShouldExit { get; set; }
            public string ExitReason { get; set; }
            public string EntrySignalId { get; set; }
            public double CurrentPrice { get; set; }
            public DateTime Timestamp { get; set; }
            
            public override string ToString()
            {
                return $"Standard: {StandardDivergence:F1}, RF: {RFExitScore}, Action: {ExitReason}";
            }
        }

        // NEW: Send position closure data to RF service for annotation
        public async Task<bool> SendPositionClosedAsync(string patternID, List<BarDataPacket> closingBars, double profit, bool isWin)
        {
            if (IsDisposed()) return false;

            // Skip position annotations during backtesting (historical mode)
            if (isHistoricalMode) 
            {
                Log($"[BACKTEST] Skipping position closure annotation for {patternID} (historical mode)");
                return true; // Return success to avoid disrupting normal flow
            }

            try
            {
                // Validate required parameters
                if (string.IsNullOrEmpty(patternID))
                {
                    Log("[ERROR] SendPositionClosed: patternID is required");
                    return false;
                }

                // Create the request payload
                var positionClosedRequest = new
                {
                    patternID = patternID,
                    closingBars = closingBars?.Select(bar => new
                    {
                        timestamp = DateTimeToUnixMs(bar.Timestamp),  // Convert DateTime to Unix ms
                        open = bar.Open,
                        high = bar.High,
                        low = bar.Low,
                        close = bar.Close,
                        volume = bar.Volume,
                        timeframe = bar.Timeframe
                    }).ToArray() ?? new object[0],
                    profit = profit,
                    isWin = isWin,
                    strategyId = sessionID,  // Consistent with other functions
                    sessionID = sessionID,   // Keep both for compatibility
                    closedAt = DateTime.UtcNow.ToString("o")
                };

                // Convert to JSON
                string jsonPayload = JsonConvert.SerializeObject(positionClosedRequest);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send to RF service
                string rfServiceUrl = "http://localhost:3009";
                string endpoint = $"{rfServiceUrl}/api/position-closed";
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        Log($"[ANNOTATION] Successfully sent position closure data for {patternID} ({(isWin ? "WIN" : "LOSS")}: ${profit:F2})");
                        return true;
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        Log($"[ANNOTATION] Failed to send position closure (HTTP {response.StatusCode}): {errorContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ANNOTATION] Error sending position closure: {ex.Message}");
                return false;
            }
        }

        // NEW: Synchronous wrapper for SendPositionClosedAsync
        public bool SendPositionClosed(string patternID, List<BarDataPacket> closingBars, double profit, bool isWin)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential processing
                    var result = SendPositionClosedAsync(patternID, closingBars, profit, isWin).ConfigureAwait(false).GetAwaiter().GetResult();
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            await SendPositionClosedAsync(patternID, closingBars, profit, isWin);
                        }
                        catch (Exception ex)
                        {
                            Log($"[ANNOTATION] Background position closure send failed for {patternID}: {ex.Message}");
                        }
                    });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[ANNOTATION] Error in SendPositionClosed: {ex.Message}");
                return false;
            }
        }

        // NEW: Send historical bars to remote MI service for quick buffer population
        public async Task<bool> SendHistoricalBarsForBackfill(List<object> bars, string instrument, bool useRemoteService)
        {
            if (IsDisposed() || !useRemoteService) return false;

            try
            {
                string baseInstrument = instrument.Split(' ')[0];  // Extract ES, MGC, etc.
                
                Log($"[BACKFILL] Starting backfill for {baseInstrument} with {bars.Count} historical bars to remote MI service");
                
                // Convert bars to the expected format
                var barsData = bars.Select(bar => {
                    dynamic b = bar;
                    return new {
                        timestamp = (long)(b.timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds,
                        open = (double)b.open,
                        high = (double)b.high,
                        low = (double)b.low,
                        close = (double)b.close,
                        volume = (int)b.volume
                    };
                }).ToList();

                // Create backfill payload
                var backfillPayload = new
                {
                    strategyId = sessionID,
                    bars = barsData
                };

                string jsonPayload = JsonConvert.SerializeObject(backfillPayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                Log($"[BACKFILL] JSON payload size: {jsonPayload.Length} characters");
                
                // Send to remote MI service backfill endpoint
                string miUrl = "https://curves-market-ingestion.onrender.com";
                string endpoint = $"{miUrl}/api/ingest/backfill/bars/{baseInstrument}";
                
                Log($"[BACKFILL] Sending {bars.Count} bars to {endpoint}");
                Log($"[BACKFILL] HttpClient timeout: {client.Timeout.TotalSeconds} seconds");
                Log($"[BACKFILL] Request starting at: {DateTime.Now:HH:mm:ss.fff}");
                
                // Step 1: Send backfill request (should respond quickly)
                using (var initialTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var response = await client.PostAsync(endpoint, content, initialTimeoutCts.Token);
                    stopwatch.Stop();
                    Log($"[BACKFILL] Request completed in {stopwatch.ElapsedMilliseconds}ms");
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"[BACKFILL] ❌ FAILED to submit backfill for {baseInstrument}: {response.StatusCode}");
                        Log($"[BACKFILL] Response: {responseContent}");
                        return false;
                    }
                    
                    Log($"[BACKFILL] ✅ Backfill request submitted for {baseInstrument}");
                }
                
                // Step 2: Poll MI service to check when processing is complete
                return await PollBackfillCompletion(miUrl, baseInstrument, bars.Count);
            }
            catch (TaskCanceledException tcEx)
            {
                Log($"[BACKFILL] Request cancelled at: {DateTime.Now:HH:mm:ss.fff}");
                if (tcEx.CancellationToken.IsCancellationRequested)
                {
                    Log($"[BACKFILL] Request was cancelled (timeout): {tcEx.Message}");
                }
                else
                {
                    Log($"[BACKFILL] Request was cancelled (likely network/connectivity): {tcEx.Message}");
                }
                return false;
            }
            catch (HttpRequestException httpEx)
            {
                Log($"[BACKFILL] HTTP request failed: {httpEx.Message}");
                if (httpEx.InnerException != null)
                {
                    Log($"[BACKFILL] Inner exception: {httpEx.InnerException.Message}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log($"[BACKFILL] Unexpected error sending historical bars for {instrument}: {ex.Message}");
                Log($"[BACKFILL] Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Log($"[BACKFILL] Inner exception: {ex.InnerException.Message}");
                }
                return false;
            }
        }

        // NEW: Poll MI service to check backfill completion status
        private async Task<bool> PollBackfillCompletion(string miUrl, string instrument, int expectedBarCount)
        {
            try
            {
                Log($"[BACKFILL-POLL] Monitoring backfill progress for {instrument} (expecting {expectedBarCount} bars)");
                
                int pollAttempts = 0;
                const int maxPollAttempts = 60; // 5 minutes at 5-second intervals
                const int pollIntervalMs = 5000; // 5 seconds between checks
                
                while (pollAttempts < maxPollAttempts)
                {
                    try
                    {
                        // Check MI buffer status using market-data endpoint with strategy ID
                        string statusEndpoint = $"{miUrl}/api/ingest/market-data/{instrument}?strategyId={sessionID}";
                        
                        using (var pollTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        {
                            var statusResponse = await client.GetAsync(statusEndpoint, pollTimeoutCts.Token);
                            
                            if (statusResponse.IsSuccessStatusCode)
                            {
                                var statusContent = await statusResponse.Content.ReadAsStringAsync();
                                dynamic statusObj = JsonConvert.DeserializeObject(statusContent);
                                int currentBarCount = statusObj.barCount;
                                
                                Log($"[BACKFILL-POLL] Attempt {pollAttempts + 1}: {currentBarCount}/{expectedBarCount} bars processed");
                                
                                // Check if we have enough bars (allow some tolerance)
                                if (currentBarCount >= expectedBarCount * 0.95) // 95% threshold
                                {
                                    Log($"[BACKFILL-POLL] ✅ Backfill complete! {currentBarCount} bars available for {instrument}");
                                    return true;
                                }
                            }
                            else if (statusResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                // Buffer might still be empty - this is normal in early polling attempts
                                Log($"[BACKFILL-POLL] Attempt {pollAttempts + 1}: Buffer not yet available (still processing...)");
                            }
                            else
                            {
                                Log($"[BACKFILL-POLL] Attempt {pollAttempts + 1}: Unexpected status {statusResponse.StatusCode}");
                            }
                        }
                    }
                    catch (Exception pollEx)
                    {
                        Log($"[BACKFILL-POLL] Attempt {pollAttempts + 1}: Error checking status - {pollEx.Message}");
                    }
                    
                    pollAttempts++;
                    
                    // Wait before next poll (unless this was the last attempt)
                    if (pollAttempts < maxPollAttempts)
                    {
                        await Task.Delay(pollIntervalMs);
                    }
                }
                
                Log($"[BACKFILL-POLL] ❌ Timeout after {maxPollAttempts} attempts (5 minutes). Backfill may still be processing.");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[BACKFILL-POLL] Error during polling: {ex.Message}");
                return false;
            }
        }
/*
        // NEW: Signal Approval - Request signal approval from approval service
        public async Task<SignalApprovalResponse> RequestSignalApprovalAsync(SignalApprovalRequest request)
        {
            var startTime = DateTime.Now;
            if (IsDisposed()) 
            {
                return new SignalApprovalResponse 
                { 
                    Approved = false, 
                    Confidence = 0.0,
                    Reasons = new[] { "service_disposed" },
                    SuggestedTp = ((MainStrategy)strategy).microContractTakeProfit,
                    SuggestedSl = ((MainStrategy)strategy).microContractStoploss,
                    RecPullback = ((MainStrategy)strategy).softTakeProfitMult // Default soft floor $5
                };
            }
            
            try
            {
                string approvalServiceUrl = "http://localhost:3017";
                string endpoint = $"{approvalServiceUrl}/api/approve-signal";
                
                Log($"[SIGNAL-APPROVAL] Requesting signal approval for {request.Direction} {request.Instrument}");
                
                string jsonPayload = JsonConvert.SerializeObject(request);
                
                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        var approvalResponse = JsonConvert.DeserializeObject<SignalApprovalResponse>(responseText);
                        
                        string status = approvalResponse.Approved ? "APPROVED" : "REJECTED";
                        var duration = (DateTime.Now - startTime).TotalMilliseconds;
                        Log($"[SIGNAL-APPROVAL] Signal {status} - Confidence: {approvalResponse.Confidence:P1} - Duration: {duration:F0}ms");
                        
                        return approvalResponse;
                    }
                    else
                    {
                        var errorDuration = (DateTime.Now - startTime).TotalMilliseconds;
                        Log($"[SIGNAL-APPROVAL] Approval request failed: {response.StatusCode} - Duration: {errorDuration:F0}ms");
                        return new SignalApprovalResponse 
                        { 
                            Approved = false, 
                            Confidence = 0.0,
                            Reasons = new[] { $"http_error_{response.StatusCode}" },
                            SuggestedTp = ((MainStrategy)strategy).microContractTakeProfit,
		                    SuggestedSl = ((MainStrategy)strategy).microContractStoploss,
		                    RecPullback = ((MainStrategy)strategy).softTakeProfitMult // Default soft floor $5
                        };
                    }
                }
            }
            catch (TaskCanceledException)
            {
                var timeoutDuration = (DateTime.Now - startTime).TotalMilliseconds;
                Log($"[SIGNAL-APPROVAL] Signal approval request timed out - Duration: {timeoutDuration:F0}ms");
                return new SignalApprovalResponse 
                { 
                    Approved = false, 
                    Confidence = 0.0,
                    Reasons = new[] { "request_timeout" },
                    SuggestedTp = ((MainStrategy)strategy).microContractTakeProfit,
                    SuggestedSl = ((MainStrategy)strategy).microContractStoploss,
                    RecPullback = ((MainStrategy)strategy).softTakeProfitMult // Default soft floor $5
                };
            }
            catch (Exception ex)
            {
                var exceptionDuration = (DateTime.Now - startTime).TotalMilliseconds;
                Log($"[SIGNAL-APPROVAL] Error in RequestSignalApprovalAsync: {ex.Message} - Duration: {exceptionDuration:F0}ms");
                return new SignalApprovalResponse 
                { 
                    Approved = false, 
                    Confidence = 0.0,
                    Reasons = new[] { $"error_{ex.Message}" },
                    SuggestedTp = ((MainStrategy)strategy).microContractTakeProfit,
                    SuggestedSl = ((MainStrategy)strategy).microContractStoploss,
                    RecPullback = ((MainStrategy)strategy).softTakeProfitMult // Default soft floor $5
                };
            }
        }
*/
        // NEW: Method to trigger backfill when switching to remote mode
        public async Task<bool> BackfillRemoteServiceAsync(List<object> historicalBars, string instrument)
        {
            if (IsDisposed() || historicalBars == null || historicalBars.Count == 0)
            {
                Log("[BACKFILL] No historical bars available for backfill");
                return false;
            }

            Log($"[BACKFILL] 🚀 Starting remote service backfill for {instrument} with {historicalBars.Count} bars");
            
            // Send historical bars to remote MI service
            bool backfillSuccess = await SendHistoricalBarsForBackfill(historicalBars, instrument, true);
            
            if (backfillSuccess)
            {
                Log($"[BACKFILL] ✅ Remote service backfill completed successfully for {instrument}");
                Log($"[BACKFILL] Remote MI service should now have sufficient buffer to start pattern matching");
                return true;
            }
            else
            {
                Log($"[BACKFILL] ❌ Remote service backfill failed for {instrument}");
                return false;
            }
        }
    }
} 