/*
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


namespace NinjaTrader.NinjaScript.Strategies
{
	

    public class SignalResponse
	{

	    public double bull_strength { get; set; }
	    public double bear_strength { get; set; }
	    public string test_string { get; set; }
	    public List<Signal> signals { get; set; }
	}
	 
	
	public class DetectionsResponse
	{
	    public Dictionary<string, SignalResponse> instruments { get; set; }
	    public long timestamp { get; set; }
	}
	
	
    public class MinimalPythonService : IDisposable
    {
        private readonly HttpClient client;
        private readonly string baseUrl;
	  	private readonly string statesUrl;
		private readonly string healthUrl;
        private bool disposed;
        private readonly Action<string> logger;
        private const int MaxRetries = 3;
        private dynamic lastSentMetrics = null;  // Store last sent metrics
        private DateTime lastMetricsSent = DateTime.MinValue;
        private DateTime lastBarSent = DateTime.MinValue;
        private DateTime lastL2Sent = DateTime.MinValue;
        private DateTime lastSignalCheck = DateTime.MinValue;
        private readonly object disposeLock = new object();

        // Add current strength properties
        public static double CurrentBullStrength { get; private set; }
        public static double CurrentBearStrength { get; private set; }
		public static string test_string { get; private set; }
		public static string patternName { get; private set; }
	
        // Rate limiting in milliseconds
        public int MetricsIntervalMs { get; set; } = 1000;  // 1 second
        public int BarIntervalMs { get; set; } = 1000;      // 1 second
        public int L2IntervalMs { get; set; } = 1000;       // 1 second
        public int SignalsIntervalMs { get; set; } = 1000;  // 1 second

		
         public MinimalPythonService(bool useRemoteService, int port, Action<string> logger = null)
	    {
	        string host = useRemoteService ? "pattern-detection-platform.onrender.com" : "localhost";
	        string protocol = useRemoteService ? "https" : "http";
	        
	        // For Render, don't include the port in the URL
	        this.baseUrl = useRemoteService 
	            ? $"{protocol}://{host}" 
	            : $"{protocol}://{host}:{port}"; // This will be port 3001
	            
	        // Add states server URL (port 3006)
	        this.statesUrl = useRemoteService
	            ? $"{protocol}://{host}:3001"
	            : $"{protocol}://{host}:3001";
			
			// Add states server URL (port 3006)
	        this.healthUrl = useRemoteService
	            ? $"{protocol}://{host}:3001"
	            : $"{protocol}://{host}:3001";
	            
	        Print($"ENDPOINT! {this.baseUrl}");
	        Print($"STATES ENDPOINT! {this.statesUrl}");
	        this.client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
	        this.logger = logger ?? (msg => { });
	    }

        private string SignalsUrl => baseUrl;  // Use same base URL for signals

        private bool IsDisposed()
        {
            lock (disposeLock)
            {
                return disposed;
            }
        }

        private void ValidateNotDisposed()
        {
            if (IsDisposed())
            {
                throw new ObjectDisposedException(nameof(MinimalPythonService));
            }
        }

        private bool IsValidValue(double value)
        {
            return !double.IsInfinity(value) && !double.IsNaN(value);
        }

        public async Task<bool> CheckHealth()
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    NinjaTrader.Code.Output.Process($"Health check {baseUrl}/health, attempt {attempt}/{MaxRetries}...", PrintTo.OutputTab1);
                    var response = await client.GetAsync($"{baseUrl}/api/health");
                    var content = await response.Content.ReadAsStringAsync();
                    logger($"Health check response: {content}");
                    
                    // Parse the response to check status
                    var result = JsonConvert.DeserializeAnonymousType(content, new { status = "", message = "" });
                    if (result.status == "ok")
                        return true;
                    else if (result.status == "initializing")
                    {
                        logger("Server is still initializing, waiting...");
                        await Task.Delay(1000);
                        continue;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    logger($"Health check attempt {attempt} failed: {ex.Message}");
                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(1000);
                    }
                }
            }
            return false;
        }

        public async Task SendBar(DateTime time, double open, double high, double low, double close, double volume, string instrument)
        {
            try
            {
                var data = new
                {
                    timestamp = time.ToString("O"),
                    high = high,
                    low = low,
                    open = open,
                    close = close,
                    volume = volume,
                    instrument = instrument.Split(' ')[0]  // Extract ES or NQ
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(data),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await client.PostAsync($"{baseUrl}/api/realtime_bars/{instrument.Split(' ')[0]}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                Print($"Bar data response: {responseContent}");
            }
            catch (Exception ex)
            {
                logger($"Failed to send bar data: {ex.Message}");
            }
        }

       
       

        public async Task SendHLOCV(DateTime time, double high, double low, double open, double close, double volume, string instrument)
        {
            var now = DateTime.UtcNow;
            if ((now - lastBarSent).TotalMilliseconds < BarIntervalMs)
            {
                return; // Skip if too soon
            }
            lastBarSent = now;

            try
            {
                var data = new
                {
                    timestamp = time.ToString("O"),
                    high = high,
                    low = low,
                    open = open,
                    close = close,
                    volume = volume,
                    instrument = instrument.Split(' ')[0]  // Extract ES or NQ
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(data),
                    Encoding.UTF8,
                    "application/json"
                );

                ///var response = await client.PostAsync($"{baseUrl}/bar_update/{instrument}", content); /// old
				var response = await client.PostAsync($"{baseUrl}/api/realtime_bars/{instrument}", content); /// new
				var responseContent = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                logger($"Failed to send bar data: {ex.Message}");
            }
        }

        public async Task SendBarData(object barData)
        {
            // Delegate to SendHLOCV for consistent handling
            try
            {
                dynamic data = barData;
                string instrument = data.instrument.ToString().Split(' ')[0]; // Extract ES or NQ
                await SendHLOCV(
                    DateTime.Parse(data.timestamp.ToString()),
                    data.high,
                    data.low,
                    data.open,
                    data.close,
                    data.volume,
                    instrument
                );
            }
            catch (Exception ex)
            {
                logger($"Failed to send bar data: {ex.Message}");
            }
        }

		
		
		// Store active signals at class level
		public List<Signal> ActiveSignals { get; private set; } = new List<Signal>();
		private DateTime lastSignalRequestTime = DateTime.MinValue;
		private readonly TimeSpan signalRequestThrottle = TimeSpan.FromMilliseconds(500); // Adjust as needed
		private readonly SemaphoreSlim signalSemaphore = new SemaphoreSlim(1, 1);
		
		public class DetectionResponse
		{
		    public bool success { get; set; }
		    public string message { get; set; }
		    public DetectionData data { get; set; }
		}
		
		public class DetectionData
		{
		    public bool success { get; set; }
		    public string instrument { get; set; }
		    public Detection detection { get; set; }
		}
		
		public class Detection
		{
		    public List<Signal> matches { get; set; }
		    public int bull_strength { get; set; }
		    public int bear_strength { get; set; }
		    public string signal { get; set; }
		    public long last_update { get; set; }
		    public List<Signal> signals { get; set; }
		}
		

		/// <summary>
		/// async
		/// </summary>
		/// <param name="instrument"></param>
		/// <returns></returns>
		public async Task<(double bull, double bear, List<Signal> signals)> GetSignals(string instrument)
		{
		    // Check if we should throttle
		    var now = DateTime.UtcNow;
		    if ((now - lastSignalRequestTime) < signalRequestThrottle)
		    {
		        // Return the last known values instead of making a new request
		        return (CurrentBullStrength, CurrentBearStrength, ActiveSignals);
		    }
		    
		    // Try to enter the semaphore, but don't wait if it's locked
		    if (!await signalSemaphore.WaitAsync(0))
		    {
		        // Return the last known values if we can't acquire the semaphore
		        return (CurrentBullStrength, CurrentBearStrength, ActiveSignals);
		    }
		    
		    try
		    {
		        // Update the last request time
		        lastSignalRequestTime = now;
		        
		        string baseInstrument = instrument.Split(' ')[0];  // Extract ES or NQ
		        
		        // Create a request with authentication
		        var request = new HttpRequestMessage(HttpMethod.Get, $"{statesUrl}/api/detections/{baseInstrument}");
				
		        // Add Basic Authentication header
		        //string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(":thatfuckingdog"));
		        //request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
		        
		        // Send the request with a timeout
		  
		       var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				var response = await client.SendAsync(request, cts.Token);
				
				CurrentBullStrength = 0;
				CurrentBearStrength = 0;
				
				if (response.IsSuccessStatusCode)
				{
				    var content = await response.Content.ReadAsStringAsync();
				    
				    if (!string.IsNullOrWhiteSpace(content))
				    {
				        var responseObj = JsonConvert.DeserializeObject<DetectionResponse>(content);
				        
				        if (responseObj != null && 
				            responseObj.success && 
				            responseObj.data != null && 
				            responseObj.data.detection != null)
				        {
				            // Store strength values
				            CurrentBullStrength = responseObj.data.detection.bull_strength;
				            CurrentBearStrength = responseObj.data.detection.bear_strength;
				            
				            // Store signals
				            ActiveSignals = MakeActiveSignalsList(responseObj.data.detection);
				            
				            return (CurrentBullStrength, CurrentBearStrength, ActiveSignals);
				        }
				        else
				        {
				            Print($"No valid detection data found for instrument: {baseInstrument}");
				        }
				    }
				    else
				    {
				        Print("Received empty response content");
				    }
				}
				else
				{
				    Print($"API request failed: {response.StatusCode} - {response.ReasonPhrase}");
				    var errorContent = await response.Content.ReadAsStringAsync();
				    if (!string.IsNullOrWhiteSpace(errorContent))
				    {
				        Print($"Error details: {errorContent}");
				    }
				}
		        
		        // If we get here, something went wrong but we didn't throw an exception
		        return (CurrentBullStrength, CurrentBearStrength, ActiveSignals);
		    }
		    catch (TaskCanceledException)
		    {
		        Print("Signal request timed out, using last known values");
		        return (CurrentBullStrength, CurrentBearStrength, ActiveSignals);
		    }
		    catch (Exception ex)
		    {
		        Print($"Error getting signals: {ex.Message}");
		        return (CurrentBullStrength, CurrentBearStrength, ActiveSignals);
		    }
		    finally
		    {
		        // Always release the semaphore
		        signalSemaphore.Release();
		    }
		}
		
	

		public List<Signal> MakeActiveSignalsList(Detection detection)
		{
		    List<Signal> signals = new List<Signal>();
		    
		    try
		    {
		        // Check if we have signals in the response
		        if (detection.signals == null)
		            return signals;
		            
		        // Convert each signal to a Signal object
		        foreach (var item in detection.signals)
		        {
		            signals.Add(new Signal
		            {
		                type = item.type,
		                confidence = item.confidence,
		                timestamp = item.timestamp,
		                pattern_id = item.pattern_id,
		                pattern_name = item.pattern_name
		            });
		        }
		        
		        // Sort by confidence (highest first)
		        signals = signals.OrderByDescending(s => s.confidence).ToList();
		    }
		    catch (Exception ex)
		    {
		        Print($"Error creating signals list: {ex.Message}");
		    }
		    
		    return signals;
		}
				
        public async Task SendHistoricalBars(List<object> bars, string instrument)
        {
            try
            {
                string baseInstrument = instrument.Split(' ')[0];  // Extract ES or NQ
                const int batchSize = 100;  // Send 100 bars at a time
                
                for (int i = 0; i < bars.Count; i += batchSize)
                {
                    // Get current batch
                    var batch = bars.Skip(i).Take(batchSize).ToList();
                    
                    // Restructure data to match server expectations
                    var data = new
                    {
                        strategy_id = baseInstrument,  // Required by server
						type = "bad_data",
						instrument = baseInstrument,
                        bars = batch.Select(bar => {
                            dynamic b = bar;
                            return new {
                                timestamp = b.timestamp.ToString("O"),  // Ensure timestamp is formatted
                                high = (double)b.high,
                                low = (double)b.low,
                                open = (double)b.open,
                                close = (double)b.close,
                                volume = (int)b.volume
                           
                            };
                        }).ToList()
                    };

                    var json = JsonConvert.SerializeObject(data);
                    logger($"Sending batch {i}/{bars.Count} - Sample data: {json.Substring(0, Math.Min(json.Length, 500))}");

                    var content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    );

					var response = await client.PostAsync($"{baseUrl}/api/bars/{baseInstrument}", content); ///new
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Server returned error: {response.StatusCode} - {responseContent}");
                    }
                    
                    // Brief delay between batches
                    await Task.Delay(100);
                    
                    // Log progress every 1000 bars
                    if (i % 1000 == 0)
                    {
                        logger($"Sent {i} of {bars.Count} bars...");
                    }
                }
                
                return;
            }
            catch (Exception ex)
            {
                logger($"Failed to send historical bars: {ex.Message}");
                throw; // Re-throw to allow retry logic in MicroStrategy to work
            }
        }
		// When you exit a position, send data to the review server
		// In your Python service class
public async Task ReportPositionExit(string instrument, double entryPrice, double exitPrice, 
    DateTime entryTime, DateTime exitTime, List<Signal> signals, string marketPosition, double profit)
{
    try
    {
		string baseInstrument = instrument.Split(' ')[0];  // Extract ES or NQ

        // Convert signals to a format that can be serialized
        var signalData = signals.Select(s => new
        {
            type = s.type,
            confidence = s.confidence,
            timestamp = s.timestamp,
            pattern_id = s.pattern_id,
            pattern_name = s.pattern_name
        }).ToList();

        var positionData = new
        {
            instrument = instrument,
            timestamp_start = entryTime.ToString("o"),
            timestamp_end = exitTime.ToString("o"),
            signals = signalData,  // Pass the entire signals array
            MarketPosition = marketPosition,
            profit = profit,
            exit_price = exitPrice,
            start_price = entryPrice
        };

        using (var client = new HttpClient())
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(positionData),
                Encoding.UTF8,
                "application/json");
                
            var response = await client.PostAsync($"{baseUrl}/api/positions/{baseInstrument}", content);

            
            if (!response.IsSuccessStatusCode)
            {
                Print("Failed to report position exit: " + response.StatusCode);
            }
        }
    }
    catch (Exception ex)
    {
        Print("Error reporting position exit: " + ex.Message);
    }
}
		

        public void Dispose()
        {
            lock (disposeLock)
            {
                if (!disposed)
                {
                    client?.Dispose();
                    disposed = true;
                }
            }
        }

        private void Print(string message)
        {
            NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab1);
        }

        private void ProcessSignal(dynamic signal)
        {
            try
            {
                // Handle confidence messages from WebSocket
                if (signal.type?.ToString() == "confidence" && signal.data != null)
                {
                    Print($"Received confidence message - Raw data: {JsonConvert.SerializeObject(signal)}");
                    
                    CurrentBullStrength = signal.data.bull?.ToObject<double>() ?? 0.0;
                    CurrentBearStrength = signal.data.bear?.ToObject<double>() ?? 0.0;
                    
                    Print($"Processed confidence values - Bull: {CurrentBullStrength}, Bear: {CurrentBearStrength}");
                    return;
                }

                // Handle direct signals from L2 updates
                if (signal.status?.ToString() == "success")
                {
                    // Update current strengths from signal response
                    CurrentBullStrength = signal.bull_strength?.ToObject<double>() ?? 0.0;
                    CurrentBearStrength = signal.bear_strength?.ToObject<double>() ?? 0.0;
                    
                    if (CurrentBullStrength > 0 || CurrentBearStrength > 0)
                    {
                       // Print($"Signal returned - Bull: {CurrentBullStrength}, Bear: {CurrentBearStrength}");
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Error processing signal: {ex.Message}");
            }
        }

     
        
    }
} 
*/