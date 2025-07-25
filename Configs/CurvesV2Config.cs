using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    // Enum for backtest modes
    public enum BacktestModes
    {
        LiveTrading,
        BacktestWithReset,
        BacktestWithPersistentLearning,
        BacktestIsolated
    }
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

        // Risk Agent and Anti-Overfitting Settings
        [Display(Name = "Enable Risk Agent", Description = "Enable Risk Agent for agentic memory and risk management", Order = 22, GroupName = "Risk Agent")]
        public bool EnableRiskAgent { get; set; } = true;

        [Display(Name = "Risk Agent Port", Description = "Risk Agent service port (default: 3017)", Order = 23, GroupName = "Risk Agent")]
        [Range(3000, 9999)]
        public int RiskAgentPort { get; set; } = 3017;

        [Display(Name = "Enable Anti-Overfitting", Description = "Enable anti-overfitting protection for pattern analysis", Order = 24, GroupName = "Risk Agent")]
        public bool EnableAntiOverfitting { get; set; } = true;

        [Display(Name = "Diminishing Factor", Description = "Confidence reduction factor per pattern exposure (0.5-0.99)", Order = 25, GroupName = "Risk Agent")]
        [Range(0.5, 0.99)]
        public double DiminishingFactor { get; set; } = 0.8;

        [Display(Name = "Max Pattern Exposure", Description = "Maximum times a pattern can be used before heavy penalty", Order = 26, GroupName = "Risk Agent")]
        [Range(1, 20)]
        public int MaxPatternExposure { get; set; } = 5;

        [Display(Name = "Time Window Minutes", Description = "Time window for pattern clustering detection (minutes)", Order = 27, GroupName = "Risk Agent")]
        [Range(15, 240)]
        public int TimeWindowMinutes { get; set; } = 60;

        [Display(Name = "Backtest Mode", Description = "Current backtest mode for anti-overfitting", Order = 28, GroupName = "Risk Agent")]
        public BacktestModes BacktestMode { get; set; }

        [Display(Name = "Reset Learning on Backtest", Description = "Reset pattern exposure when starting new backtests", Order = 29, GroupName = "Risk Agent")]
        public bool ResetLearningOnBacktest { get; set; } = true;

        [Display(Name = "Risk Agent Timeout (ms)", Description = "Timeout for Risk Agent requests in milliseconds", Order = 30, GroupName = "Risk Agent")]
        [Range(100, 10000)]
        public int RiskAgentTimeoutMs { get; set; } = 2000;

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

        // Get Risk Agent API endpoint
        public string GetRiskAgentEndpoint()
        {
            string baseUrl = GetBaseApiEndpoint();
            return $"{baseUrl}:{RiskAgentPort}";
        }

        // Get Risk Agent evaluate risk endpoint
        public string GetRiskEvaluationEndpoint()
        {
            return $"{GetRiskAgentEndpoint()}/api/evaluate-risk";
        }

        // Get Risk Agent anti-overfitting configuration endpoint
        public string GetAntiOverfittingConfigEndpoint()
        {
            return $"{GetRiskAgentEndpoint()}/api/anti-overfitting/configure";
        }

        // Get Risk Agent backtest control endpoints
        public string GetBacktestStartEndpoint()
        {
            return $"{GetRiskAgentEndpoint()}/api/backtest/start";
        }

        public string GetBacktestEndEndpoint()
        {
            return $"{GetRiskAgentEndpoint()}/api/backtest/end";
        }

        // Fix GetApiEndpoint for backward compatibility
        public string GetApiEndpoint()
        {
            return GetSignalApiEndpoint();
        }

        // Override ToString for display in property grids
        public override string ToString()
        {
            string riskAgentStatus = EnableRiskAgent ? (EnableAntiOverfitting ? "Risk+Anti-Overfitting" : "Risk Only") : "No Risk Agent";
            return $"CurvesV2 ({(UseRemoteServer ? "Remote" : "Local")}) [{(UseWebSocketOnly ? "WebSocket Only" : "Mixed Mode")}] - {riskAgentStatus}";
        }
    }
} 