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
        public string signalContextId { get; set; }
        public long timestamp_ms { get; set; }
        public long bar_timestamp_ms { get; set; }
        public float outcome_score { get; set; }
        public double pnl { get; set; }
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
        
        // Static properties for signal data
        public static double CurrentBullStrength { get; private set; }
        public static double CurrentBearStrength { get; private set; }
        public static string PatternName { get; private set; }
        public static DateTime LastSignalTimestamp { get; private set; }
        public static double CurrentAvgSlope { get; private set; }
        public static int CurrentSlopeImpact { get; private set; }
        public static List<PatternMatch> CurrentMatches { get; private set; } = new List<PatternMatch>();
        public static bool SignalsAreFresh => (DateTime.Now - LastSignalTimestamp).TotalSeconds < 30;
        // Connection status property
        public bool IsConnected => !disposed && client != null;

        // Backtest visualization service URL
        private readonly string visualizationUrl = "http://localhost:3007";
        private readonly HttpClient visualizationClient;

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
            logger?.Invoke($"CurvesV2 [HTTP]: {message}");
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
                string healthEndpoint = $"{baseUrl}/";
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
        public bool SendBar(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, bool isHistorical = false, string timeframe = "1m")
        {
            // For compatibility, simply delegate to our new methods
            return SendBarFireAndForget(instrument, timestamp, open, high, low, close, volume, timeframe);
        }

		/// send currentbars for Qdrant
        // Ultra-simple bar sender that TRULY never blocks - MODIFIED FOR DEBUG: ALWAYS USE HTTP
        public bool SendBarFireAndForget(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
		    return false;
            
            try
            {
                // Log the attempt
                //Log($"[DEBUG HTTP] SendBarFireAndForget: Called for {instrument} @ {timestamp}");
                
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;

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
                string endpoint = $"{baseUrl}/api/realtime_bars/{instrument}/backtest?matchPatterns=true&sessionId={sessionID}"; // Use /api/realtime_bars endpoint for NinjaTrader
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

        // Restore the SYNCHRONOUS debug version of CheckSignalsFireAndForget
        public bool CheckSignalsFireAndForget(string timestamp,string instrument)
        {
            // Log("[DEBUG] CheckSignalsFireAndForget called - Synchronous Debug Mode"); 
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
                return false;

            if (client == null) 
            {
                 Log("[DEBUG SYNC] CheckSignalsFireAndForget: HttpClient is null, cannot proceed.");
                 return false;
            }
                
            HttpResponseMessage response = null;
            try
            {
                //Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Preparing check for {instrument}");
                string endpoint = $"{baseUrl}/api/signals/{instrument}";

                if (IsDisposed() || IsShuttingDown())
                {
                    Log("[DEBUG SYNC] CheckSignalsFireAndForget: Aborted due to service shutdown before GET");
                    return false; 
                }

                //Log($"[DEBUG SYNC] CheckSignalsFireAndForget: {timestamp} Sending GET to {endpoint} (Blocking)");
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) 
                {
                    response = client.GetAsync(endpoint, timeoutCts.Token).GetAwaiter().GetResult(); 
                }

                //Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Received status {response.StatusCode} for {instrument}");

                if (response.IsSuccessStatusCode)
                {
                    string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); 
                    //Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Response content for {instrument}: {responseText.Substring(0, Math.Min(responseText.Length, 100))}...");

					var signalResponse = JsonConvert.DeserializeObject<CurvesV2Response>(responseText);
					if (signalResponse != null && signalResponse.success) // Check success directly
					{
					    string responseJsonForLog = JsonConvert.SerializeObject(signalResponse, Formatting.None); // Formatting.None for compact log

					    // Log the serialized JSON
					    // Check contextId directly
					    if (signalResponse.contextId != "nullscore")
					    {
					        // Store the context ID directly
					        CurrentContextId = signalResponse.contextId;
					
					        // Update static properties directly
					        CurrentBullStrength = signalResponse.bullScore; // Use bullScore
					        CurrentBearStrength = signalResponse.bearScore; // Use bearScore
							if(CurrentBullStrength > 0 || CurrentBearStrength > 0)
							{
								//Log("*****************************************************************************");
								//Log($"[INFO] GetSignalsSync: bull {CurrentBullStrength}, bear {CurrentBearStrength} ");
								//Log("*****************************************************************************");
							}
					      

	                        // Now, attempt the parse (still recommend TryParse)
	                        try // Add specific try-catch for safety, or use TryParse
	                        {
	                            if (!string.IsNullOrEmpty(signalResponse.timestamp) || signalResponse.timestamp == "null")
	                            {
	                                // Use TryParse for safety
	                                if (DateTime.TryParse(signalResponse.timestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsedTimestamp))
	                                {
	                                    LastSignalTimestamp = parsedTimestamp;
	                                }
	                                else
	                                {
	                                    Log($"[WARN] GetSignalsSync: Failed to parse non-null timestamp '{signalResponse.timestamp}' for instrument {instrument}.");
	                                    LastSignalTimestamp = DateTime.MinValue; // Default on parse failure
	                                }
	                            }
	                            else
	                            {
									if(CurrentBullStrength > 0 || CurrentBearStrength > 0)
									{
										Log("*****************************************************************************");
										Log($"[WARN] GetSignalsSync: Timestamp was null or empty in the valid signal response for instrument {instrument}. Response: {responseJsonForLog}");
										Log("*****************************************************************************");
									}
	                                // This case confirms the timestamp was null or empty BEFORE parsing
	                                LastSignalTimestamp = DateTime.MinValue; // Default on missing timestamp
	                            }
	                        }
	                        catch (Exception parseEx)
	                        {
	                           Log($"[ERROR] GetSignalsSync: CRITICAL error parsing timestamp for {instrument}. Value was '{(signalResponse.timestamp == null ? "NULL" : signalResponse.timestamp)}'. Error: {parseEx.Message}");
	                           // Handle the fact that the timestamp couldn't be set
	                           LastSignalTimestamp = DateTime.MinValue; // Ensure a default
	                        }
							
					      
					    }
					   
					    return true; // Or adjust as needed
					}
					else
					{
						
					    Log($"[WARN] GetSignalsSync: Invalid response or success=false for {instrument}. Success: {signalResponse?.success}");
					    return false; // Or appropriate default/error value
					}
                }
                else
                {
                     Log($"[DEBUG SYNC] CheckSignalsFireAndForget: HTTP error {response.StatusCode} checking signals for {instrument}");
                }
            }
            catch (OperationCanceledException)
            {
                Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Request timed out for {instrument} (5s)");
            }
            catch (Exception ex)
            {
                if (IsDisposed() || IsShuttingDown()) return false; 
                Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Error checking signals for {instrument}: {ex.GetType().Name} - {ex.Message}");
                 if(ex.InnerException != null) Log($"  Inner: {ex.InnerException.Message}");
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
        public bool QueueBar(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            // For now, simply pass through to SendBarFireAndForget
            return SendBarFireAndForget(instrument, timestamp, open, high, low, close, volume, timeframe);
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
                if (string.IsNullOrEmpty(record.signalContextId))
                {
                    Log("[ERROR] RecordPatternPerformance: signalContextId is required");
                    return false;
                }

                // Use endpoint URL pointing to CurvesV2 API server on port 3002
                string endpoint = "http://localhost:3002/api/pattern-timeline/record";
                
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
                            Log($"[INFO] Pattern performance record sent successfully: {record.signalContextId}, outcome: {record.outcome_score}");
                            
                            // Remove the mapping after successful recording
                            lock (signalContextLock)
                            {
                                signalContextToEntryMap.Remove(record.signalContextId);
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
    }
} 