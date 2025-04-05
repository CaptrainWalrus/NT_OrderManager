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
        public string timestamp { get; set; }
        public SignalData signals { get; set; }
        public string requestId { get; set; }
    }

    public class SignalData
    {
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
		public string sessionID;
        private readonly HttpClient client;
        private readonly string baseUrl;
        private readonly Action<string> logger;
        private bool disposed = false;
        private readonly object disposeLock = new object();
        private readonly OrganizedStrategy.CurvesV2Config config;
        
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
        public static int CurrentBullStrength { get; private set; }
        public static int CurrentBearStrength { get; private set; }
        public static string PatternName { get; private set; }
        public static DateTime LastSignalTimestamp { get; private set; }
        public static double CurrentAvgSlope { get; private set; }
        public static int CurrentSlopeImpact { get; private set; }
        public static List<PatternMatch> CurrentMatches { get; private set; } = new List<PatternMatch>();
        public static bool SignalsAreFresh => (DateTime.Now - LastSignalTimestamp).TotalSeconds < 30;
        
        // Connection status property
        public bool IsConnected => !disposed && client != null;

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

         // Synchronous Signal Fetch
        // Returns SignalData directly, or null on failure/error
        public SignalData GetSignalsSync(string instrument)
        {
            lock(disposeLock) { if(disposed) return null; }

            if (string.IsNullOrEmpty(instrument))
            {
                 Log("[WARN] GetSignalsSync: Instrument cannot be null or empty.");
                 return null;
            }

            HttpResponseMessage response = null;
            string endpoint = $"{baseUrl}/api/signals/{instrument}";

            try
            {
                Log($"[DEBUG] GetSignalsSync: Fetching signals for {instrument} from {endpoint} (Blocking)");

                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) 
                {
                    // BLOCKING CALL
                    response = client.GetAsync(endpoint, timeoutCts.Token).GetAwaiter().GetResult();
                }

                Log($"[DEBUG] GetSignalsSync: Received status {response.StatusCode} for {instrument}");
                string responseText = ""; 
                try { 
                    // BLOCKING CALL
                    responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); 
                }
                catch (Exception readEx) {
                     Log($"[ERROR] GetSignalsSync: Failed to read response content: {readEx.Message}");
                     // Still check status code below, might be informative
                }

                        if (response.IsSuccessStatusCode)
                        {
                    Log($"[DEBUG] GetSignalsSync: Response content for {instrument}: {responseText.Substring(0, Math.Min(responseText.Length, 100))}...");
                    try {
                        var signalResponse = JsonConvert.DeserializeObject<CurvesV2Response>(responseText);
                        if (signalResponse != null && signalResponse.success && signalResponse.signals != null)
                        {
                            Log($"[INFO] GetSignalsSync: Successfully received signals for {instrument}. Bull: {signalResponse.signals.bull}, Bear: {signalResponse.signals.bear}");
                            return signalResponse.signals; 
                                    }
                                    else
                                    {
                             Log($"[WARN] GetSignalsSync: Invalid response or success=false/null signals for {instrument}. Success: {signalResponse?.success}, Signals Null: {signalResponse?.signals == null}");
                             return null;
                        }
                    }
                    catch (JsonException jsonEx) {
                        Log($"[ERROR] GetSignalsSync: Failed to parse JSON response for {instrument}: {jsonEx.Message}");
                        return null;
                            }
                        }
                        else
                        {
                     // Log the response body even on error if available
                     Log($"[ERROR] GetSignalsSync: HTTP error {response.StatusCode} fetching signals for {instrument}. Response: {responseText.Substring(0, Math.Min(responseText.Length, 200))}");
                     return null;
                }
            }
            catch (OperationCanceledException) // Catches timeout from CancellationTokenSource
            {
                 bool wasDisposed; 
                 lock(disposeLock) { wasDisposed = disposed; }
                 if (!wasDisposed) {
                    Log($"[ERROR] GetSignalsSync: Request timed out fetching signals for {instrument} (5s)");
                 }
                return null;
            }
            catch (Exception ex)
            {
                 bool wasDisposed; 
                 lock(disposeLock) { wasDisposed = disposed; }
                 if (!wasDisposed) {
                    Log($"[ERROR] GetSignalsSync: Error fetching signals for {instrument}: {ex.GetType().Name} - {ex.Message}");
                    if(ex.InnerException != null) Log($"  Inner: {ex.InnerException.Message}");
                 }
                return null;
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

        // Check for high-confidence signals
        public async Task<bool> CheckHighConfidenceSignalsAsync(string instrument, int minConfidence = 90)
        {
            if (IsDisposed()) return false;
            
            // Rate limiting - only check every 3 seconds at most
            if ((DateTime.Now - lastSignalCheck).TotalMilliseconds < 3000)
                return false;
            
            lastSignalCheck = DateTime.Now;
            
            try
            {
                // Build the URL - use the high_confidence endpoint
                string endpoint = $"{baseUrl}/api/signals/{instrument}/high_confidence?min_confidence={minConfidence}";
                
                // Use HttpWebRequest with Timeout
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "GET";
                request.Timeout = 2000; // 2 second timeout
                
                // Get response
                using (WebResponse response = await Task.Factory.FromAsync(
                    request.BeginGetResponse,
                    request.EndGetResponse,
                    null))
                {
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string responseText = await reader.ReadToEndAsync();
                        
                        // Parse the response
                        var signalResponse = JsonConvert.DeserializeObject<CurvesV2Response>(responseText);
                        
                        if (signalResponse != null && signalResponse.success)
                        {
                            // Update static properties for backwards compatibility
                            if (signalResponse.signals != null)
                            {
								Log($"[DEBUG CRITICAL] Setting static properties: Bull={signalResponse.signals.bull}, Bear={signalResponse.signals.bear}");

                                CurrentBullStrength = signalResponse.signals.bull;
                                CurrentBearStrength = signalResponse.signals.bear;
                                
                                if (signalResponse.signals.matches != null && signalResponse.signals.matches.Count > 0)
                                {
                                    CurrentMatches = signalResponse.signals.matches;
                                    PatternName = signalResponse.signals.matches[0].patternName;
                                }
                                else
                                {
                                    CurrentMatches = new List<PatternMatch>();
                                    PatternName = string.Empty;
                                }
                            }
                            
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error checking high-confidence signals: {ex.Message}");
            }
            
            return false;
        }

        // Update the SendBar method to use WebSocket for backtest if available
        public bool SendBar(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, bool isHistorical = false, string timeframe = "1m")
        {
            // For compatibility, simply delegate to our new methods
            return SendBarFireAndForget(instrument, timestamp, open, high, low, close, volume, timeframe);
        }

        // Add automatic signal polling
        private void StartSignalPolling()
        {
            // Cancel any existing polling task
            if (signalPollCts != null && !signalPollCts.IsCancellationRequested)
            {
                signalPollCts.Cancel();
                signalPollCts.Dispose();
            }
            
            // Create a new cancellation token source
            signalPollCts = new CancellationTokenSource();
            
            // Start polling task
            Task.Run(async () => 
            {
                try
                {
                    NinjaTrader.Code.Output.Process("Starting automatic signal polling", PrintTo.OutputTab1);
                    
                    while (!IsDisposed() && !signalPollCts.IsCancellationRequested)
                    {
                        try
                        {
                            // Poll for signals for active instruments
                            // For simplicity, we'll poll for ES, NQ, and MES - these are the most common
                            string[] instruments = new string[] { "ES", "NQ", "MES" };
                            
                            foreach (string instrument in instruments)
                            {
                                await PollSignalsForInstrument(instrument);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Just log errors but keep polling
                            NinjaTrader.Code.Output.Process($"Error during signal polling: {ex.Message}", PrintTo.OutputTab1);
                        }
                        
                        // Wait for the next poll interval
                        await Task.Delay(signalPollIntervalMs, signalPollCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, just exit
                    NinjaTrader.Code.Output.Process("Signal polling cancelled", PrintTo.OutputTab1);
                }
                catch (Exception ex)
                {
                    NinjaTrader.Code.Output.Process($"Unhandled error in signal polling: {ex.Message}", PrintTo.OutputTab1);
                }
            }, signalPollCts.Token);
        }

        // Helper method to poll signals for a specific instrument
        private async Task PollSignalsForInstrument(string instrument)
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
                return;
            
            try
            {
                // Build the signals endpoint URL
                string endpoint = $"{baseUrl}/api/signals/{instrument}";
                
                // Create a request with timeout
                var request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "GET";
                request.Timeout = 2000; // 2 second timeout
                
                // Get response
                using (var response = (HttpWebResponse)await Task.Factory.FromAsync(
                    request.BeginGetResponse, 
                    request.EndGetResponse, 
                    null))
		        {
		            if (response.StatusCode == HttpStatusCode.OK)
		            {
		                using (var reader = new StreamReader(response.GetResponseStream()))
		                {
                            string responseText = await reader.ReadToEndAsync();
		                    var signalResponse = JsonConvert.DeserializeObject<CurvesV2Response>(responseText);
		                    
                            if (signalResponse != null && signalResponse.success && signalResponse.signals != null)
		                    {
                                // Update the static properties
		                        CurrentBullStrength = signalResponse.signals.bull;
		                        CurrentBearStrength = signalResponse.signals.bear;
		                        
                                // Update matches and pattern name
		                        if (signalResponse.signals.matches != null && signalResponse.signals.matches.Count > 0)
		                        {
		                            CurrentMatches = signalResponse.signals.matches;
		                            PatternName = signalResponse.signals.matches[0].patternName;
		                        }
		                        else
		                        {
		                            CurrentMatches = new List<PatternMatch>();
		                            PatternName = string.Empty;
		                        }
                                
                                // Update timestamp from the server or use current time
                                DateTime timestamp;
                                if (!string.IsNullOrEmpty(signalResponse.timestamp))
                                {
                                    // Try to parse the server timestamp
                                    if (DateTime.TryParse(signalResponse.timestamp, out DateTime parsedTime))
                                    {
                                        timestamp = parsedTime;
                                    }
                                    else
                                    {
                                        timestamp = DateTime.Now;
                                    }
                                }
                                else
                                {
                                    timestamp = DateTime.Now;
                                }
                                
                                LastSignalTimestamp = timestamp;
                                
                                // Log successful update with minimal output
                                Log($"Updated signals for {instrument}: Bull {CurrentBullStrength}, Bear {CurrentBearStrength}");
                            }
		                }
		            }
		        }
		    }
		    catch (Exception ex)
		    {
                // Non-blocking error - just log
                Log($"Error polling signals for {instrument}: {ex.Message}");
            }
        }

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
                                            Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP response error for {instrument}: {ex.Message}");
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

                Log($"[DEBUG SYNC] CheckSignalsFireAndForget: {timestamp} Sending GET to {endpoint} (Blocking)");
                
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

                    if (signalResponse != null && signalResponse.success)
                    {
                        Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Parsed success for {instrument}");
                        if (signalResponse.signals != null)
                        {                                    
                            //Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Updating signals for {instrument}...");
                            CurrentBullStrength = signalResponse.signals.bull;
                            CurrentBearStrength = signalResponse.signals.bear;
                            LastSignalTimestamp = DateTime.Now; 
                            CurrentMatches = signalResponse.signals.matches ?? new List<PatternMatch>();
                            PatternName = CurrentMatches.Count > 0 ? (CurrentMatches[0]?.patternName ?? "No Pattern") : "No Pattern";
                            Log($"[DEBUG SYNC] CheckSignalsFireAndForget: {timestamp} Updated signals for {instrument}: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}");
			                return true;
			            }
			            else
			            {
                             Log($"[DEBUG SYNC] CheckSignalsFireAndForget: 'signals' property missing in successful response for {instrument}");
                        }
                    }
                    else
                    {
                        Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Invalid response or success=false for {instrument}. Success: {signalResponse?.success}");
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
        
        // Synchronously poll for signals and update static properties
        public bool PollSignalsSync(string instrument)
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
                return false;
            
            try
            {
                NinjaTrader.Code.Output.Process($"PollSignalsSync: Polling signals for {instrument}", PrintTo.OutputTab1);
                
                // Build the signals endpoint URL
                string endpoint = $"{baseUrl}/api/signals/{instrument}";
                
                // Create a request with timeout
                var request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "GET";
                request.Timeout = 15000; // 15 second timeout
                
                try 
                {
                    // Get response synchronously
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var reader = new StreamReader(response.GetResponseStream()))
                            {
                                string responseText = reader.ReadToEnd();
                                var signalResponse = JsonConvert.DeserializeObject<dynamic>(responseText);
                                
                                if (signalResponse != null && signalResponse.success == true)
                                {
                                    // Update the static properties directly
                                    // Use the signals property from the response with bull/bear values
                                    if (signalResponse.signals != null)
                                    {
                                        CurrentBullStrength = signalResponse.signals.bull;
                                        CurrentBearStrength = signalResponse.signals.bear;
                                        
                                        // Update timestamp
                                        LastSignalTimestamp = DateTime.Now;
                                        
                                        NinjaTrader.Code.Output.Process($"[PollSignalsSync] Updated signals for {instrument}: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}", PrintTo.OutputTab1);
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (WebException webEx) when (webEx.Status == WebExceptionStatus.Timeout)
                {
                    NinjaTrader.Code.Output.Process($"PollSignalsSync ERROR: The request timed out after 15 seconds", PrintTo.OutputTab1);
                    return false;
                }
                
                NinjaTrader.Code.Output.Process($"PollSignalsSync: Failed to get valid signal response", PrintTo.OutputTab1);
                return false;
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"PollSignalsSync ERROR: {ex.Message}", PrintTo.OutputTab1);
                return false;
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

        // Asynchronously poll for signals and update static properties
        public async Task<bool> PollSignalsAsync(string instrument, CancellationToken cancellationToken = default) // Made async, added CancellationToken
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
                return false;

            // Ensure client is initialized (might need to move initialization logic)
            if (client == null) 
            {
                 // Cannot reassign readonly field, log a warning instead
                 NinjaTrader.Code.Output.Process("PollSignalsAsync: HttpClient is null, cannot proceed with request", PrintTo.OutputTab1);
                 return false;
            }
            
            try
            {
                NinjaTrader.Code.Output.Process($"PollSignalsAsync: Polling signals for {instrument}", PrintTo.OutputTab1);
                
                // Build the signals endpoint URL
                string endpoint = $"{baseUrl}/api/signals/{instrument}";
                
                // Use HttpClient for async request
                HttpResponseMessage response = null;
                try
                {
                    // Make request asynchronously with timeout handled by CancellationToken
                    using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeoutCts.CancelAfter(15000); // 15 second timeout
                        NinjaTrader.Code.Output.Process($"PollSignalsAsync: Sending GET request to {endpoint}", PrintTo.OutputTab1);
                        response = await client.GetAsync(endpoint, timeoutCts.Token);
                        NinjaTrader.Code.Output.Process($"PollSignalsAsync: Received response status {response.StatusCode}", PrintTo.OutputTab1);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        string responseText = await response.Content.ReadAsStringAsync();
                        var signalResponse = JsonConvert.DeserializeObject<dynamic>(responseText);
                        
                        if (signalResponse != null && signalResponse.success == true)
                        {
                            // Update the static properties directly
                            if (signalResponse.signals != null)
                            {
                                // Potential improvement: Lock static updates if accessed elsewhere simultaneously
                                CurrentBullStrength = signalResponse.signals.bull;
                                CurrentBearStrength = signalResponse.signals.bear;
                                LastSignalTimestamp = DateTime.Now; // Consider using server timestamp if available
                                
                                // Update slope information if available
                                if (signalResponse.signals.avgSlope != null) {
                                    CurrentAvgSlope = signalResponse.signals.avgSlope;
                                    CurrentSlopeImpact = signalResponse.signals.slopeImpact ?? 0;
                                    NinjaTrader.Code.Output.Process($"PollSignalsAsync: Slope info - Avg={CurrentAvgSlope.ToString("F6")}, Impact={CurrentSlopeImpact}%", PrintTo.OutputTab1);
                                }
                                
                                // Optionally update CurrentMatches if provided by the API
                                // if (signalResponse.signals.matches != null) {
                                //     CurrentMatches = signalResponse.signals.matches.ToObject<List<PatternMatch>>();
                                // } else {
                                //     CurrentMatches.Clear();
                                // }

                                NinjaTrader.Code.Output.Process($"PollSignalsAsync: Updated signals for {instrument}: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}", PrintTo.OutputTab1);
                                return true;
                            }
                            else
                            {
                                NinjaTrader.Code.Output.Process($"PollSignalsAsync: Success response but missing 'signals' data for {instrument}", PrintTo.OutputTab1);
                            }
                        }
                        else
                        {
                             NinjaTrader.Code.Output.Process($"PollSignalsAsync: API reported failure or null response for {instrument}. Response: {responseText}", PrintTo.OutputTab1);
                        }
                    }
                    else
                    {
                         NinjaTrader.Code.Output.Process($"PollSignalsAsync: HTTP error {response.StatusCode} polling signals for {instrument}", PrintTo.OutputTab1);
                    }
                }
                catch (OperationCanceledException ex) // Catches timeout or external cancellation
                {
                    if (cancellationToken.IsCancellationRequested)
                         NinjaTrader.Code.Output.Process($"PollSignalsAsync: Operation cancelled externally for {instrument}", PrintTo.OutputTab1);
                    else
                         NinjaTrader.Code.Output.Process($"PollSignalsAsync ERROR: Request timed out after 15 seconds for {instrument}", PrintTo.OutputTab1);
                    return false;
                }
                catch (HttpRequestException httpEx)
                {
                    NinjaTrader.Code.Output.Process($"PollSignalsAsync HTTP ERROR polling signals for {instrument}: {httpEx.Message}", PrintTo.OutputTab1);
                    return false;
                }
                finally
                {
                    response?.Dispose(); // Ensure response is disposed
                }
                
                NinjaTrader.Code.Output.Process($"PollSignalsAsync: Failed to get valid signal response for {instrument}", PrintTo.OutputTab1);
                return false;
            }
            catch (Exception ex) // Catch-all for unexpected errors
            {
                NinjaTrader.Code.Output.Process($"PollSignalsAsync UNEXPECTED ERROR polling signals for {instrument}: {ex.Message}", PrintTo.OutputTab1);
                return false;
            }
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
    }
} 