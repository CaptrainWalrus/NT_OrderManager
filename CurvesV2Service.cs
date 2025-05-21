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
    
        // WebSocket related fields
        private ClientWebSocket webSocket;
        private bool webSocketConnected = false;
        public int ErrorCounter = 0;
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
		}
		
		// Add class for DTW match data
		public class DtwMatchData
		{
		    public double score { get; set; }
		    public double avgDistance { get; set; }
		    public int validPeriods { get; set; }
		}
		
		// Add a new property to get Signal Pool URL
		private readonly string signalPoolUrl = "http://localhost:3004";
		public static string CurrentSubtype { get; private set; }
		public static long CurrentRequestTimestampEpoch { get; private set; }
		public static double CurrentBullStrength { get; private set; }
        public static double CurrentBearStrength { get; private set; }
		public static double CurrentOppositionStrength { get; private set; }
		public static double CurrentConfluenceScore { get; private set; }
		public static double CurrentEffectiveScore { get; private set; }
		public static double CurrentRawScore { get; private set; }
		public static string CurrentPatternType { get; private set; }
        public static string PatternName { get; private set; }
        public static DateTime LastSignalTimestamp { get; private set; }
        public static double CurrentAvgSlope { get; private set; }
        public static int CurrentSlopeImpact { get; private set; }
        public static List<PatternMatch> CurrentMatches { get; private set; } = new List<PatternMatch>();
        public static bool SignalsAreFresh => (DateTime.Now - LastSignalTimestamp).TotalSeconds < 30;
        
        // Add DTW Service URL for divergence tracking
        private readonly string meServiceUrl = "http://localhost:5000"; // Adjust port as needed
        
        // Divergence tracking properties
        public static double CurrentDivergenceScore { get; private set; } = 0;
        public static int CurrentBarsSinceEntry { get; private set; } = 0;
        
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
		
		// Add error tracking
		private readonly Dictionary<string, int> positionErrorCounts = new Dictionary<string, int>();
		private readonly Dictionary<string, DateTime> positionErrorCooldowns = new Dictionary<string, DateTime>();
		private const int MAX_ERRORS_BEFORE_DEREGISTER = 5;
		private const int ERROR_COOLDOWN_SECONDS = 10;
		private const int MAX_BARS_TO_CHECK = 50; // Limit the number of bars to check for divergence

        // Class to represent a position in the divergence tracking system
        private class RegisteredPosition
        {
            public string PatternUuid { get; set; }
            public string Instrument { get; set; }
            public DateTime EntryTimestamp { get; set; }
            public int BarsSinceEntry { get; set; }
            public DateTime RegistrationTimestamp { get; set; }
        }

        // Constructor
        public CurvesV2Service(CurvesV2Config config, Action<string> logger = null)
        {
            this.config = config;
            this.baseUrl = config.GetSignalApiEndpoint();
            this.logger = logger ?? ((msg) => { NinjaTrader.Code.Output.Process(msg, PrintTo.OutputTab1); });
            this.disposed = false;
            
            // Initialize HttpClient
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(10); // Default timeout
            
            // Initialize visualization client
            visualizationClient = new HttpClient();
            visualizationClient.DefaultRequestHeaders.Accept.Clear();
            visualizationClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            visualizationClient.Timeout = TimeSpan.FromSeconds(2); // Short timeout for visualization

			sessionID = Guid.NewGuid().ToString();
			CurrentContextId = null;
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
                string healthEndpoint = $"{baseUrl}/api/health";
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var response = await client.GetAsync(healthEndpoint, timeoutCts.Token);
                    bool isHealthy = response.IsSuccessStatusCode;
                    response.Dispose();
                    Log($"[INFO] Health check result: {response.StatusCode} (Success: {isHealthy})");
                    return isHealthy;
                }
                }
                catch (Exception ex)
                {
                bool wasDisposed; 
                lock(disposeLock) { wasDisposed = disposed; }
                if (!wasDisposed) {
                   Log($"[ERROR] Health check failed: {ex.GetType().Name} - {ex.Message}");
                }
                return false;
            }
        }

        // Synchronous Bar Sending
        public bool SendBarSync(BarDataPacket barData)
        {
             lock(disposeLock) { if(disposed) return false; }

            if (barData == null || string.IsNullOrEmpty(barData.Instrument))
            {
                Log("[WARN] SendBarSync: Invalid BarDataPacket provided.");
                return false;
            }

            string endpoint = $"{baseUrl}/api/bars/{barData.Instrument}";
            HttpResponseMessage response = null;

            try
            {
                Log($"[DEBUG] SendBarSync: Sending bar for {barData.Instrument} @ {barData.Timestamp} to {endpoint}");

                long epochMs = (long)(barData.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                var payloadObject = new {
                    symbol = barData.Instrument,
                    timestamp = epochMs,
                    open = barData.Open,
                    high = barData.High,
                    low = barData.Low,
                    close = barData.Close,
                    volume = barData.Volume,
                    timeframe = barData.Timeframe
                };
                string jsonPayload = JsonConvert.SerializeObject(payloadObject);
                
                using (StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) 
                {
                    // BLOCKING CALL
                    response = client.PostAsync(endpoint, content, timeoutCts.Token).GetAwaiter().GetResult();
                }

                Log($"[DEBUG] SendBarSync: Received status {response.StatusCode} for {barData.Instrument}");

                string responseContent = ""; // Read content regardless of status code for logging
                try { 
                    // BLOCKING CALL
                    responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); 
                }
                catch { /* Ignore read errors */ }

                if (!response.IsSuccessStatusCode)
                {
                     Log($"[ERROR] SendBarSync: Failed to send bar for {barData.Instrument}. Status: {response.StatusCode}. Response: {responseContent.Substring(0, Math.Min(responseContent.Length, 200))}");
                     return false;
                }

                Log($"[DEBUG] SendBarSync: Successfully sent bar for {barData.Instrument}");
                return true;
            }
            catch (OperationCanceledException) // Catches timeout from CancellationTokenSource
            {
                 bool wasDisposed; 
                 lock(disposeLock) { wasDisposed = disposed; }
                 if (!wasDisposed) {
                    Log($"[ERROR] SendBarSync: Request timed out sending bar for {barData.Instrument} (3s)");
                 }
                 return false;
            }
            catch (Exception ex)
            {
                 bool wasDisposed; 
                 lock(disposeLock) { wasDisposed = disposed; }
                 if (!wasDisposed) {
                    Log($"[ERROR] SendBarSync: Exception sending bar for {barData.Instrument}: {ex.GetType().Name} - {ex.Message}");
                    if(ex.InnerException != null) Log($"  Inner: {ex.InnerException.Message}");
                 }
                 return false;
            }
            finally
            {
                 response?.Dispose(); 
            }
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
                const int batchSize = 100;  // Send 100 bars at a time
                
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
                    string endpoint = $"{baseUrl}/api/bars/{baseInstrument}";
                    
                    Log($"Sending batch {i}/{bars.Count} bars to {endpoint}");

                    var content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = await client.PostAsync(endpoint, content);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Server returned error: {response.StatusCode} - {responseContent}");
                        return false;
                    }
                    
                    // Parse response for timeframe information
                    try
                    {
                        dynamic responseObj = JsonConvert.DeserializeObject(responseContent);
                        if (responseObj != null && responseObj.timeframe != null)
                        {
                            Log($"Server processed bars with timeframe: {responseObj.timeframe.minFormatted} to {responseObj.timeframe.maxFormatted}");
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors, just continue
                    }
                    
                    // Brief delay between batches
                    await Task.Delay(100);
                    
                    // Log progress every 500 bars
                    if (i % 500 == 0 && i > 0)
                    {
                        Log($"Sent {i} of {bars.Count} bars...");
                    }
                }
                
                Log($"Successfully sent all {bars.Count} historical bars for {baseInstrument}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to send historical bars: {ex.Message}");
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
        public bool SendBarFireAndForget(bool useRemoteService,string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
		    return false;
            
            try
            {
                // Log the attempt
                //Log($"[DEBUG HTTP] SendBarFireAndForget: Called for {instrument} @ {timestamp}");
                
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
				CurrentRequestTimestampEpoch = epochMs;
                // --- START TEMPORARY DEBUG: Force HTTP Fallback ---
                // Always skip WebSocket path for debugging
                /* 
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                    // ... (original WebSocket send logic) ...
                    return true;
                }
                else
                { 
                     Log($"SendBarFireAndForget: WebSocket not connected or null. State: {webSocket?.State}. Using HTTP fallback.");
                } 
                */
                //Log($"[DEBUG HTTP] SendBarFireAndForget: Skipping WebSocket, forcing HTTP.");
                // --- END TEMPORARY DEBUG ---

                // Fallback to HTTP
                // Build the URL for the endpoint
				
				
				string microServiceUrl = useRemoteService == true ? "https://curves-monolith.onrender.com" : baseUrl;
				string endpoint = $"{microServiceUrl}/api/ingest/bars/{instrument}";
                //string endpoint = $"{microServiceUrl}/api/realtime_bars/{instrument}/backtest?matchPatterns=true&sessionId={sessionID}"; // Use /api/realtime_bars endpoint for NinjaTrader
                // Create payload
                string jsonPayload = JsonConvert.SerializeObject(new {
                     timestamp = epochMs,
                     open = open,
                     high = high,
                     low = low,
                     close = close,
                     volume = volume,
                     timeframe = timeframe,
                     symbol = instrument // Add symbol for server processing
                });
                byte[] data = Encoding.UTF8.GetBytes(jsonPayload);
                
                // Create a non-blocking request
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.ContentLength = data.Length;
                request.Timeout = 3000; // 3 second timeout
                //Log($" SendFireAndForget: {endpoint} with payload {jsonPayload}");
                // Begin the request without blocking
                try
                {
                    // Get request stream asynchronously but don't wait
                    IAsyncResult asyncResult = request.BeginGetRequestStream(
                        ar => {
                            try
                            {
                                if (IsDisposed() || IsShuttingDown()) return;
                                using (Stream requestStream = request.EndGetRequestStream(ar))
                                {
                                    requestStream.Write(data, 0, data.Length);
                                }
                                request.BeginGetResponse(
                                    responseResult => {
                                        try
                                        {
                                            if (IsDisposed() || IsShuttingDown()) return;
                                            using (WebResponse response = request.EndGetResponse(responseResult))
                                            {
                                                //Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP bar data sent successfully for {instrument}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            if (IsDisposed() || IsShuttingDown()) return;
                                            Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP response error for {instrument} at {endpoint}: {ex.Message}");
                                        }
                                    },
                                    null 
                                );
                            }
                            catch (Exception ex)
                            {
                                if (IsDisposed() || IsShuttingDown()) return;
                                Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP request stream error for {instrument}: {ex.Message}");
                            }
                        },
                        null 
                    );
                    
                    //Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP request initiated for {instrument}");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[DEBUG HTTP] SendBarFireAndForget: Error creating HTTP request for {instrument}: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[DEBUG HTTP] SendBarFireAndForget: Unexpected error for {instrument}: {ex.Message}");
                return false;
            }
        }
		


		// Simplified CheckSignalsFireAndForget method - just gets and stores bull/bear values
		public bool CheckSignalsFireAndForget(bool useRemoteService, DateTime dt, string instrument, int? pattern_size = null, double? minRawScore = null, double? effectiveScoreRequirement = null, string patternId = null, string subtype = null)
		{
		    if (IsDisposed() || string.IsNullOrEmpty(instrument) || client == null)
		        return false;
			
			string timestamp = dt.ToString();

			CurrentOppositionStrength = 0;
			CurrentConfluenceScore = 0;
			CurrentRawScore = 0;
			CurrentEffectiveScore = 0;
			CurrentPatternId = null; // Reset the pattern ID
			
			HttpResponseMessage response = null;
		    try
		    {
				// Inside CheckSignalsFireAndForget method
				string timestampIso = dt.ToUniversalTime().ToString("o");
				string encodedTimestamp = Uri.EscapeDataString(timestampIso);
		        // Build simple endpoint URL
				
				string microServiceUrl = useRemoteService == true ? "https://curves-signal-pool-service.onrender.com" : signalPoolUrl;

				
		        StringBuilder endpointBuilder = new StringBuilder($"{microServiceUrl}/api/signals/available?instrument={instrument}");
		        
		        // Add optional filters if specified
		       // if (pattern_size.HasValue)
		         //   endpointBuilder.Append($"&pattern_size={pattern_size.Value}");

				 // <<< ADDED: Append subtype filter if provided >>>
		       // if (!string.IsNullOrEmpty(subtype))
		       //     endpointBuilder.Append($"&signalSubtype={Uri.EscapeDataString(subtype)}"); 
		        // <<< END ADDED >>>
				
				//&barTimestamp={encodedTimestamp}
				
		        //if (minRawScore.HasValue)
		        //    endpointBuilder.Append($"&minRawScore={minRawScore.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
		        
		        //if (effectiveScoreRequirement.HasValue)
		        //    endpointBuilder.Append($"&minEffectiveScore={effectiveScoreRequirement.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
		        
		        
		        string endpoint = endpointBuilder.ToString();
		        
		        // Log request (but less verbose)
		        //Log($"[SIGNAL] Requesting signals for {instrument} from {endpoint}");
		        
		        if (IsDisposed() || IsShuttingDown())
		            return false; 
		            
		        // Get response with timeout
		        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) 
		        {
		            response = client.GetAsync(endpoint, timeoutCts.Token).GetAwaiter().GetResult(); 
		        }

		        if (response.IsSuccessStatusCode)
		        {
		            string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		            //Log($"responseText [{dt}] {responseText}");
		            // Parse the response
		            var signalPoolResponse = JsonConvert.DeserializeObject<SignalPoolResponse>(responseText);
		            
		            if (signalPoolResponse != null && signalPoolResponse.success && 
		                signalPoolResponse.signals != null && signalPoolResponse.signals.Count > 0) 
		            {
		                // MODIFIED: Classify signals as bull or bear based on type or direction 
		                var bullSignals = signalPoolResponse.signals.Where(s => 
                            s.type == "bull" || 
                            (s.direction != null && s.direction.ToLower() == "long") || 
                            (s.type != null && s.type.ToLower().Contains("bull"))
                        ).ToList();
                        
		                var bearSignals = signalPoolResponse.signals.Where(s => 
                            s.type == "bear" || 
                            (s.direction != null && s.direction.ToLower() == "short") || 
                            (s.type != null && s.type.ToLower().Contains("bear"))
                        ).ToList();
                        
                        // If we still don't have bull/bear signals, try to infer from the pattern type
                        if (bullSignals.Count == 0 && bearSignals.Count == 0)
                        {
                            // Default categorization based on first signal if we can't determine otherwise
                            if (signalPoolResponse.signals.Count > 0)
                            {
                                var signal = signalPoolResponse.signals[0];
                                Log($"Inferring direction from signal type: {signal.type}");
                                
                                // Handle specific pattern types we know about
                                if (signal.type == "EMARibbonHarmonics")
                                {
                                    // For EMARibbonHarmonics, let's consider it bullish by default
                                    // We can add more sophisticated logic based on other properties if available
                                    Log($"EMARibbonHarmonics pattern detected - treating as BULL signal with rawScore={signal.rawScore}");
                                    bullSignals.Add(signal);
                                }
                                // Add other known pattern types here
                                else if (signal.type != null)
                                {
                                    // By default, add to bull signals if we can't determine
                                    Log($"Unknown pattern type '{signal.type}' - defaulting to BULL signal");
                                    bullSignals.Add(signal);
                                }
                            }
                        }
		               
		                // Calculate highest bull/bear scores (convert to percentage)
		                double bullScore_raw = bullSignals.Any() ? bullSignals.Max(s => s.rawScore) * 100 : 0;
		                double bearScore_raw = bearSignals.Any() ? bearSignals.Max(s => s.rawScore) * 100 : 0; 
		                
						// Calculate highest bull/bear scores (convert to percentage)
		                double bullScore_eff = bullSignals.Any() ? bullSignals.Max(s => s.effectiveScore) * 100 : 0; // changed from rawScore effectiveScore
		                double bearScore_eff = bearSignals.Any() ? bearSignals.Max(s => s.effectiveScore) * 100 : 0; // changed from rawScore effectiveScore
					    
						double bullScore = Math.Max(bullScore_eff,bullScore_raw);
						double bearScore = Math.Max(bearScore_eff,bearScore_raw);						
						
						// Extract the highest opposition strength and confluence score
						//oppositionStrength = 0;
						//confluenceScore = 0;
						//effectiveScore = 0;
						
						// Check if we have any signals and extract the values
						if (signalPoolResponse.signals.Any())
						{
							var firstSignal = signalPoolResponse.signals[0]; // Get the first/prioritized signal
						    CurrentPatternType = firstSignal.patternType;
							CurrentSubtype = firstSignal.subtype; // <<< STORE SUBTYPE
						    CurrentPatternId = firstSignal.patternId; // <<< STORE PATTERN ID
						    // Get the highest opposition strength and confluence score from any signal
						    CurrentOppositionStrength = signalPoolResponse.signals.Max(s => s.oppositionStrength);
						    CurrentConfluenceScore = signalPoolResponse.signals.Max(s => s.confluenceScore);
							CurrentEffectiveScore = signalPoolResponse.signals.Max(s => s.effectiveScore);
							CurrentRawScore = signalPoolResponse.signals.Max(s => s.rawScore);
							// Extract DTW match data if available
						
						}
					
		                // Update static properties with the new values
		                CurrentBullStrength = bullScore;
		                CurrentBearStrength = bearScore;
		                LastSignalTimestamp = DateTime.UtcNow;
		                CurrentContextId = "signal-" + DateTime.Now.Ticks; // Simple unique context
		                
		                // Log signal values with patternId for debugging
		                if (bullScore > 0 || bearScore > 0)
		                {
		                    //Log($"[{dt}] [SIGNAL] Values - [PatternId] {CurrentPatternId}, [Bull] {bullScore:F2}%, [Bear]: {bearScore:F2}%, [EffectiveScore] {CurrentEffectiveScore:F2}");
		                }
		                
		                return true;
		            }
		            else
		            {
		                // No signals found - reset values
		                CurrentPatternType = null; // <<< RESET
		                CurrentSubtype = null; // <<< RESET
		                CurrentPatternId = null; // <<< RESET
		                CurrentBullStrength = 0;
		                CurrentBearStrength = 0;
		                return false;
		            }
		        }
		    }
		    catch (Exception ex)
		    {
		        if (!IsDisposed() && !IsShuttingDown())
		            Log($"[ERROR] Checking signals for {instrument}: {ex.Message}");
					if(ErrorCounter < 11)
					{
						ErrorCounter++;
					}
		    }
		    finally
		    {
		        response?.Dispose(); 
		    }
		    
		    return false; 
		}

		
		
        // Add synchronous methods for direct NinjaTrader integration
        
        // Synchronously send a bar and wait for confirmation
        public bool SendBarSync(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            NinjaTrader.Code.Output.Process("SendBarSync: Entered method", PrintTo.OutputTab1);
            
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
            {
                NinjaTrader.Code.Output.Process($"SendBarSync: Exiting early - Disposed={IsDisposed()}, Instrument null/empty={string.IsNullOrEmpty(instrument)}", PrintTo.OutputTab1);
                return false;
            }
            
            try
            {
                NinjaTrader.Code.Output.Process($"SendBarSync: Sending bar for {instrument} @ {timestamp}", PrintTo.OutputTab1);
                
                // Check if WebSocket is connected
                NinjaTrader.Code.Output.Process($"SendBarSync: Checking WebSocket state (Current: {webSocket?.State})", PrintTo.OutputTab1);
                if (webSocket == null || webSocket.State != WebSocketState.Open)
                {
                    NinjaTrader.Code.Output.Process("SendBarSync: WebSocket not connected, attempting to connect...", PrintTo.OutputTab1);
                    
                    // Try to connect asynchronously, but wait synchronously
                    NinjaTrader.Code.Output.Process("SendBarSync: Calling ConnectWebSocketAsync...", PrintTo.OutputTab1);
                    var connectTask = ConnectWebSocketAsync();
                    NinjaTrader.Code.Output.Process("SendBarSync: Waiting for ConnectWebSocketAsync (max 3s)...", PrintTo.OutputTab1);
                    if (!connectTask.Wait(3000)) // Wait up to 3 seconds with timeout
                    {
                        NinjaTrader.Code.Output.Process("SendBarSync: WebSocket connection timed out", PrintTo.OutputTab1);
                        return false;
                    }
                    NinjaTrader.Code.Output.Process($"SendBarSync: ConnectWebSocketAsync completed (Result={connectTask.Result})", PrintTo.OutputTab1);
                    
                    if (!connectTask.Result)
                    {
                        NinjaTrader.Code.Output.Process("SendBarSync: Failed to connect WebSocket after wait", PrintTo.OutputTab1);
                        return false;
                    }
                    // Add a small delay after connection to allow stabilization
                    NinjaTrader.Code.Output.Process("SendBarSync: Delaying 50ms after connection...", PrintTo.OutputTab1);
                    Task.Delay(50).Wait(); 
                    NinjaTrader.Code.Output.Process("SendBarSync: Delay complete", PrintTo.OutputTab1);
                }
                
                // Ensure WebSocket is open after connection attempt
                NinjaTrader.Code.Output.Process($"SendBarSync: Re-checking WebSocket state (Current: {webSocket?.State})", PrintTo.OutputTab1);
                if (webSocket == null || webSocket.State != WebSocketState.Open)
                {
                    NinjaTrader.Code.Output.Process($"SendBarSync: WebSocket still not open after connection attempt. State: {webSocket?.State}", PrintTo.OutputTab1);
                    return false;
                }
                
                // Convert timestamp to milliseconds since epoch
                NinjaTrader.Code.Output.Process("SendBarSync: Converting timestamp...", PrintTo.OutputTab1);
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                
                // Create bar message
                NinjaTrader.Code.Output.Process("SendBarSync: Creating bar message object...", PrintTo.OutputTab1);
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
                NinjaTrader.Code.Output.Process("SendBarSync: Serializing message to JSON...", PrintTo.OutputTab1);
                string jsonMessage = JsonConvert.SerializeObject(barMessage);
                byte[] messageBytes = Encoding.UTF8.GetBytes(jsonMessage);
                
                // Send synchronously with timeout
                NinjaTrader.Code.Output.Process("SendBarSync: Calling SendAsync on WebSocket...", PrintTo.OutputTab1);
                var sendTask = webSocket.SendAsync(
                    new ArraySegment<byte>(messageBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None // No cancellation token needed for synchronous Wait
                );
                NinjaTrader.Code.Output.Process("SendBarSync: Waiting for SendAsync (max 2s)...", PrintTo.OutputTab1);
                
                // Wait for completion with timeout
                if (sendTask.Wait(2000)) // 2 second timeout
                {
                    NinjaTrader.Code.Output.Process($"SendBarSync: Successfully sent bar for {instrument}", PrintTo.OutputTab1);
                    return true;
                }
                else
                {
                    NinjaTrader.Code.Output.Process("SendBarSync: Timeout sending bar data", PrintTo.OutputTab1);
                    return false;
                }
            }
            catch (AggregateException aggEx) when (aggEx.InnerException is OperationCanceledException)
            {
                NinjaTrader.Code.Output.Process("SendBarSync: Task cancelled (likely timeout)", PrintTo.OutputTab1);
                return false;
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"SendBarSync ERROR: {ex.GetType().Name} - {ex.Message}", PrintTo.OutputTab1);
                // Log inner exception if present
                if (ex.InnerException != null)
                {
                    NinjaTrader.Code.Output.Process($"  Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}", PrintTo.OutputTab1);
                }
                return false;
            }
            finally
            {
                NinjaTrader.Code.Output.Process("SendBarSync: Exiting method.", PrintTo.OutputTab1);
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
            CurrentDivergenceScore = 0;
            CurrentBarsSinceEntry = 0;
            CurrentPatternId = null; // Reset the pattern ID

            CurrentPatternType = null;
            CurrentSubtype = null;
            
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
                    await webSocket.ConnectAsync(new Uri(wsEndpoint), timeoutCts.Token);
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
        public async Task<bool> RecordPatternPerformanceAsync(PatternPerformanceRecord record)
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

                // Use endpoint URL pointing to CurvesV2 API server on port 3002
                string endpoint = "http://localhost:3002/api/pattern-timeline/record-with-penalties";
                
                // Serialize the record to JSON
                string jsonPayload = JsonConvert.SerializeObject(record);
                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                {
                    // Send request to the server
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            string responseText = await response.Content.ReadAsStringAsync(); 
                            Log($"[INFO] Pattern performance record sent successfully: {record.contextId}");
                            
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
                string endpoint = $"{signalPoolUrl}/api/signals/available?simulationTime={signalEpochMs}&instrument={instrument}";
                
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

        // Register a position for divergence tracking
        public async Task<bool> RegisterPositionAsync(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp)
        {
            if (IsDisposed()) return false;

            // Log(`Begin RegisterPositionAsync of ${instrument}`);
            
            // Validate input parameters first
            if (string.IsNullOrEmpty(entrySignalId) || string.IsNullOrEmpty(patternUuid) || string.IsNullOrEmpty(instrument))
            {
                Log($"[DIVERGENCE] ERROR: Invalid parameters for registration - EntrySignalId: {entrySignalId ?? "null"}, PatternUuid: {patternUuid ?? "null"}, Instrument: {instrument ?? "null"}");
                return false;
            }

            // Try up to 3 times with retry logic
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // Convert timestamp to ISO format
                    string isoTimestamp = entryTimestamp.ToUniversalTime().ToString("o");
                    
                    // Create the request payload
                    var requestPayload = new
                    {
                        entrySignalId = entrySignalId,
                        patternUuid = patternUuid,
                        instrument = instrument,
                        positionEntryTimestamp = isoTimestamp
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
                    string endpoint = $"{meServiceUrl}/api/positions/register";
                    
                    // Send request with timeout
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3))) // Reduced timeout
                    {
                        var response = await client.PostAsync(endpoint, content, timeoutCts.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Log($"[DIVERGENCE] Successfully registered position {entrySignalId}");
                            
                            // Add a small delay after registration to let the server process 
                            // This is especially helpful in backtest mode where things happen very quickly
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

        // Check divergence score for a registered position with threshold parameter
		public bool CheckDivergence(string entrySignalId, int age)
		{
		    if (IsDisposed() || age > 50) return false;
		    
		    // Simple retry logic
		    int retries = 1;
		    int delayMs = 300;
		    
		    for (int attempt = 0; attempt <= retries; attempt++)
		    {
		        try
		        {
		            string endpoint = $"{meServiceUrl}/api/positions/{entrySignalId}/divergence";
		            var response = client.GetAsync(endpoint).Result;
		            
		            if (response.IsSuccessStatusCode)
		            {
		                var divergenceResponse = JsonConvert.DeserializeObject<DivergenceResponse>(
		                    response.Content.ReadAsStringAsync().Result);
		                
		                if (divergenceResponse != null)
		                {
		                    // Update values on success
		                    CurrentDivergenceScore = divergenceResponse.divergenceScore;
		                    CurrentBarsSinceEntry = divergenceResponse.barsSinceEntry;
		                    divergenceScores[entrySignalId] = CurrentDivergenceScore;
		                    return true;
		                }
		            }
		            else
		            {
		                Log($"[DIVERGENCE] Failed to check divergence for {entrySignalId}: {response.StatusCode}");
		                
		                // Only retry on service unavailable
		                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < retries)
		                {
		                    Thread.Sleep(delayMs);
		                    continue;
		                }
		            }
		        }
		        catch (Exception ex)
		        {
		            Log($"[DIVERGENCE] Error checking divergence: {ex.Message}");
		            
		            // Retry on connection errors
		            if ((ex is HttpRequestException || ex is TaskCanceledException) && attempt < retries)
		            {
		                Thread.Sleep(delayMs);
		                continue;
		            }
		        }
		    }
		    
		    return false;
		}
        // Synchronous wrapper for RegisterPositionAsync
        public bool RegisterPosition(string entrySignalId, string patternUuid, string instrument, DateTime entryTimestamp)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // Call the async method and wait for it to complete
                return RegisterPositionAsync(entrySignalId, patternUuid, instrument, entryTimestamp).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"[DIVERGENCE] Error in RegisterPosition synchronous method: {ex.Message}");
                return false;
            }
        }

      
        // Deregister a position when it's closed
        public async Task<bool> DeregisterPositionAsync(string entrySignalId, bool wasGoodExit = false, double finalDivergence = 0.0)
        {
            if (IsDisposed()) return false;
            
            if (string.IsNullOrEmpty(entrySignalId))
            {
                Log($"[DIVERGENCE] ERROR: Invalid entrySignalId (null or empty) for deregistration");
                return false;
            }

            //Log($"[DIVERGENCE] Deregistering position {entrySignalId}");
            
            // Try up to 2 times (fewer retries for deregistration since it's less critical)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    // Define the endpoint
                    string endpoint = $"{meServiceUrl}/api/positions/{entrySignalId}/deregister";
                    
                    if (attempt > 1)
                    {
                        Log($"[DIVERGENCE] Retry {attempt}/2: Deregistering position {entrySignalId}");
                    }
                    
                    // Create the payload with exit outcome data
                    var exitOutcomeData = new 
                    {
                        wasGoodExit = wasGoodExit,
                        finalDivergence = finalDivergence
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
                            Log($"[DIVERGENCE] Successfully deregistered position {entrySignalId} with wasGoodExit={wasGoodExit}, finalDivergence={finalDivergence}");
                            
                            // Reset the static properties
                            CurrentDivergenceScore = 0;
                            CurrentBarsSinceEntry = 0;
							if(divergenceScores.ContainsKey(entrySignalId))
							{
                            	divergenceScores.Remove(entrySignalId);
							}
                            // Remove from local registry
                            if (activePositions.ContainsKey(entrySignalId))
                            {
                                activePositions.Remove(entrySignalId);
                                //Log($"[DIVERGENCE] Position {entrySignalId} removed from local registry");
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

        // Synchronous wrapper for DeregisterPositionAsync
        public bool DeregisterPosition(string entrySignalId, bool wasGoodExit = false, double finalDivergence = 0.0)
        {
            if (IsDisposed()) return false;
            
            try
            {
                // Call the async method and wait for it to complete
                return DeregisterPositionAsync(entrySignalId, wasGoodExit, finalDivergence).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"[DIVERGENCE] Error in DeregisterPosition synchronous method: {ex.Message}");
                return false;
            }
        }

        // Get position by entrySignalId
        private RegisteredPosition GetPosition(string entrySignalId)
        {
            if (string.IsNullOrEmpty(entrySignalId))
            {
                Log($"[DIVERGENCE] GetPosition called with null or empty entrySignalId");
                return null;
            }
            
            if (activePositions.TryGetValue(entrySignalId, out RegisteredPosition position))
            {
                return position;
            }
            
            Log($"[DIVERGENCE] Position not found for entrySignalId: {entrySignalId}");
            return null;
        }

        // Add a method to check if we should exit based on divergence
        public double GetExitSignal(string entrySignalId)
        {
            if (IsDisposed() || string.IsNullOrEmpty(entrySignalId)) return 0;
            
            // If we have a positive exit signal stored in divergenceScores, return it
            if (divergenceScores.ContainsKey(entrySignalId))
            {
                return divergenceScores[entrySignalId];
            }
            
            return 0; // No signal to exit
        }
	}

   
} 