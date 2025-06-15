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
		public static string CurrentPatternType { get; private set; }
        public static string PatternName { get; private set; }
        public static DateTime LastSignalTimestamp { get; private set; }
        public static double CurrentAvgSlope { get; private set; }
        public static int CurrentSlopeImpact { get; private set; }
        public static List<PatternMatch> CurrentMatches { get; private set; } = new List<PatternMatch>();
        public static bool SignalsAreFresh => (DateTime.Now - LastSignalTimestamp).TotalSeconds < 30;
        
        // Add DTW Service URL for divergence tracking
        private readonly string meServiceUrl = "http://localhost:5000"; // Use local ME service for orange line API
        
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
		
		        // Add Thompson score caching
        public Dictionary<string,double> thompsonScores = new Dictionary<string,double>();
        
        // Orange Line data caching
        public static double CurrentOrangeLine { get; private set; } = double.NaN;
        public static double CurrentOrangeLineDeviation { get; private set; } = double.NaN;
        public static string CurrentOrangeLineSignal { get; private set; } = "NONE";
        public static double CurrentOrangeLineConfidence { get; private set; } = 0.0;
        public static DateTime LastOrangeLineUpdate { get; private set; } = DateTime.MinValue;
		
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
            client.Timeout = TimeSpan.FromSeconds(10); // Default timeout
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
                string meUrl = getMeServiceUrl();
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
        public bool SendHeartbeat(bool useRemoteService = false)
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
        public bool CheckAndSendHeartbeat(bool useRemoteService = false)
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
								Log($" Request Success {responseText}");
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
							
								CurrentOppositionStrength = signalPoolResponse.signals.Max(s => s.oppositionStrength);
								CurrentConfluenceScore = signalPoolResponse.signals.Max(s => s.confluenceScore);
								CurrentEffectiveScore = signalPoolResponse.signals.Max(s => s.effectiveScore);
								CurrentStopModifier = signalPoolResponse.signals.Max(s => s.stopModifier);
                                CurrentPullbackModifier = signalPoolResponse.signals.Max(s => s.pullbackModifier);
                                CurrentRawScore = signalPoolResponse.signals.Max(s => s.rawScore);
                                
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

		// NEW: Synchronous version that blocks and returns score immediately for BuildNewSignal
		public double CheckSignalsSync(bool useRemoteService, DateTime dt, string instrument, double? minRawScore = null, double? effectiveScoreRequirement = null)
		{
		    if (IsDisposed() || string.IsNullOrEmpty(instrument) || client == null)
		        return 0;
			
			// Check concurrent request limit
			if (concurrentRequests >= MAX_CONCURRENT_REQUESTS)
				return 0;

			try
			{
				// Build endpoint with better filtering
				string microServiceUrl = useRemoteService ? "https://curves-signal-pool-service.onrender.com" : signalPoolUrl;
				string endpoint = $"{microServiceUrl}/api/signals/available?strategyId={sessionID}&instrument={instrument}";
				
				// Add minimum score filter if provided
				if (minRawScore.HasValue)
					endpoint += $"&minScore={minRawScore.Value}";
				
				//Log($"[SYNC-SIGNALS] Requesting: {endpoint}");
				
				// Synchronous HTTP request with timeout
				using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
				{
					var response = client.GetAsync(endpoint, timeoutCts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
					
					if (response.IsSuccessStatusCode)
					{
						string responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
						var signalPoolResponse = JsonConvert.DeserializeObject<SignalPoolResponse>(responseText);
						
						if (signalPoolResponse?.success == true && signalPoolResponse.signals?.Count > 0)
						{
							//Log($"[SYNC-SIGNALS] Found {signalPoolResponse.signals.Count} signals");
							
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
									continue;
								
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
								LastSignalTimestamp = DateTime.UtcNow;
								CurrentContextId = "signal-" + DateTime.Now.Ticks;
								
								// Determine direction and return signed score
								bool isBull = bestSignal.type == "bull" || 
											 (bestSignal.direction?.ToLower() == "long") || 
											 (bestSignal.type?.ToLower().Contains("bull") == true);
								
								double finalScore = Math.Max(bestSignal.effectiveScore, bestSignal.rawScore);
								
								if (isBull)
								{
									CurrentBullStrength = finalScore;
									CurrentBearStrength = 0;
									//Log($"[SYNC-SIGNALS] BULL signal: Score={finalScore:F4}, Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
									return finalScore; // Positive for bull
								}
								else
								{
									CurrentBullStrength = 0;
									CurrentBearStrength = finalScore;
									//Log($"[SYNC-SIGNALS] BEAR signal: Score={finalScore:F4}, Pattern={bestSignal.patternType}, ID={bestSignal.patternId}");
									return -finalScore; // Negative for bear
								}
							}
						}
						
						// No valid signals found
						//Log($"[SYNC-SIGNALS] No valid signals found for {instrument}");
						ResetCurrentSignalProperties();
						return 0;
					}
					else
					{
						//Log($"[SYNC-SIGNALS] HTTP error: {response.StatusCode}");
						return 0;
					}
				}
			}
			catch (Exception ex)
			{
				Log($"[SYNC-SIGNALS] Error: {ex.Message}");
				return 0;
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
						//Log($"SendBarSync {jsonPayload}");
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
            CurrentOrangeLine = double.NaN;
            CurrentOrangeLineDeviation = double.NaN;
            CurrentOrangeLineSignal = "NONE";
            CurrentOrangeLineConfidence = 0.0;
            LastOrangeLineUpdate = DateTime.MinValue;
            
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

        // Orange Line API integration
        public async Task<bool> UpdateOrangeLineAsync(string instrument, List<double> priceHistory, bool useRemoteService = false)
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument) || priceHistory == null || priceHistory.Count < 10)
                return false;

            try
            {
                // Build request data
                var requestData = new
                {
                    instrument = instrument,
                    prices = priceHistory.ToArray(),
                    currentBar = priceHistory.Count
                };

                string jsonData = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
				
                // Use ME service orange line endpoint
                
				string meServiceUrl = getMeServiceUrl();
				string endpoint =  $"{meServiceUrl}/api/orange-line/analyze";
                                Log($"UpdateOrangeLineAsync endpoint {endpoint} request {jsonData}");
                //Log($"[ORANGE-LINE-DEBUG] Sending {priceHistory.Count} prices: [{string.Join(", ", priceHistory.TakeLast(5).Select(p => p.ToString("F2")))}]");
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                    //Log($"UpdateOrangeLineAsync response {response}");
                    if (response.IsSuccessStatusCode)
                    {
                        string responseJson = await response.Content.ReadAsStringAsync();
                        Log($"[ORANGE-LINE-DEBUG] API Response: {responseJson}");
                        var result = JsonConvert.DeserializeObject<dynamic>(responseJson);
                        
                        // Update static properties
                        if (result != null)
                        {
                            // Extract orange line value
                            if (result.orangeLine != null && double.TryParse(result.orangeLine.ToString(), out double orangeLine))
                            {
                                CurrentOrangeLine = result.orangeLine;
                            }
                            
                            // Extract deviation
                            if (result.deviation != null)
                            {
                                if (double.TryParse(result.deviation.ToString(), out double deviation))
                                {
                                    CurrentOrangeLineDeviation = Math.Abs(deviation);
                                }
                                else
                                {
                                    // Fallback: try to convert directly if it's already a numeric type
                                    try
                                    {
                                        CurrentOrangeLineDeviation = Math.Abs(Convert.ToDouble(result.deviation));
                                    }
                                    catch
                                    {
                                        Log($"[ORANGE-LINE] Warning: Could not parse deviation value: {result.deviation}");
                                    }
                                }
                            }
                            
                            // Extract signal
                            if (result.signal != null)
                            {
                                CurrentOrangeLineSignal = result.signal.ToString();
                            }
                            
                            // Extract confidence
                            if (result.confidence != null && double.TryParse(result.confidence.ToString(), out double confidence))
                            {
                                CurrentOrangeLineConfidence = result.confidence;
                            }
                            
                            LastOrangeLineUpdate = DateTime.Now;
                            
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed())
                {
                    Log($"[ORANGE-LINE] Error updating orange line data: {ex.Message}");
                }
            }
            
            return false;
        }

        // Get existing orange line data from ME service (no price data sent)
        public bool GetOrangeLineData(string instrument, bool useRemoteService = false)
        {
            //Log($"[ORANGE-LINE-DEBUG] GetOrangeLineData called for {instrument}, useRemoteService={useRemoteService}");
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    var result = GetOrangeLineDataAsync(instrument, useRemoteService).ConfigureAwait(false).GetAwaiter().GetResult();
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            await GetOrangeLineDataAsync(instrument, useRemoteService);
                        }
                        catch (Exception ex)
                        {
                            Log($"[ORANGE-LINE] Background data fetch failed: {ex.Message}");
                        }
                    });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[ORANGE-LINE] Error in GetOrangeLineData: {ex.Message}");
                return false;
            }
        }

        // Get existing orange line data from ME service without sending price data
        public async Task<bool> GetOrangeLineDataAsync(string instrument, bool useRemoteService = false)
        {
            if (IsDisposed())
                return false;

            try
            {
                // Use ME service orange line endpoint to get existing data
                string meServiceUrl = getMeServiceUrl();
                string endpoint = $"{meServiceUrl}/api/orange-line/current/{instrument}";
                
               // Log($"[ORANGE-LINE-DEBUG] Requesting existing data from {endpoint}");
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await client.GetAsync(endpoint, timeoutCts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseJson = await response.Content.ReadAsStringAsync();
                       // Log($"[ORANGE-LINE-DEBUG] API Response: {responseJson}");
                        var result = JsonConvert.DeserializeObject<dynamic>(responseJson);
                        
                        // Update static properties with existing data
                        if (result != null)
                        {
                            // Extract orange line value
                            if (result.orangeLine != null && double.TryParse(result.orangeLine.ToString(), out double orangeLine))
                            {
                                CurrentOrangeLine = result.orangeLine;
                            }
                            
                            // Extract deviation
                            if (result.deviation != null)
                            {
                                if (double.TryParse(result.deviation.ToString(), out double deviation))
                                {
                                    CurrentOrangeLineDeviation = Math.Abs(deviation);
                                }
                                else
                                {
                                    try
                                    {
                                        CurrentOrangeLineDeviation = Math.Abs(Convert.ToDouble(result.deviation));
                                    }
                                    catch
                                    {
                                        Log($"[ORANGE-LINE] Warning: Could not parse deviation value: {result.deviation}");
                                    }
                                }
                            }
                            
                            // Extract signal (if available)
                            if (result.signal != null)
                            {
                                CurrentOrangeLineSignal = result.signal.ToString();
                            }
                            
                            // Extract confidence (if available)
                            if (result.confidence != null)
                            {
                                if (double.TryParse(result.confidence.ToString(), out double confidence))
                                {
                                    CurrentOrangeLineConfidence = confidence;
                                }
                                else
                                {
                                    try
                                    {
                                        CurrentOrangeLineConfidence = Convert.ToDouble(result.confidence);
                                    }
                                    catch
                                    {
                                        Log($"[ORANGE-LINE] Warning: Could not parse confidence value: {result.confidence}");
                                        CurrentOrangeLineConfidence = 0.0; // Default fallback
                                    }
                                }
                            }
                            
                            LastOrangeLineUpdate = DateTime.Now;
                            
                            //Log($"[ORANGE-LINE] Retrieved existing data: Line={CurrentOrangeLine:F2}, Deviation={CurrentOrangeLineDeviation:F2}, Signal={CurrentOrangeLineSignal}, Confidence={CurrentOrangeLineConfidence:P1}");
                            return true;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Log($"[ORANGE-LINE] No existing orange line data available for {instrument} - ME service may need time to calculate");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed())
                {
                    Log($"[ORANGE-LINE] Error getting existing orange line data: {ex.Message}");
                }
            }
            
            return false;
        }

        // DEPRECATED: Keep for backward compatibility but mark as obsolete
        [Obsolete("Use GetOrangeLineData instead - ME service already calculates orange line internally")]
        public bool UpdateOrangeLine(string instrument, List<double> priceHistory, bool useRemoteService = false)
        {
            //Log($"[ORANGE-LINE-DEBUG] UpdateOrangeLine called with {priceHistory.Count} prices, useRemoteService={useRemoteService} - DEPRECATED, use GetOrangeLineData instead");
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    var result = UpdateOrangeLineAsync(instrument, priceHistory,useRemoteService).ConfigureAwait(false).GetAwaiter().GetResult();
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            await UpdateOrangeLineAsync(instrument, priceHistory);
                        }
                        catch (Exception ex)
                        {
                            Log($"[ORANGE-LINE] Background update failed: {ex.Message}");
                        }
                    });
                    
                    // Return success immediately (fire-and-forget)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[ORANGE-LINE] Error in UpdateOrangeLine: {ex.Message}");
                return false;
            }
        }

        // Add a method to submit pattern performance record
        public async Task<bool> RecordPatternPerformanceAsync(PatternPerformanceRecord record, bool useRemoteService = false)
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
        public async Task<bool> SendMatchingConfigAsync(string instrument, MatchingConfig config,bool useRemoteService = false)
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
                    Log($"UpdateOrangeLineAsync response {response}");
                    
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
                    var result = SendMatchingConfigAsync(instrument, config).ConfigureAwait(false).GetAwaiter().GetResult();
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
                            await SendMatchingConfigAsync(instrument, config);
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
        public async Task<bool> RegisterPositionAsync(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp, double entryPrice = 0.0, string direction = "long", double[] originalForecast = null)
        {
            return await RegisterPositionWithDataAsync(entrySignalId, patternUuid, instrument, entryTimestamp, entryPrice, direction, originalForecast);
        }

        // Legacy registration method for compatibility
        public async Task<bool> RegisterPositionAsync(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp)
        {
            return await RegisterPositionWithDataAsync(entrySignalId, patternUuid, instrument, entryTimestamp, 0.0, "long", null);
        }

        // Core registration method with full Dynamic Risk Alignment data
        private async Task<bool> RegisterPositionWithDataAsync(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp, double entryPrice, string direction, double[] originalForecast,bool useRemoteService = false)
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
                        patternUuid = patternUuid,
                        instrument = instrument,
                        entryTimestamp = isoTimestamp,
                        entryPrice = entryPrice, // Actual entry price from strategy
                        direction = direction, // Actual direction from strategy  
                        originalForecast = originalForecast ?? new double[] { }, // Forecast from Curved Prediction Service
                        marketContext = new { // Current market context
                            timestamp = isoTimestamp,
                            sessionId = sessionID
                        }
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

        // Enhanced synchronous registration with entry price and direction - CONDITIONAL SYNC/ASYNC
        public bool RegisterPosition(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp, double entryPrice = 0.0, string direction = "long", double[] originalForecast = null)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] RegisterPosition starting sync call for {entrySignalId}");
                    var result = RegisterPositionAsync(entrySignalId, patternUuid, instrument, entryTimestamp, entryPrice, direction, originalForecast).ConfigureAwait(false).GetAwaiter().GetResult();
                    Log($"[HISTORICAL-SYNC] RegisterPosition completed sync call for {entrySignalId}");
                    return result;
                }
                else
                {
                    // REAL-TIME: Use fire-and-forget to prevent freezes
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] RegisterPosition starting background registration for {entrySignalId}");
                            await RegisterPositionAsync(entrySignalId, patternUuid, instrument, entryTimestamp, entryPrice, direction, originalForecast);
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
        public bool RegisterPosition(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] Legacy RegisterPosition starting sync call for {entrySignalId}");
                    var result = RegisterPositionAsync(entrySignalId, patternUuid, instrument, entryTimestamp).ConfigureAwait(false).GetAwaiter().GetResult();
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
                            await RegisterPositionAsync(entrySignalId, patternUuid, instrument, entryTimestamp);
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
		    var requestTask = PerformDynamicRiskRequestAsync(entrySignalId, currentPrice);
		    
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

		// UPDATED: Dynamic Risk Alignment with forecast recalculation and 3-strike rule
		// Returns binary divergence score from intelligent risk assessment
		private async Task<double> PerformDynamicRiskRequestAsync(string entrySignalId, double currentPrice = 0.0,bool useRemoteService = false)
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

        // Batch method to update divergence for all active positions - call this once per bar
        public async Task UpdateAllDivergenceScoresAsync(double currentPrice = 0.0)
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
                    //logger?.Invoke($"[DEBUG DIVERGENCE] Creating task for position: {simStop.EntryOrderUUID}");
                    updateTasks.Add(CheckDivergenceAsync(simStop.EntryOrderUUID, currentPrice));
                }
            }
            
            //logger?.Invoke($"[DEBUG DIVERGENCE] About to await {updateTasks.Count} tasks with 10s timeout");
            
            // Wait for all updates to complete (with reasonable timeout)
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    //logger?.Invoke($"[DEBUG DIVERGENCE] Starting Task.WhenAll...");
                    await Task.WhenAll(updateTasks).ConfigureAwait(false);
                    //logger?.Invoke($"[DEBUG DIVERGENCE] Task.WhenAll completed successfully");
                }
            }
            catch (Exception ex)
            {
                //logger?.Invoke($"[DEBUG DIVERGENCE] ERROR in Task.WhenAll: {ex.Message}");
                Log($"[DIVERGENCE] Error updating batch divergence scores: {ex.Message}");
            }
            
            //logger?.Invoke($"[DEBUG DIVERGENCE] UpdateAllDivergenceScoresAsync END");
        }

        // Deregister a position when it's closed
        public async Task<bool> DeregisterPositionAsync(string entrySignalId, bool wasGoodExit = false, double finalDivergenceP = 0.0, bool useRemoteService = false)
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
                        finalDivergence = finalDivergenceP
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
                            // Remove from local registry
                            if (activePositions.ContainsKey(entrySignalId))
                            {
                                activePositions.Remove(entrySignalId);
                                //Log($"[DIVERGENCE] Successfully deregistered position {entrySignalId} with wasGoodExit={wasGoodExit}, finalDivergence={finalDivergenceP}");
                            }
                            
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

        // Synchronous wrapper for DeregisterPositionAsync - CONDITIONAL SYNC/ASYNC
        public bool DeregisterPosition(string entrySignalId, bool wasGoodExit = false, double finalDivergence = 0.0)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // CONDITIONAL LOGIC: Sync for Historical (backtesting), Async for Real-time
                if (IsHistoricalMode())
                {
                    // BACKTESTING: Use synchronous calls for sequential bar processing
                    Log($"[HISTORICAL-SYNC] DeregisterPosition starting sync call for {entrySignalId}");
                    var result = DeregisterPositionAsync(entrySignalId, wasGoodExit, finalDivergence).ConfigureAwait(false).GetAwaiter().GetResult();
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
                    
                    // Fire-and-forget async deregistration in background
                    Task.Run(async () => {
                        try
                        {
                            Log($"[REALTIME-ASYNC] DeregisterPosition starting background deregistration for {entrySignalId}");
                            await DeregisterPositionAsync(entrySignalId, wasGoodExit, finalDivergence);
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
    }
} 