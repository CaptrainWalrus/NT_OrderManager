using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Custom.OrderManager;

namespace NinjaTrader.Custom.OrderManager
{
    /// <summary>
    /// Backtest visualization integration for NinjaTrader strategies.
    /// Sends position entry/exit events to the backtest visualization service.
    /// </summary>
    public class BacktestVisualizer
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private bool _isAvailable;
        private DateTime _lastHealthCheck;
        
        /// <summary>
        /// Initialize backtest visualizer with the specified API URL.
        /// </summary>
        /// <param name="apiUrl">API URL of visualization service (default: http://localhost:3007)</param>
        public BacktestVisualizer(string apiUrl = "http://localhost:3007")
        {
            _baseUrl = apiUrl;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(2); // Short timeout to avoid blocking strategy
            _isAvailable = false;
            _lastHealthCheck = DateTime.MinValue;
        }
        
        /// <summary>
        /// Checks if the visualization service is available.
        /// Caches result for 5 seconds to avoid excessive requests.
        /// </summary>
        /// <returns>True if service is available</returns>
        public async Task<bool> IsAvailableAsync()
        {
            // Use cached result if checked recently
            if (DateTime.Now - _lastHealthCheck < TimeSpan.FromSeconds(5))
                return _isAvailable;
                
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/health");
                _isAvailable = response.IsSuccessStatusCode;
                _lastHealthCheck = DateTime.Now;
                return _isAvailable;
            }
            catch
            {
                _isAvailable = false;
                _lastHealthCheck = DateTime.Now;
                return false;
            }
        }
        
        /// <summary>
        /// Notify the visualization service of a position entry.
        /// </summary>
        /// <param name="direction">Trade direction ("long" or "short")</param>
        /// <param name="price">Entry price</param>
        /// <returns>True if successful</returns>
        public async Task<bool> NotifyPositionEntryAsync(string direction, double price)
        {
            if (!await IsAvailableAsync())
                return false;
                
            try
            {
                var entryData = new
                {
                    direction = direction.ToLower(),
                    price = price,
                    timestamp = DateTimeToUnixMs(DateTime.Now)
                };
                
                var json = JsonConvert.SerializeObject(entryData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/position/entry", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Notify the visualization service of a position exit.
        /// </summary>
        /// <param name="price">Exit price</param>
        /// <param name="profit">Profit/loss amount</param>
        /// <returns>True if successful</returns>
        public async Task<bool> NotifyPositionExitAsync(double price, double profit)
        {
            if (!await IsAvailableAsync())
                return false;
                
            try
            {
                var exitData = new
                {
                    price = price,
                    profit = profit,
                    timestamp = DateTimeToUnixMs(DateTime.Now)
                };
                
                var json = JsonConvert.SerializeObject(exitData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/position/exit", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Start a new backtest visualization session.
        /// Call this at the beginning of a new backtest.
        /// </summary>
        /// <returns>Session ID if successful, null otherwise</returns>
        public async Task<string> StartNewSessionAsync()
        {
            if (!await IsAvailableAsync())
                return null;
                
            try
            {
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/session/start", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    dynamic result = JsonConvert.DeserializeObject(responseContent);
                    return result.sessionId;
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Convert DateTime to Unix timestamp (milliseconds).
        /// </summary>
        /// <param name="dateTime">DateTime to convert</param>
        /// <returns>Unix timestamp in milliseconds</returns>
        private static long DateTimeToUnixMs(DateTime dateTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(dateTime.ToUniversalTime() - epoch).TotalMilliseconds;
        }
    }
    
    /// <summary>
    /// Extension methods for NinjaScript strategies to use the BacktestVisualizer.
    /// </summary>
    public static class BacktestVisualizerExtensions
    {
        /// <summary>
        /// Notify the visualization service of a position entry.
        /// </summary>
        /// <param name="strategy">The strategy</param>
        /// <param name="visualizer">The BacktestVisualizer instance</param>
        /// <param name="position">Position object</param>
        /// <returns>True if successful</returns>
        public static async Task<bool> NotifyVisualizerEntryAsync(
            this Strategy strategy, 
            BacktestVisualizer visualizer, 
            Position position)
        {
            if (visualizer == null || position == null)
                return false;
                
            string direction = position.Quantity > 0 ? "long" : "short";
            double price = position.AveragePrice;
            
            return await visualizer.NotifyPositionEntryAsync(direction, price);
        }
        
        /// <summary>
        /// Notify the visualization service of a position exit.
        /// </summary>
        /// <param name="strategy">The strategy</param>
        /// <param name="visualizer">The BacktestVisualizer instance</param>
        /// <param name="price">Exit price</param>
        /// <param name="profit">Profit/loss amount</param>
        /// <returns>True if successful</returns>
        public static async Task<bool> NotifyVisualizerExitAsync(
            this Strategy strategy, 
            BacktestVisualizer visualizer, 
            double price, 
            double profit)
        {
            if (visualizer == null)
                return false;
                
            return await visualizer.NotifyPositionExitAsync(price, profit);
        }
    }
} 