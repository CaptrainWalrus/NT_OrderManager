using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    // Configuration class for CurvesV2 integration
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class CurvesV2Config
    {
        // Server connection settings
        [Display(Name = "Server URL", Description = "CurvesV2 server URL", Order = 1, GroupName = "Connection")]
        public string ServerUrl { get; set; } = "http://localhost";

        [Display(Name = "Use Remote Server", Description = "Use remote CurvesV2 server instead of localhost", Order = 2, GroupName = "Connection")]
        public bool UseRemoteServer { get; set; } = false;

        [Display(Name = "Remote Server URL", Description = "Remote CurvesV2 server URL", Order = 3, GroupName = "Connection")]
        public string RemoteServerUrl { get; set; } = "https://curves-v2-server.example.com";

        [Display(Name = "Main Server Port", Description = "Main UI server port (default: 3001)", Order = 4, GroupName = "Connection")]
        public int MainServerPort { get; set; } = 3001;

        [Display(Name = "Signal Server Port", Description = "Signal server port (default: 3002)", Order = 5, GroupName = "Connection")]
        public int SignalServerPort { get; set; } = 3002;

        [Display(Name = "Signal Pool Port", Description = "Signal Pool service port (default: 3004)", Order = 6, GroupName = "Connection")]
        public int SignalPoolPort { get; set; } = 3004;

        [Display(Name = "Signal Pool WebSocket Port", Description = "Signal Pool WebSocket port (default: 3005)", Order = 7, GroupName = "Connection")]
        public int SignalPoolWebSocketPort { get; set; } = 3005;
        
        // WebSocket settings
        [Display(Name = "Use WebSocket Only", Description = "Use WebSocket exclusively for all communications", Order = 8, GroupName = "WebSocket")]
        public bool UseWebSocketOnly { get; set; } = true;
        
        [Display(Name = "WebSocket Backtest Timeout (ms)", Description = "Timeout for backtest WebSocket requests in milliseconds", Order = 9, GroupName = "WebSocket")]
        [Range(1, 60000)]
        public int WebSocketBacktestTimeoutMs { get; set; } = 250;
        
        [Display(Name = "WebSocket Reconnect Attempts", Description = "Number of reconnection attempts for WebSocket", Order = 10, GroupName = "WebSocket")]
        [Range(1, 10)]
        public int WebSocketReconnectAttempts { get; set; } = 3;

        // Signal thresholds
        [Display(Name = "Bull Signal Threshold", Description = "Minimum value for bull signal", Order = 11, GroupName = "Signals")]
        [Range(0, 100)]
        public int BullSignalThreshold { get; set; } = 70;

        [Display(Name = "Bear Signal Threshold", Description = "Minimum value for bear signal", Order = 12, GroupName = "Signals")]
        [Range(0, 100)]
        public int BearSignalThreshold { get; set; } = 70;

        [Display(Name = "Pattern Confidence Threshold", Description = "Minimum confidence for pattern matching", Order = 13, GroupName = "Signals")]
        [Range(0, 1)]
        public double PatternConfidenceThreshold { get; set; } = 0.7;

        // Rate limiting
        [Display(Name = "Bar Data Interval (ms)", Description = "Minimum time between bar data updates in milliseconds", Order = 14, GroupName = "Rate Limiting")]
        [Range(100, 10000)]
        public int BarDataIntervalMs { get; set; } = 1000;

        [Display(Name = "Signal Check Interval (ms)", Description = "Minimum time between signal checks in milliseconds", Order = 15, GroupName = "Rate Limiting")]
        [Range(100, 10000)]
        public int SignalCheckIntervalMs { get; set; } = 1000;
        
        [Display(Name = "Signal Poll Interval (ms)", Description = "Interval for automatic signal polling in milliseconds", Order = 16, GroupName = "Rate Limiting")]
        [Range(1000, 10000)]
        public int SignalPollIntervalMs { get; set; } = 3000;

        // Synchronous mode settings
        [Display(Name = "Enable Sync Mode", Description = "Use synchronous processing for NinjaTrader integration", Order = 17, GroupName = "Processing")]
        public bool? EnableSyncMode { get; set; } = null;

        // Logging
        [Display(Name = "Enable Detailed Logging", Description = "Enable detailed logging of API calls", Order = 18, GroupName = "Logging")]
        public bool EnableDetailedLogging { get; set; } = false;

        // Pattern auction settings
        [Display(Name = "Enable Pattern Auction", Description = "Enable pattern auction system", Order = 19, GroupName = "Pattern Auction")]
        public bool EnablePatternAuction { get; set; } = true;

        [Display(Name = "Performance Decay Rate", Description = "How quickly pattern performance decays (0-1)", Order = 20, GroupName = "Pattern Auction")]
        [Range(0, 1)]
        public double PerformanceDecayRate { get; set; } = 0.1;

        [Display(Name = "Minimum Pattern Trades", Description = "Minimum number of trades before pattern performance is considered reliable", Order = 21, GroupName = "Pattern Auction")]
        [Range(1, 100)]
        public int MinimumPatternTrades { get; set; } = 5;

        // Default constructor
        public CurvesV2Config()
        {
        }

        // Get base API endpoint based on settings
        public string GetBaseApiEndpoint()
        {
            if (UseRemoteServer)
            {
                return RemoteServerUrl;
            }
            else
            {
                return $"{ServerUrl}";
            }
        }
        
        // Get main API endpoint (UI server)
        public string GetMainApiEndpoint()
        {
            string baseUrl = GetBaseApiEndpoint();
            return $"{baseUrl}:{MainServerPort}";
        }
        
        // Get signal API endpoint
        public string GetSignalApiEndpoint()
        {
            string baseUrl = GetBaseApiEndpoint();
            return $"{baseUrl}:{SignalServerPort}";
        }
        
        // Get SignalPool API endpoint
        public string GetSignalPoolApiEndpoint()
        {
            string baseUrl = GetBaseApiEndpoint();
            return $"{baseUrl}:{SignalPoolPort}";
        }
        
        // Get WebSocket endpoint - primary communication method
        public string GetWebSocketEndpoint()
        {
            string baseUrl = GetBaseApiEndpoint().Replace("http://", "ws://").Replace("https://", "wss://");
            return $"{baseUrl}:{SignalServerPort}/ws";
        }
        
        // Restore HTTP endpoints for backward compatibility
        // Get signal endpoint for a specific instrument
        public string GetSignalEndpoint(string instrument)
        {
            return $"{GetSignalApiEndpoint()}/api/signals/{instrument}";
        }
        
        // Get realtime bars endpoint for a specific instrument
        public string GetBarDataEndpoint(string instrument)
        {
            return $"{GetSignalApiEndpoint()}/api/realtime_bars/{instrument}";
        }
        
        // Get trade result endpoint for a specific instrument
        public string GetTradeResultEndpoint(string instrument)
        {
            return $"{GetSignalApiEndpoint()}/api/signals/{instrument}/trade_results";
        }

        // Fix GetApiEndpoint for backward compatibility
        public string GetApiEndpoint()
        {
            return GetSignalApiEndpoint();
        }

        // Override ToString for display in property grids
        public override string ToString()
        {
            return $"CurvesV2 ({(UseRemoteServer ? "Remote" : "Local")}) [{(UseWebSocketOnly ? "WebSocket Only" : "Mixed Mode")}]";
        }
    }
} 