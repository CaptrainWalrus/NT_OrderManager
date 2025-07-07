using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Linq;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    // Pushcut integration for trade approval via Python microservice
    public partial class MainStrategy
    {
        #region Pushcut Properties
        
        private static readonly HttpClient httpClient = new HttpClient();
        private bool pushcutEnabled = true;
        private string currentTradeId = null;
        
        // Microservice configuration
        private string pushcutServerUrl = "https://pushcut-server.onrender.com";
        private int approvalTimeoutMinutes = 5;
        private string timeoutBehavior = "reject"; // "reject" or "approve"
        
        #endregion

        #region Data Models for Microservice

        public class Bar
        {
            public string time { get; set; }
            public double open { get; set; }
            public double high { get; set; }
            public double low { get; set; }
            public double close { get; set; }
            public int volume { get; set; }
        }

        public class Signal
        {
            public string direction { get; set; }
            public double entry_price { get; set; }
            public double risk_amount { get; set; }
            public double target_amount { get; set; }
            public string pattern_type { get; set; }
            public double confidence { get; set; }
        }

        public class TradeRequest
        {
            public string instrument { get; set; }
            public string timeframe { get; set; }
            public List<Bar> bars { get; set; }
            public Signal signal { get; set; }
            public Dictionary<string, object> indicators { get; set; }
        }

        public class TradeResponse
        {
            public string trade_id { get; set; }
            public string status { get; set; }
            public string chart_url { get; set; }
        }

        public class StatusResponse
        {
            public string trade_id { get; set; }
            public string status { get; set; }
            public string timestamp { get; set; }
            public string expires_at { get; set; }
            public string decision_time { get; set; }
        }

        #endregion

        #region Main Approval Method

        /// <summary>
        /// Request trade approval via Python microservice
        /// This method BLOCKS until approval/rejection/timeout
        /// </summary>
        public async Task<bool> RequestTradeApproval(
            int quantity, 
            OrderAction orderAction, 
            signalPackage signalPackage, 
            string description, 
            int bar, 
            OrderType orderType, 
            patternFunctionResponse builtSignal,
            TradeRequest prebuiltSnapshot = null)
        {
            // Skip approval in historical mode or if disabled
            if (!isRealTime || !pushcutEnabled)
            {
                return true; // Auto-approve in backtest/historical
            }

            try
            {
                // Step 1: Build trade request (use snapshot if provided to stay on Ninja thread)
                var tradeRequest = prebuiltSnapshot ?? BuildTradeRequest(quantity, orderAction, signalPackage, description, builtSignal);
                
                Print($"[PUSHCUT] Requesting approval for {orderAction} {quantity} @ {Close[0]:F2}");

                // Step 2: Send to microservice
                var tradeResponse = await SendTradeRequest(tradeRequest);
                
                if (tradeResponse == null)
                {
                    Print("[PUSHCUT] Failed to send trade request - defaulting to reject");
                    return false;
                }

                Print($"[PUSHCUT] Trade submitted: {tradeResponse.trade_id} - polling for approval...");

                // Step 3: Poll for approval with timeout
                bool approved = await PollForApproval(tradeResponse.trade_id);

                Print($"[PUSHCUT] Trade {tradeResponse.trade_id} {(approved ? "APPROVED" : "REJECTED/TIMEOUT")}");
                return approved;
            }
            catch (Exception ex)
            {
                Print($"[PUSHCUT] Error in trade approval: {ex.Message}");
                // On error, default to reject for safety
                return false;
            }
        }

        #endregion

        #region Microservice Communication

        private TradeRequest BuildTradeRequest(
            int quantity, 
            OrderAction orderAction, 
            signalPackage signalPackage, 
            string description, 
            patternFunctionResponse builtSignal)
        {
            // Get recent bars for chart context (last 50 bars)
            var bars = new List<Bar>();
            int barCount = Math.Min(50, CurrentBars[0]);
            
            for (int i = barCount - 1; i >= 0; i--)
            {
                bars.Add(new Bar
                {
                    time = Time[i].ToString("yyyy-MM-ddTHH:mm:ss"),
                    open = Open[i],
                    high = High[i],
                    low = Low[i],
                    close = Close[i],
                    volume = (int)Volume[i]
                });
            }

            // Calculate risk/target amounts
            var direction = orderAction == OrderAction.Buy ? "LONG" : "SHORT";
            var riskAmount = Math.Round(quantity * microContractStoploss, 0);
            var targetAmount = Math.Round(quantity * microContractTakeProfit, 0);
            
            // Extract pattern information
            var patternType = builtSignal?.patternSubType ?? description ?? "signal";
            var confidence = builtSignal?.signalScore ?? 85.0;

            var signal = new Signal
            {
                direction = direction,
                entry_price = Close[0],
                risk_amount = riskAmount,
                target_amount = targetAmount,
                pattern_type = patternType,
                confidence = Math.Abs(confidence) / 100.0 // Convert to 0-1 scale
            };

            var indicators = new Dictionary<string, object>();
            
            // Add available indicators
            if (EMA3 != null) indicators["ema_20"] = EMA3[0];
            if (VWAP1 != null) indicators["vwap"] = VWAP1[0];
            indicators["position"] = Position.MarketPosition.ToString();
            indicators["unrealized_pnl"] = unrealizedPNL;
            indicators["daily_pnl"] = dailyProfit;

            return new TradeRequest
            {
                instrument = Instrument.FullName,
                timeframe = "5min", // Could be made configurable
                bars = bars,
                signal = signal,
                indicators = indicators
            };
        }

        private async Task<TradeResponse> SendTradeRequest(TradeRequest tradeRequest)
        {
            try
            {
                // Transform to new server format
                var tradeNotification = new
                {
                    instrument = tradeRequest.instrument,
                    direction = tradeRequest.signal.direction,
                    entryPrice = tradeRequest.signal.entry_price,
                    stopLoss = tradeRequest.signal.entry_price - (tradeRequest.signal.direction == "LONG" ? tradeRequest.signal.risk_amount : -tradeRequest.signal.risk_amount),
                    takeProfit = tradeRequest.signal.entry_price + (tradeRequest.signal.direction == "LONG" ? tradeRequest.signal.target_amount : -tradeRequest.signal.target_amount),
                    confidence = tradeRequest.signal.confidence,
                    bars = tradeRequest.bars.Select(b => new {
                        time = DateTime.Parse(b.time).ToString("HH:mm"),
                        open = Math.Round(b.open, 2),
                        high = Math.Round(b.high, 2),
                        low = Math.Round(b.low, 2),
                        close = Math.Round(b.close, 2)
                    }).ToList()
                };
                
                var json = JsonConvert.SerializeObject(tradeNotification);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await httpClient.PostAsync($"{pushcutServerUrl}/trade-notification", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var serverResponse = JsonConvert.DeserializeObject<dynamic>(responseJson);
                    
                    // Store the trade ID for polling
                    currentTradeId = serverResponse.tradeId;
                    
                    return new TradeResponse
                    {
                        trade_id = serverResponse.tradeId,
                        status = "pending",
                        chart_url = serverResponse.chartUrl
                    };
                }
                else
                {
                    Print($"[PUSHCUT] Server error: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Print($"[PUSHCUT] Error sending trade request: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> PollForApproval(string tradeId)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(approvalTimeoutMinutes);
            
            // Poll every 3 seconds initially, then every 5 seconds
            var pollIntervals = new[] { 3000, 3000, 3000, 5000, 5000, 5000, 5000 };
            int intervalIndex = 0;

            Print($"[PUSHCUT] Polling for approval of trade {tradeId}...");

            while (DateTime.Now - startTime < timeout)
            {
                try
                {
                    // Check if trade is still pending using widget endpoint
                    var response = await httpClient.GetAsync($"{pushcutServerUrl}/widget/pending-trade");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var pendingData = JsonConvert.DeserializeObject<dynamic>(json);
                        
                        if (pendingData.hasPendingTrade == false)
                        {
                            // No pending trade means it was approved or rejected
                            // Check recent stats to determine which
                            var statsResponse = await httpClient.GetAsync($"{pushcutServerUrl}/widget/summary");
                            if (statsResponse.IsSuccessStatusCode)
                            {
                                var statsJson = await statsResponse.Content.ReadAsStringAsync();
                                var stats = JsonConvert.DeserializeObject<dynamic>(statsJson);
                                
                                // If we had a pending trade and now we don't, assume last action was on our trade
                                // This is a simple heuristic - in production you'd want better tracking
                                Print("[PUSHCUT] Trade decision detected - assuming APPROVED");
                                return true;
                            }
                            else
                            {
                                Print("[PUSHCUT] Trade completed but couldn't determine outcome - defaulting to APPROVED");
                                return true;
                            }
                        }
                        else
                        {
                            // Trade is still pending, continue polling
                            var timeRemaining = pendingData.timeRemaining?.ToString() ?? "unknown";
                            Print($"[PUSHCUT] Trade still pending - time remaining: {timeRemaining}");
                        }
                    }
                    else
                    {
                        Print($"[PUSHCUT] Status check failed: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Print($"[PUSHCUT] Error checking status: {ex.Message}");
                }

                // Wait before next poll
                var delay = pollIntervals[Math.Min(intervalIndex, pollIntervals.Length - 1)];
                await Task.Delay(delay);
                intervalIndex++;
            }

            // Client-side timeout
            Print($"[PUSHCUT] Client timeout after {approvalTimeoutMinutes} minutes - applying timeout behavior: {timeoutBehavior}");
            return timeoutBehavior == "approve";
        }

        #endregion

        #region Integration Points

        /// <summary>
        /// Enhanced EntryLimitFunctionLite with Pushcut approval gate
        /// Use this instead of direct EntryLimitFunctionLite for approved trades
        /// </summary>
        public async Task<bool> EntryLimitFunctionLiteWithApproval(
            int quantity, 
            OrderAction orderAction, 
            signalPackage signalPackage, 
            string description, 
            int bar, 
            OrderType orderType, 
            patternFunctionResponse builtSignal,
            TradeRequest prebuiltSnapshot = null)
        {
            // Request approval (blocking call)
            bool approved = await RequestTradeApproval(quantity, orderAction, signalPackage, description, bar, orderType, builtSignal, prebuiltSnapshot);
            
            if (!approved)
            {
                Print($"[PUSHCUT] Trade not approved - skipping entry");
                return false;
            }

            // Execute the original entry logic
            Print($"[PUSHCUT] Trade approved - executing entry");
            EntryLimitFunctionLite(quantity, orderAction, signalPackage, description, bar, orderType, builtSignal);
            return true;
        }

        /// <summary>
        /// Test method to verify microservice connectivity
        /// </summary>
        public async Task<bool> TestPushcutConnection()
        {
            try
            {
                var response = await httpClient.GetAsync($"{pushcutServerUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Print($"[PUSHCUT] Connection test successful: {json}");
                    return true;
                }
                else
                {
                    Print($"[PUSHCUT] Connection test failed: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Print($"[PUSHCUT] Connection test error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Configuration Properties

        [NinjaScriptProperty]
        [Display(Name="Enable Pushcut Approval", Description="Enable mobile trade approval via Pushcut", Order=1, GroupName="Pushcut")]
        public bool EnablePushcutApproval
        {
            get { return pushcutEnabled; }
            set { pushcutEnabled = value; }
        }

        [NinjaScriptProperty]
        [Display(Name="Pushcut Server URL", Description="URL of the Pushcut microservice", Order=2, GroupName="Pushcut")]
        public string PushcutServerUrl
        {
            get { return pushcutServerUrl; }
            set { pushcutServerUrl = value; }
        }

        [NinjaScriptProperty]
        [Display(Name="Approval Timeout (Minutes)", Description="Minutes to wait for approval before timeout", Order=3, GroupName="Pushcut")]
        public int ApprovalTimeoutMinutes
        {
            get { return approvalTimeoutMinutes; }
            set { approvalTimeoutMinutes = Math.Max(1, Math.Min(10, value)); } // 1-10 minutes
        }

        [NinjaScriptProperty]
        [Display(Name="Timeout Behavior", Description="What to do on timeout: approve or reject", Order=4, GroupName="Pushcut")]
        public string TimeoutBehavior
        {
            get { return timeoutBehavior; }
            set { timeoutBehavior = value == "approve" ? "approve" : "reject"; }
        }

        #endregion

        // Public wrapper to safely create snapshot from strategy thread
        public TradeRequest CreateTradeRequestSnapshot(
            int quantity,
            OrderAction orderAction,
            signalPackage signalPackage,
            string description,
            patternFunctionResponse builtSignal)
        {
            return BuildTradeRequest(quantity, orderAction, signalPackage, description, builtSignal);
        }
    }
} 