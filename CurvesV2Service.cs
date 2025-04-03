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
using NinjaTrader.Data;
using System.Net.WebSockets;
using System.Net;
using System.IO;
using System.Collections.Concurrent;
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

    public class CurvesV2Service : IDisposable
    {
        // Rate limiting variables
        private static ConcurrentQueue<BarDataPacket> barQueue = new ConcurrentQueue<BarDataPacket>();
        private static bool processorRunning = false;
        private static readonly object syncLock = new object();

        // Simple data packet class - must be inside the CurvesV2Service class
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
            public DateTime QueuedAt { get; set; }
        }
        
        private HttpClient client;
        private readonly string baseUrl;
        private readonly string wsUrl;
        private ClientWebSocket webSocket = new ClientWebSocket();
        private CancellationTokenSource webSocketCts = new CancellationTokenSource();
        private bool webSocketConnected = false;
        private bool disposed = false;
        private readonly Action<string> logger;
        private const int MaxRetries = 3;
        private DateTime lastBarSent = DateTime.MinValue;
        private DateTime lastSignalCheck = DateTime.MinValue;
        private readonly object disposeLock = new object();
        private readonly OrganizedStrategy.CurvesV2Config config;
        private static readonly object webSocketSendLock = new object(); // Lock for send operations

        // References to background tasks for proper disposal
        private Task receiveLoopTask;
        private Task pingLoopTask;
        
        // Add public property to expose connection state
        public bool IsConnected => webSocketConnected && webSocket?.State == WebSocketState.Open;

        // Connection state tracking
        private bool isShuttingDown = false;
        private readonly object connectionStateLock = new object();
        private DateTime lastConnectionAttempt = DateTime.MinValue;
        private int consecutiveConnectionErrors = 0;

        // Static properties for strategy use
        public static int CurrentBullStrength { get; private set; }
        public static int CurrentBearStrength { get; private set; }
        public static string PatternName { get; private set; }
        public static List<PatternMatch> CurrentMatches { get; private set; } = new List<PatternMatch>();
        public static DateTime? LastSignalTimestamp { get; private set; }
        public static bool SignalsAreFresh => LastSignalTimestamp.HasValue && 
                                             (DateTime.Now - LastSignalTimestamp.Value).TotalMilliseconds < 10000; // 10 seconds

        // New method to reset static data (for cleanup in backtest or memory management)
        public static void ResetStaticData()
        {
            try
            {
                CurrentBullStrength = 0;
                CurrentBearStrength = 0;
                PatternName = "";
                CurrentMatches = new List<PatternMatch>();
                LastSignalTimestamp = null;
                
                // Clear any static task or resource references
                lock (syncLock)
                {
                    processorRunning = false;
                    // Clear bar queue
                    while (barQueue.TryDequeue(out _)) { }
                }
                
                // Clear response cache
                ClearCache();

                // Force cleanup of any static resources
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                
                // Log the reset
                NinjaTrader.Code.Output.Process("CurvesV2Service static data has been reset", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"Error resetting static data: {ex.Message}", PrintTo.OutputTab1);
            }
        }
        
        // Clear old entries from the cache
        private static void ClearCache()
        {
            try
            {
                // If cache is too large, remove oldest items
                if (backtestResponseCache.Count > cacheMaxSize / 2)
                {
                    NinjaTrader.Code.Output.Process($"Clearing CurvesV2 cache (currently {backtestResponseCache.Count} items)", PrintTo.OutputTab1);
                    backtestResponseCache.Clear();
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"Error clearing CurvesV2 cache: {ex.Message}", PrintTo.OutputTab1);
            }
        }

        // Rate limiting in milliseconds
        public int BarIntervalMs { get; set; } = 200;      // 5 bars per second max
        public int SignalsIntervalMs { get; set; } = 1000;  // 1 second

        // Add automatic signal polling
        private bool signalPollingEnabled = true;
        private CancellationTokenSource signalPollCts = new CancellationTokenSource();
        private int signalPollIntervalMs = 3000; // Poll every 3 seconds by default

        // Track WebSocket failures to avoid repeated attempts
        private bool webSocketBacktestFailed = false;
        private DateTime lastWebSocketFailure = DateTime.MinValue;
        private int consecutiveWebSocketFailures = 0;
        
        // Backtest response cache to avoid calling server multiple times with same data
        private class BacktestCacheKey
        {
            public string Instrument { get; set; }
            public long Timestamp { get; set; }
            
            public override bool Equals(object obj)
            {
                if (!(obj is BacktestCacheKey other))
                    return false;
                
                return Instrument == other.Instrument && Timestamp == other.Timestamp;
            }
            
            public override int GetHashCode()
            {
                return (Instrument + Timestamp.ToString()).GetHashCode();
            }
        }
        
        private static ConcurrentDictionary<BacktestCacheKey, CurvesV2Response> backtestResponseCache = 
            new ConcurrentDictionary<BacktestCacheKey, CurvesV2Response>();
        private static int cacheMaxSize = 500; // Reduced from 1000 to 500 for better memory management

        // Variables for WebSocket heartbeat system
        private int missedHeartbeats = 0;
        private bool receivedPong = true;

        // Constructor
        public CurvesV2Service(CurvesV2Config config, Action<string> logger = null)
        {
            this.config = config;
            this.baseUrl = config.GetMainApiEndpoint();
            this.wsUrl = config.GetWebSocketEndpoint();
            this.logger = logger ?? ((msg) => { /* No-op if no logger provided */ });
            this.disposed = false;
            
            // Initialize the shared HttpClient (kept for health checks only)
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(10);  // 10-second timeout for HTTP operations
            
            // Initialize WebSocket and cancellation token source
            // webSocket = new ClientWebSocket(); // Delay creation until ConnectWebSocketAsync
            webSocketCts = new CancellationTokenSource();
            
            // Set rate limiting defaults based on config
            BarIntervalMs = config.BarDataIntervalMs;
            
            // Configure signal polling interval from config if available
            if (config.SignalPollIntervalMs > 0)
            {
                signalPollIntervalMs = config.SignalPollIntervalMs;
            }
            
            // Check if we should auto-initialize WebSockets and polling
            bool autoInitialize = true;
            if (config.EnableSyncMode.HasValue)
            {
                autoInitialize = !config.EnableSyncMode.Value;
                NinjaTrader.Code.Output.Process($"CurvesV2Service Constructor: AutoInitialize set to {autoInitialize} based on config", PrintTo.OutputTab1);
            }
            
            // Initialize the client immediately - CONDITIONALLY DISABLED IN SYNC MODE
            // --- START TEMPORARY DEBUG: Disable Auto Connect ---
            /*
            if (autoInitialize)
            {
                Task.Run(async () => { 
                    await ConnectWebSocketAsync();
                });
                NinjaTrader.Code.Output.Process("CurvesV2Service Constructor: WebSocket connection task started", PrintTo.OutputTab1);
            }
            else
            {
                NinjaTrader.Code.Output.Process("CurvesV2Service Constructor: Auto WebSocket connection DISABLED for sync mode", PrintTo.OutputTab1);
            }
            // */ // <-- Incorrect placement, move below
            // --- END TEMPORARY DEBUG ---
            
            // Start signal polling if enabled - CONDITIONALLY DISABLED IN SYNC MODE
             // --- START TEMPORARY DEBUG: Disable Auto Polling ---
            /* 
            if (signalPollingEnabled && autoInitialize)
            {
                StartSignalPolling();
                NinjaTrader.Code.Output.Process("CurvesV2Service Constructor: Signal polling started", PrintTo.OutputTab1);
            }
            else
            {
                NinjaTrader.Code.Output.Process("CurvesV2Service Constructor: Auto signal polling DISABLED for sync mode", PrintTo.OutputTab1);
            }
            */
            // --- END TEMPORARY DEBUG ---
             NinjaTrader.Code.Output.Process("[DEBUG] CurvesV2Service Constructor: Auto WebSocket connection & Polling DISABLED FOR DEBUGGING", PrintTo.OutputTab1); // Combined log
        }

        private bool IsDisposed()
        {
            NinjaTrader.Code.Output.Process("IsDisposed: Attempting to acquire disposeLock...", PrintTo.OutputTab1);
            lock (disposeLock)
            {
                NinjaTrader.Code.Output.Process($"IsDisposed: Lock acquired. Value={disposed}", PrintTo.OutputTab1);
                return disposed;
            }
        }
        
        private bool IsShuttingDown()
        {
            NinjaTrader.Code.Output.Process("IsShuttingDown: Attempting to acquire connectionStateLock...", PrintTo.OutputTab1);
            lock (connectionStateLock)
            {
                NinjaTrader.Code.Output.Process($"IsShuttingDown: Lock acquired. Value={isShuttingDown || disposed}", PrintTo.OutputTab1);
                return isShuttingDown || disposed;
            }
        }

        public void Dispose()
        {
            lock (disposeLock)
            {
                if (disposed)
                    return;
                
                try
                {
                    isShuttingDown = true;
                    
                    // Stop the signal polling task if it's running
                    try 
                    {
                        // Cancel the signal polling token
                        if (signalPollCts != null)
                        {
                            if (!signalPollCts.IsCancellationRequested)
                                signalPollCts.Cancel();
                            signalPollCts.Dispose();
                            signalPollCts = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error stopping signal polling: {ex.Message}");
                    }
                    
                    // Cancel WebSocket token source
                    try
                    {
                        if (webSocketCts != null)
                        {
                            if (!webSocketCts.IsCancellationRequested)
                                webSocketCts.Cancel();
                            webSocketCts.Dispose();
                            webSocketCts = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error canceling WebSocket operations: {ex.Message}");
                    }
                    
                    // Close WebSocket if needed
                    if (webSocket != null)
                    {
                        try
                        {
                            // Use a short timeout for closing
                            var timeout = new CancellationTokenSource(2000).Token;
                            
                            // Only attempt to close if it's open
                            if (webSocket.State == WebSocketState.Open)
                            {
                                var closeTask = webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Service is shutting down",
                                    timeout);
                                
                                // Wait with timeout
                                if (!closeTask.Wait(2000))
                                {
                                    Log("WebSocket close operation timed out");
                                }
                            }
                            
                            webSocket.Dispose();
                            webSocket = null;
                        }
                        catch (Exception ex)
                        {
                            Log($"Error closing WebSocket: {ex.Message}");
                        }
                    }
                    
                    // Clear and dispose client
                    if (client != null)
                    {
                        try
                        {
                            client.Dispose();
                            client = null;
                        }
                        catch (Exception ex)
                        {
                            Log($"Error disposing HTTP client: {ex.Message}");
                        }
                    }
                    
                    // Set flags to terminate any pending operations
                    webSocketConnected = false;
                    disposed = true;
                    
                    // Signal processor to stop
                    processorRunning = false;
                    
                    // Apply aggressive cleanup to ensure no tasks linger
                    GC.Collect(2, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                    
                    Log("CurvesV2Service fully disposed");
                }
                catch (Exception ex)
                {
                    Log($"Error during service disposal: {ex.Message}");
                    disposed = true; // Mark as disposed anyway
                }
            }
        }

        private void Log(string message)
        {
            if (config != null && !config.EnableDetailedLogging)
                return;
                
            logger($"CurvesV2: {message}");
        }

        // Ultra-simple bar sender that never blocks
        public bool QueueBar(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            if (IsDisposed() || string.IsNullOrEmpty(instrument))
                return false;
            
            // Queue the bar for processing
            barQueue.Enqueue(new BarDataPacket
            {
                Instrument = instrument,
                Timestamp = timestamp,
                Open = open, 
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
                Timeframe = timeframe,
                QueuedAt = DateTime.Now
            });
            NinjaTrader.Code.Output.Process($"QueueBar: Added bar for {instrument} @ {timestamp}. Queue size: {barQueue.Count}", PrintTo.OutputTab1);
            
            // Start the processor if it's not running
            StartProcessor();
            
            // Always return success - we've queued the data
            return true;
        }

        // Start the background processor if not already running
        private void StartProcessor()
        {
            lock (syncLock)
            {
                if (!processorRunning)
                {
                    processorRunning = true;
                    // Pass the cancellation token to the task
                    Task.Run(() => ProcessBarQueue(webSocketCts.Token), webSocketCts.Token);
                    NinjaTrader.Code.Output.Process("StartProcessor: Launched ProcessBarQueue task.", PrintTo.OutputTab1); 
                }
            }
        }

        // Process the bar queue in the background
        private async Task ProcessBarQueue(CancellationToken cancellationToken) // Accept token
        {
            NinjaTrader.Code.Output.Process("ProcessBarQueue Task Started.", PrintTo.OutputTab1); 
            try
            {
                // Check token in loop condition
                while (!IsDisposed() && !barQueue.IsEmpty && !cancellationToken.IsCancellationRequested) 
                {
                    NinjaTrader.Code.Output.Process($"ProcessBarQueue Loop: Start. IsDisposed={IsDisposed()}, Cancelled={cancellationToken.IsCancellationRequested}, QueueEmpty={barQueue.IsEmpty}", PrintTo.OutputTab1); // Updated log
                    
                    // Pass token to delay
                    await Task.Delay(1, cancellationToken); 
                    
                    NinjaTrader.Code.Output.Process("ProcessBarQueue Loop: After 1ms Delay.", PrintTo.OutputTab1); 
                    
                    // Check cancellation again after delay
                    if (cancellationToken.IsCancellationRequested) break; 
                    
                    if (barQueue.TryDequeue(out BarDataPacket packet))
                    {
                        NinjaTrader.Code.Output.Process($"ProcessBarQueue: Dequeued bar for {packet.Instrument} @ {packet.Timestamp}. Remaining: {barQueue.Count}", PrintTo.OutputTab1);
                        try
                        {
                            TimeSpan age = DateTime.Now - packet.QueuedAt;
                            if (age.TotalSeconds > 30)
                            {
                                NinjaTrader.Code.Output.Process($"ProcessBarQueue: Skipping stale bar (>30s old).", PrintTo.OutputTab1); 
                                continue;
                            }
                            
                            // --- Integrate Send Logic Directly Here --- 
                            if (webSocket != null && webSocket.State == WebSocketState.Open)
                            {
                                NinjaTrader.Code.Output.Process($"ProcessBarQueue: Attempting direct SendAsync for {packet.Instrument}. WS State: {webSocket.State}", PrintTo.OutputTab1); // Log send attempt
                                try
                                {
                                    long epochMs = (long)(packet.Timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                                    var barMessage = new
                                    {
                                        type = "bar",
                                        instrument = packet.Instrument,
                                        timestamp = epochMs,
                                        open = Convert.ToDouble(packet.Open),
                                        high = Convert.ToDouble(packet.High),
                                        low = Convert.ToDouble(packet.Low),
                                        close = Convert.ToDouble(packet.Close),
                                        volume = Convert.ToDouble(packet.Volume),
                                        timeframe = packet.Timeframe
                                    };
                                    string jsonMessage = JsonConvert.SerializeObject(barMessage);
                                    byte[] messageBytes = Encoding.UTF8.GetBytes(jsonMessage);
                                    
                                    // Pass token to SendAsync
                                    await webSocket.SendAsync(
                                        new ArraySegment<byte>(messageBytes),
                                        WebSocketMessageType.Text,
                                        true,
                                        cancellationToken // <<< Pass token
                                    );
                                    NinjaTrader.Code.Output.Process($"ProcessBarQueue: Direct SendAsync completed for {packet.Instrument}.", PrintTo.OutputTab1); // Log success
                                }
                                catch (OperationCanceledException) 
                                { 
                                    NinjaTrader.Code.Output.Process($"ProcessBarQueue: SendAsync cancelled for {packet.Instrument}.", PrintTo.OutputTab1);
                                    break; // Exit loop if send is cancelled
                                } 
                                catch (Exception sendEx) 
                                {
                                    NinjaTrader.Code.Output.Process($"ProcessBarQueue: ERROR during direct SendAsync: {sendEx.Message}", PrintTo.OutputTab1); // Log send error
                                    // Optionally handle reconnection or queue retry here
                                }
                            }
                            else
                            {
                                // Log if WebSocket wasn't ready for sending
                                NinjaTrader.Code.Output.Process($"ProcessBarQueue: Skipping send for {packet.Instrument}, WebSocket not open. State: {webSocket?.State}", PrintTo.OutputTab1);
                                // Requeue maybe? For now, just log.
                                // barQueue.Enqueue(packet); // Be careful with requeueing logic
                            }
                            // --- End of Integrated Send Logic --- 

                        }
                        catch (Exception ex)
                        {
                            NinjaTrader.Code.Output.Process($"ProcessBarQueue: Error processing dequeued bar: {ex.Message}", PrintTo.OutputTab1); 
                        }
                    }
                    else
                    {
                       NinjaTrader.Code.Output.Process("ProcessBarQueue Loop: Dequeue failed (queue likely empty?).", PrintTo.OutputTab1); 
                }
                    NinjaTrader.Code.Output.Process("ProcessBarQueue Loop: End of loop iteration.", PrintTo.OutputTab1); 
                }
                 NinjaTrader.Code.Output.Process($"ProcessBarQueue Loop Exit. IsDisposed={IsDisposed()}, Cancelled={cancellationToken.IsCancellationRequested}, QueueEmpty={barQueue.IsEmpty}", PrintTo.OutputTab1); // Updated log
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested during Task.Delay
                NinjaTrader.Code.Output.Process("ProcessBarQueue Task Cancelled (OperationCanceledException).", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                 NinjaTrader.Code.Output.Process($"ProcessBarQueue Task: CRITICAL ERROR: {ex.Message} \n {ex.StackTrace}", PrintTo.OutputTab1); 
            }
            finally
            {
                NinjaTrader.Code.Output.Process("ProcessBarQueue Task Finishing.", PrintTo.OutputTab1); 
                lock (syncLock)
                {
                    processorRunning = false;
                    NinjaTrader.Code.Output.Process("ProcessBarQueue: processorRunning set to false.", PrintTo.OutputTab1);
                    
                    // Restart logic should also check token
                    if (!barQueue.IsEmpty && !IsDisposed() && !cancellationToken.IsCancellationRequested)
                    {
                        NinjaTrader.Code.Output.Process("ProcessBarQueue: More items in queue, restarting processor.", PrintTo.OutputTab1);
                        processorRunning = true;
                        Task.Run(() => ProcessBarQueue(cancellationToken), cancellationToken);
                    }
                }
            }
        }

        // Minimal bar sender - using HttpWebRequest instead of WebClient to fix Timeout issue
        private async Task SendBarMinimal(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe)
        {
            try
            {
                // Build the URL
                string endpoint = $"{baseUrl}/api/realtime_bars/{instrument}";
                
                // Convert timestamp to milliseconds since epoch
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                
                // Create a simple payload with minimal data
                string jsonPayload = $"{{\"timestamp\":{epochMs},\"open\":{open},\"high\":{high},\"low\":{low},\"close\":{close},\"volume\":{volume},\"timeframe\":\"{timeframe}\"}}";
                
                // Use HttpWebRequest with Timeout instead of WebClient
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 3000; // 3 second timeout
                
                // Write the payload
                byte[] data = Encoding.UTF8.GetBytes(jsonPayload);
                request.ContentLength = data.Length;
                
                // Create a task to send the request
                Task<Stream> getRequestStream = Task.Factory.FromAsync(
                    request.BeginGetRequestStream,
                    request.EndGetRequestStream,
                    null);
                
                // Once we have the request stream, write the data
                await getRequestStream.ContinueWith(async task => 
                {
                    using (Stream requestStream = task.Result)
                    {
                        await requestStream.WriteAsync(data, 0, data.Length);
                    }
                });
                
                // Set a timeout and don't wait for the response
                Task<WebResponse> getResponse = Task.Factory.FromAsync(
                    request.BeginGetResponse,
                    request.EndGetResponse,
                    null);
                
                // Add a timeout to the task
                var timeoutTask = Task.Delay(3000);
                
                // Wait for either the response or timeout
                if (await Task.WhenAny(getResponse, timeoutTask) == getResponse)
                {
                    // We got a response in time
                    using (WebResponse response = await getResponse)
                    {
                        Log($"Bar sent for {instrument}");
                    }
                }
                else
                {
                    // Timed out, cancel the request
                    Log($"Request timed out for {instrument}");
                }
            }
            catch
            {
                // Silently fail - the queue will move on
            }
        }

        // Send bar data to CurvesV2 server
        public async Task<bool> SendBarAsync(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            if (IsDisposed()) return false;

            // Rate limiting
            if ((DateTime.Now - lastBarSent).TotalMilliseconds < BarIntervalMs)
                return false;

            lastBarSent = DateTime.Now;

            try
            {
                // Convert timestamp to milliseconds since epoch
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;

                BarData barData = new BarData
                {
                    timestamp = epochMs,
                    open = open,
                    high = high,
                    low = low,
                    close = close,
                    volume = volume,
                    timeframe = timeframe
                };

                string jsonPayload = JsonConvert.SerializeObject(barData);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send to the realtime_bars endpoint
                var response = await client.PostAsync($"{baseUrl}/api/realtime_bars/{instrument}", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    Log($"Bar data sent successfully for {instrument}");
					NinjaTrader.Code.Output.Process($"Bar data sent successfully for {instrument}", PrintTo.OutputTab1);
                    return true;
                }
                else
                {
                    Log($"Failed to send bar data for {instrument} : {response.StatusCode}");
					NinjaTrader.Code.Output.Process($"Failed to send bar data for {instrument} : {response.StatusCode}", PrintTo.OutputTab1);

                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error sending bar data: {ex.Message}");
				NinjaTrader.Code.Output.Process($"Failed to send bar data for {instrument} : {ex.Message}", PrintTo.OutputTab1);
                return false;
            }
        }

        // Get signals from CurvesV2 server
        public async Task<bool> CheckSignalsAsync(string instrument)
        {
            if (IsDisposed()) return false;

            // Rate limiting
            if ((DateTime.Now - lastSignalCheck).TotalMilliseconds < SignalsIntervalMs)
                return false;

            lastSignalCheck = DateTime.Now;

            try
            {
                for (int attempt = 0; attempt < MaxRetries; attempt++)
                {
                    try
                    {
                        var response = await client.GetAsync($"{baseUrl}/api/signals/{instrument}");

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonResponse = await response.Content.ReadAsStringAsync();
                            var signalResponse = JsonConvert.DeserializeObject<CurvesV2Response>(jsonResponse);

                            if (signalResponse != null && signalResponse.success)
                            {
                                try {
                                    // Ensure signals is not null before accessing
                                    if (signalResponse.signals != null)
                                    {
                                        // Update signal strengths with null-coalescing operator
                                        CurrentBullStrength = signalResponse.signals.bull;
                                        CurrentBearStrength = signalResponse.signals.bear;
                                        
                                        // Create a new list if matches is null
                                        CurrentMatches = signalResponse.signals.matches ?? new List<PatternMatch>();
                                        
                                        // Safely set pattern name
                                        PatternName = (CurrentMatches != null && CurrentMatches.Count > 0 && CurrentMatches[0] != null) 
                                            ? CurrentMatches[0].patternName ?? "Unknown Pattern" 
                                            : "No Pattern";
                                        
                                        Log($"Updated signals: Bull {CurrentBullStrength}, Bear {CurrentBearStrength}");
                                        return true;
                                    }
                                    else
                                    {
                                        Log("Response signals property was null");
                                        return false;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"Error processing signal response: {ex.Message}");
                                    // Reset to safe defaults
                                    CurrentMatches = new List<PatternMatch>();
                                    PatternName = "No Pattern";
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            Log($"Failed to get signals: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error getting signals (attempt {attempt+1}): {ex.Message}");
                        if (attempt < MaxRetries - 1)
                            await Task.Delay(1000); // Wait 1 second before retrying
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Log($"Error checking signals: {ex.Message}");
                return false;
            }
        }

        // Report trade result back to CurvesV2 server
        public async Task<bool> ReportTradeResultAsync(string instrument, string patternId, DateTime entryTime, double entryPrice, 
                                                      DateTime exitTime, double exitPrice, double pnl, string direction)
        {
            if (IsDisposed()) return false;
            
            // Skip if pattern auction is disabled
            if (config != null && !config.EnablePatternAuction)
                return false;

            try
            {
                // Convert timestamps to milliseconds since epoch
                long entryEpochMs = (long)(entryTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                long exitEpochMs = (long)(exitTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;

                // Determine outcome status
                string status = pnl > 0 ? "win" : (pnl < 0 ? "loss" : "breakeven");

                TradeResult tradeResult = new TradeResult
                {
                    pattern_id = patternId,
                    entry_time = entryEpochMs,
                    entry_price = entryPrice,
                    exit_time = exitEpochMs,
                    exit_price = exitPrice,
                    pnl = pnl,
                    pnl_points = pnl, // Same as PnL for futures
                    direction = direction.ToLower(), // "bull" or "bear"
                    status = status
                };

                string jsonPayload = JsonConvert.SerializeObject(tradeResult);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send to the trade_results endpoint
                var response = await client.PostAsync($"{baseUrl}/api/signals/{instrument}/trade_results", content);
                
                if (response.IsSuccessStatusCode)
                {
                    Log($"Trade result reported: {direction} {status}, PnL: {pnl}");
                    return true;
                }
                else
                {
                    Log($"Failed to report trade result: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error reporting trade result: {ex.Message}");
                return false;
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

        // Connect to WebSocket server
        public async Task<bool> ConnectWebSocketAsync()
        {
            // Add a lock to prevent multiple concurrent connection attempts
            if (!Monitor.TryEnter(connectionStateLock, TimeSpan.FromSeconds(1))) // Attempt to acquire lock for 1 sec
            {
                NinjaTrader.Code.Output.Process("WebSocket connection attempt already in progress, skipping.", PrintTo.OutputTab1);
                return false; // Another thread is already trying to connect
            }

            try
            {
                // Don't try to connect if we're shutting down or disposed
                if (IsDisposed() || IsShuttingDown())
                {
                    NinjaTrader.Code.Output.Process("Cannot connect WebSocket - service is shutting down or disposed", PrintTo.OutputTab1);
                    return false;
                }
                
                // Don't reconnect if already connected and open
                if (webSocketConnected && webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    NinjaTrader.Code.Output.Process("WebSocket already connected and open.", PrintTo.OutputTab1);
                    return true;
                }

                // Reset connected flag if socket is not null but not open
                if (webSocket != null && webSocket.State != WebSocketState.Open) {
                     webSocketConnected = false;
                }

                // Implement connection throttling
                lock (connectionStateLock)
                {
                    if ((DateTime.UtcNow - lastConnectionAttempt).TotalSeconds < 5)
                    {
                        // Don't retry too frequently
                        if (consecutiveConnectionErrors > 0)
                        {
                            NinjaTrader.Code.Output.Process("Throttling WebSocket connection attempts", PrintTo.OutputTab1);
                    return false;
                }
                    }
                    lastConnectionAttempt = DateTime.UtcNow;
                }
                
                // Create a new WebSocket if needed OR if the existing one is closed/aborted
                if (webSocket == null || 
                    (webSocket.State != WebSocketState.Open && webSocket.State != WebSocketState.Connecting)) // Only create new if not Open or Connecting
                {
                    try
                    {
                        // Dispose old WebSocket if necessary
                        if (webSocket != null)
                        {
                            try
                            {
                                if (webSocket.State == WebSocketState.Open)
                                {
                                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Creating new connection", CancellationToken.None);
                                }
                                webSocket.Dispose();
            }
            catch (Exception ex)
            {
                                NinjaTrader.Code.Output.Process($"Error disposing old WebSocket: {ex.Message}", PrintTo.OutputTab1);
                            }
                        }

                        // Create a new WebSocket and CancellationTokenSource
                        webSocket = new ClientWebSocket();
                        webSocketCts = new CancellationTokenSource();
                        
                        // Set reasonable timeouts
                        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    }
                    catch (Exception ex)
                    {
                        NinjaTrader.Code.Output.Process($"Error creating WebSocket: {ex.Message}", PrintTo.OutputTab1);
                        
                        lock (connectionStateLock)
                        {
                            consecutiveConnectionErrors++;
                        }
                        
                        return false;
                    }
                }
                
                // Check WebSocket state *before* connecting
                if (webSocket.State == WebSocketState.Open) {
                     NinjaTrader.Code.Output.Process("WebSocket is already open before ConnectAsync call.", PrintTo.OutputTab1);
                    webSocketConnected = true;
                     return true; // Already connected
                }
                if (webSocket.State != WebSocketState.None && webSocket.State != WebSocketState.Closed) { // Check if it's in a state where connect can be called
                     NinjaTrader.Code.Output.Process($"WebSocket not in a connectable state ({webSocket.State}). Aborting ConnectAsync.", PrintTo.OutputTab1);
                     return false;
                }

                try
                {
                    // Check again if we're shutting down
                    if (IsDisposed() || IsShuttingDown())
                    {
                        NinjaTrader.Code.Output.Process("Aborting connection - service is shutting down", PrintTo.OutputTab1);
                        return false;
                    }
                    
                    NinjaTrader.Code.Output.Process($"Connecting to WebSocket: {wsUrl} (Current State: {webSocket.State})", PrintTo.OutputTab1);
                    
                    // Connect to the server
                await webSocket.ConnectAsync(new Uri(wsUrl), webSocketCts.Token);
                
                    // Check state AFTER connection attempt
                if (webSocket.State == WebSocketState.Open)
                {
                    webSocketConnected = true;
                        NinjaTrader.Code.Output.Process("WebSocket connected successfully after ConnectAsync.", PrintTo.OutputTab1);

                        // Start the receive loop only if connection successful and store the task
                        receiveLoopTask = Task.Run(ReceiveWebSocketMessagesAsync);

                        // *** Start the CORRECT PingLoopAsync task ***
                        pingLoopTask = Task.Run(() => PingLoopAsync(webSocketCts.Token));

                        // Reset consecutive errors counter
                        lock (connectionStateLock)
                        {
                            consecutiveConnectionErrors = 0; 
                        }
                        return true;
                }
                else
                {
                         NinjaTrader.Code.Output.Process($"WebSocket connection attempt finished, but state is {webSocket.State}", PrintTo.OutputTab1);
                    webSocketConnected = false;
                         return false;
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"WebSocket connection error: {ex.Message}", PrintTo.OutputTab1);
                webSocketConnected = false;
                    
                    lock (connectionStateLock)
                    {
                        consecutiveConnectionErrors++;
                    }
                    
                    return false;
                }
            }
            finally
            {
                // Ensure the lock is released
                Monitor.Exit(connectionStateLock);
            }
        }
        
        // Health check using HTTP to verify server is running
        public async Task<bool> CheckHealth()
        {
            if (IsDisposed())
                return false;
            
            // If WebSocket is already connected, consider the health check passed
            if (webSocketConnected)
            {
                NinjaTrader.Code.Output.Process("Health check passed: WebSocket already connected", PrintTo.OutputTab1);
                return true;
            }
                
            try
            {
                NinjaTrader.Code.Output.Process("Checking CurvesV2 server health...", PrintTo.OutputTab1);
                
                string healthEndpoint = $"{baseUrl}/";
                
                // Create a new temporary client with a short timeout
                using (var tempClient = new HttpClient())
                {
                    tempClient.Timeout = TimeSpan.FromSeconds(5); // 5 second timeout
                    
                    // Send a request to the root endpoint (health check)
                    var response = await tempClient.GetAsync(healthEndpoint);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        NinjaTrader.Code.Output.Process($"Health check passed: {content}", PrintTo.OutputTab1);
                        return true;
                    }
                    else
                    {
                        NinjaTrader.Code.Output.Process($"Health check failed: Status code {response.StatusCode}", PrintTo.OutputTab1);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"Health check error: {ex.Message}", PrintTo.OutputTab1);
                return false;
            }
        }
        
        // Receive messages from WebSocket
        private async Task ReceiveWebSocketMessagesAsync()
        {
            if (IsDisposed()) return;
            
            try
            {
                NinjaTrader.Code.Output.Process("Starting WebSocket message listener", PrintTo.OutputTab1);
                var buffer = new byte[4096];
                var messageBuffer = new StringBuilder();
                
                while (webSocket.State == WebSocketState.Open)
                {
                    try 
                    {
                        // Create a cancellation token that can be canceled when disposing
                        using (var receiveCts = new CancellationTokenSource())
                        {
                            receiveCts.CancelAfter(30000); // 30 second timeout for long-polling
                            
                            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), receiveCts.Token);
                            
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                NinjaTrader.Code.Output.Process("WebSocket close frame received", PrintTo.OutputTab1);
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                                webSocketConnected = false;
                                break;
                            }
                            
                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                
                                // NinjaTrader.Code.Output.Process($"WebSocket message received: {message}", PrintTo.OutputTab1); // <<< COMMENT OUT
                                try 
                                {
                                    // Parse JSON response
                                    var responseObj = JsonConvert.DeserializeObject<dynamic>(message);
                                    string messageType = responseObj?.type;
                                    
                                    // Handle different message types
                                    if (messageType == "bar_response")
                                    {
                                        // NinjaTrader.Code.Output.Process("Bar data successfully processed by server", PrintTo.OutputTab1); // Keep commented or uncomment if needed
                                    }
                                    else if (messageType == "error")
                                    {
                                        NinjaTrader.Code.Output.Process($"Server reported error: {responseObj?.message}", PrintTo.OutputTab1);
                                    }
                                    else if (messageType == "pong")
                                    {
                                        // NinjaTrader.Code.Output.Process($"Received pong for ping ID: {responseObj?.id}", PrintTo.OutputTab1); // Keep commented or uncomment if needed
                                        receivedPong = true;
                                        missedHeartbeats = 0;
                                    }
                                    else if (messageType == "backtest") 
                                    {
                                        // Server sends 'backtest' responses for test messages - log them only
                                        // NinjaTrader.Code.Output.Process($"Received backtest test message: method={responseObj?.method}, instrument={responseObj?.instrument}", PrintTo.OutputTab1); // <<< COMMENT OUT
                                    }
                                    else if (messageType == "welcome")
                                    {
                                        NinjaTrader.Code.Output.Process($"Received welcome message: {responseObj?.message}", PrintTo.OutputTab1);
                                    }
                                    else 
                                    {
                                        // Unknown message type - log it
                                        NinjaTrader.Code.Output.Process($"Received unknown message type: {messageType}", PrintTo.OutputTab1);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    NinjaTrader.Code.Output.Process($"Error parsing WebSocket message: {ex.Message}", PrintTo.OutputTab1);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // This is normal for the timeout, just continue
                        continue;
                    }
                    catch (Exception innerEx) 
                    {
                        NinjaTrader.Code.Output.Process($"Error during message receive: {innerEx.Message}", PrintTo.OutputTab1);
                        if (webSocket.State != WebSocketState.Open)
                        {
                            NinjaTrader.Code.Output.Process($"WebSocket state changed to {webSocket.State}, exiting receive loop", PrintTo.OutputTab1);
                            break;
                        }
                    }
                }
                
                NinjaTrader.Code.Output.Process($"WebSocket receive loop ended. Socket state: {webSocket.State}", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"WebSocket receive error: {ex.Message}", PrintTo.OutputTab1);
                if (ex.InnerException != null)
                {
                    NinjaTrader.Code.Output.Process($"Inner exception: {ex.InnerException.Message}", PrintTo.OutputTab1);
                }
                webSocketConnected = false;
            }
            
            // Try to reconnect if disconnected
            if (!IsDisposed() && !webSocketConnected)
            {
                NinjaTrader.Code.Output.Process("WebSocket disconnected, attempting to reconnect in 5 seconds...", PrintTo.OutputTab1);
                await Task.Delay(5000);
			
                await ConnectWebSocketAsync();
            }
        }
        
        // Send bar data using WebSocket
        public bool SendBarWebSocket(string instrument, DateTime timestamp, double open, double high, double low, double close, double volume, string timeframe = "1m")
        {
            if (IsDisposed() || webSocket == null || webSocket.State != WebSocketState.Open)
                return false;
                
            try
            {
                // Create a unique request ID
                string requestId = Guid.NewGuid().ToString("N");
                
                // Convert timestamp to milliseconds since epoch
                long epochMs = (long)(timestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                
                // Create the message object
                var barMessage = new
                {
                    type = "bar",
                    requestId = requestId,
                    instrument = instrument,
                    timestamp = epochMs,
                    open = open,
                    high = high,
                    low = low,
                    close = close,
                    volume = volume,
                    timeframe = timeframe
                };
                
                // Serialize to JSON
                string jsonMessage = JsonConvert.SerializeObject(barMessage);
                byte[] messageBytes = Encoding.UTF8.GetBytes(jsonMessage);
                
                // Start fire-and-forget task to send the message
                Task.Run(async () => {
                    try
                    {
                        if (webSocket != null && webSocket.State == WebSocketState.Open && !IsDisposed())
                        {
                            await webSocket.SendAsync(
                                new ArraySegment<byte>(messageBytes),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None
                            );
                            
                            NinjaTrader.Code.Output.Process($"Bar sent via WebSocket for {instrument}", PrintTo.OutputTab1);
                        }
                    }
                    catch (Exception ex)
                    {
                        NinjaTrader.Code.Output.Process($"Error sending bar via WebSocket: {ex.Message}", PrintTo.OutputTab1);
                    }
                });
                
                return true;
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"Error preparing WebSocket bar: {ex.Message}", PrintTo.OutputTab1);
                return false;
            }
        }

        // Background task to periodically send pings
        private async Task PingLoopAsync(CancellationToken cancellationToken)
        {
            NinjaTrader.Code.Output.Process("Ping loop started.", PrintTo.OutputTab1);
            while (webSocket != null && webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, cancellationToken); // Send ping every 15 seconds

                    if (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        // Send asynchronously and await the result
                        // Lock only the SendAsync call
                        bool lockTaken = false;
                        try
                        {
                            Monitor.TryEnter(webSocketSendLock, 500, ref lockTaken);
                            if (lockTaken)
                            {
                                // Define payload and buffer INSIDE the lock, before SendAsync
                                NinjaTrader.Code.Output.Process("PingLoop: Lock acquired. Sending WebSocket ping...", PrintTo.OutputTab1);
                                string pingPayload = JsonConvert.SerializeObject(new { type = "ping", id = Guid.NewGuid().ToString("N").Substring(0, 8), timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                                byte[] buffer = Encoding.UTF8.GetBytes(pingPayload);
                                
                                var sendPingTask = webSocket.SendAsync(
                                    new ArraySegment<byte>(buffer), 
                                    WebSocketMessageType.Text, 
                                    true, 
                                    cancellationToken 
                                ); 
                                await sendPingTask; 
                            }
                            else
                            {
                                NinjaTrader.Code.Output.Process("PingLoop: Could not acquire send lock for SendAsync, skipping ping.", PrintTo.OutputTab1);
                                continue; // Skip rest of loop iteration if lock not acquired
                            }
                        }
                        finally
                        {
                            if(lockTaken) Monitor.Exit(webSocketSendLock);
                        }

                        // Check if cancellation occurred during await
                        if (cancellationToken.IsCancellationRequested)
                        {
                             NinjaTrader.Code.Output.Process("PingLoop: SendAsync cancelled during await.", PrintTo.OutputTab1);
                             break; // Exit loop if cancelled
                        }

                        NinjaTrader.Code.Output.Process("PingLoop: WebSocket ping sent successfully (async await).", PrintTo.OutputTab1);
                            missedHeartbeats = 0; // Reset missed heartbeats
                    }
                }
                catch (OperationCanceledException)
                {
                    NinjaTrader.Code.Output.Process("PingLoop: SendAsync cancelled (timeout or external).", PrintTo.OutputTab1);
                    missedHeartbeats++;
                }
                catch (Exception ex)
                {
                    NinjaTrader.Code.Output.Process($"PingLoop: Error sending ping: {ex.Message}", PrintTo.OutputTab1);
                    missedHeartbeats++;
                }
                
                // Check for too many missed heartbeats
                if (missedHeartbeats > 3)
                {
                     NinjaTrader.Code.Output.Process("PingLoop: Too many missed heartbeats, triggering reconnect.", PrintTo.OutputTab1);
                     webSocketConnected = false; // Mark as disconnected
                     break; // Exit loop to trigger reconnect logic
                }
            }
            
            // If we got here, the connection is probably closed - try to reconnect
            if (!IsDisposed() && !webSocketConnected)
            {
                NinjaTrader.Code.Output.Process("WebSocket connection lost from heartbeat thread, attempting to reconnect...", PrintTo.OutputTab1);
                await Task.Delay(5000);
                await ConnectWebSocketAsync();
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
                Log($"[DEBUG HTTP] SendBarFireAndForget: Called for {instrument} @ {timestamp}");
                
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
                Log($"[DEBUG HTTP] SendBarFireAndForget: Skipping WebSocket, forcing HTTP.");
                // --- END TEMPORARY DEBUG ---

                // Fallback to HTTP
                // Build the URL for the endpoint
                string endpoint = $"{baseUrl}/api/bars/{instrument}"; // Use /api/bars endpoint for simple post
                
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
                                                Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP bar data sent successfully for {instrument}");
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
                    
                    Log($"[DEBUG HTTP] SendBarFireAndForget: HTTP request initiated for {instrument}");
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
        public bool CheckSignalsFireAndForget(string instrument)
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
                Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Preparing check for {instrument}");
                string endpoint = $"{baseUrl}/api/signals/{instrument}";

                if (IsDisposed() || IsShuttingDown())
                {
                    Log("[DEBUG SYNC] CheckSignalsFireAndForget: Aborted due to service shutdown before GET");
                    return false; 
                }

                Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Sending GET to {endpoint} (Blocking)");
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) 
                {
                    response = client.GetAsync(endpoint, timeoutCts.Token).GetAwaiter().GetResult(); 
                }

                Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Received status {response.StatusCode} for {instrument}");

                if (response.IsSuccessStatusCode)
                {
                    string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); 
                    Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Response content for {instrument}: {responseText.Substring(0, Math.Min(responseText.Length, 100))}...");

                    var signalResponse = JsonConvert.DeserializeObject<CurvesV2Response>(responseText);

                    if (signalResponse != null && signalResponse.success)
                    {
                        Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Parsed success for {instrument}");
                        if (signalResponse.signals != null)
                        {                                    
                            Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Updating signals for {instrument}...");
                            CurrentBullStrength = signalResponse.signals.bull;
                            CurrentBearStrength = signalResponse.signals.bear;
                            LastSignalTimestamp = DateTime.Now; 
                            CurrentMatches = signalResponse.signals.matches ?? new List<PatternMatch>();
                            PatternName = CurrentMatches.Count > 0 ? (CurrentMatches[0]?.patternName ?? "No Pattern") : "No Pattern";
                            Log($"[DEBUG SYNC] CheckSignalsFireAndForget: Updated signals for {instrument}: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}");
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
                                        
                                        NinjaTrader.Code.Output.Process($"PollSignalsSync: Updated signals for {instrument}: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}", PrintTo.OutputTab1);
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
                CheckSignalsFireAndForget(instrument);
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
                 client = new HttpClient(); // Simple initialization, consider reusing instance
                 client.DefaultRequestHeaders.Accept.Clear();
                 client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                 // Consider setting BaseAddress if appropriate
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
    }

} 