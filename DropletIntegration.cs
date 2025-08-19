#region Using declarations
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// Optional droplet integration class for sending trade data to user's VPS
    /// Clean 3-step flow: compose -> append custom -> send
    /// </summary>
    public class DropletIntegration
    {
        private readonly DropletService dropletService;
        private readonly Action<string> logger;

        public DropletIntegration(DropletService service, Action<string> logAction = null)
        {
            dropletService = service;
            logger = logAction ?? ((msg) => { });
        }

        /// <summary>
        /// Step 1: Compose core droplet payload from trade outcome
        /// </summary>
        /// <param name="outcomeData">Core trade outcome data</param>
        /// <param name="instrument">Trading instrument</param>
        /// <param name="direction">Trade direction</param>
        /// <param name="entryType">Entry signal type</param>
        /// <param name="sessionId">Trading session ID</param>
        /// <returns>Base payload dictionary</returns>
        public Dictionary<string, object> ComposeDropletPayload(
            TradeOutcomeData outcomeData, 
            string instrument, 
            string direction, 
            string entryType,
            string sessionId = null)
        {
            var payload = new Dictionary<string, object>
            {
                // Core standardized fields
                ["instrument"] = instrument,
                ["direction"] = direction,
                ["entryType"] = entryType,
                ["sessionId"] = sessionId, // Add session ID
                ["exitPrice"] = outcomeData.ExitPrice,
                ["pnlDollars"] = outcomeData.PnLDollars,
                ["pnlPoints"] = outcomeData.PnLPoints,
                ["holdingBars"] = outcomeData.HoldingBars,
                ["exitReason"] = outcomeData.ExitReason,
                ["entryTime"] = outcomeData.EntryTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["exitTime"] = outcomeData.ExitTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["quantity"] = outcomeData.Quantity,
                
                // Trade efficiency metrics
                ["entryEfficiency"] = outcomeData.EntryEfficiency,
                ["exitEfficiency"] = outcomeData.ExitEfficiency,
                ["totalEfficiency"] = outcomeData.TotalEfficiency,
                ["maxAdverseExcursion"] = outcomeData.MaxAdverseExcursion,
                ["maxFavorableExcursion"] = outcomeData.MaxFavorableExcursion,
                ["netProfitPercent"] = outcomeData.NetProfitPercent,
                
                // Session context
                ["cumulativeProfit"] = outcomeData.CumulativeProfit,
                ["tradeNumber"] = outcomeData.TradeNumber,
                ["currentDrawdown"] = outcomeData.CurrentDrawdown,
                ["consecutiveWins"] = outcomeData.ConsecutiveWins,
                ["consecutiveLosses"] = outcomeData.ConsecutiveLosses,
                
                // Risk management
                ["entryPrice"] = outcomeData.EntryPrice,
                ["maxProfit"] = outcomeData.MaxProfit,
                ["maxLoss"] = outcomeData.MaxLoss,
                ["stopLoss"] = outcomeData.StopLoss,
                ["takeProfit"] = outcomeData.TakeProfit,
                ["riskRewardRatio"] = outcomeData.RiskRewardRatio,
                
                // Pattern data
                ["patternType"] = outcomeData.PatternType,
                ["patternConfidence"] = outcomeData.PatternConfidence,
                ["entrySignalType"] = outcomeData.EntrySignalType,
                
                // Trade correlation analysis fields
                ["EntryHour"] = outcomeData.EntryHour,
                ["EntryMinute"] = outcomeData.EntryMinute,
                ["DayOfWeek"] = outcomeData.DayOfWeek,
                ["TradeDurationMinutes"] = outcomeData.TradeDurationMinutes,
                ["EntryVolume"] = outcomeData.EntryVolume,
                ["AtrAtEntry"] = outcomeData.AtrAtEntry,
                ["VwapDistance"] = outcomeData.VwapDistance,
                ["EmaDistance"] = outcomeData.EmaDistance,
                ["DailyTradeSequence"] = outcomeData.DailyTradeSequence,
                ["PreviousTradeResult"] = outcomeData.PreviousTradeResult,
                
                // Optional trajectory
                ["profitByBar"] = outcomeData.ProfitByBar,
                
                // Metadata
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Log($"Composed droplet payload: {instrument} {direction} {entryType} P&L: ${outcomeData.PnLDollars:F2} Hour:{outcomeData.EntryHour} Vol:{outcomeData.EntryVolume:F0} ATR:{outcomeData.AtrAtEntry:F2} SessionID: {sessionId ?? "NULL"}");
            return payload;
        }

        /// <summary>
        /// Step 2: Append custom payload data
        /// </summary>
        /// <param name="basePayload">Base payload from ComposeDropletPayload</param>
        /// <param name="customData">Custom data to append</param>
        /// <param name="customPrefix">Optional prefix for custom fields (default: "custom_")</param>
        /// <returns>Enhanced payload with custom data</returns>
        public Dictionary<string, object> AppendCustomPayload(
            Dictionary<string, object> basePayload, 
            Dictionary<string, object> customData, 
            string customPrefix = "custom_")
        {
            if (customData == null || customData.Count == 0)
            {
                return basePayload;
            }

            var enhancedPayload = new Dictionary<string, object>(basePayload);
            
            foreach (var kvp in customData)
            {
                string fieldName = string.IsNullOrEmpty(customPrefix) ? kvp.Key : $"{customPrefix}{kvp.Key}";
                enhancedPayload[fieldName] = kvp.Value;
            }

            Log($"Appended {customData.Count} custom fields to payload");
            return enhancedPayload;
        }

        /// <summary>
        /// Step 3: Send payload to droplet
        /// </summary>
        /// <param name="payload">Complete payload to send</param>
        /// <param name="adapterType">Adapter type for droplet processing</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendPayloadToDroplet(Dictionary<string, object> payload, string adapterType = "ninjatrader_trade")
        {
            if (dropletService == null)
            {
                Log("DropletService not available - cannot send payload");
                return false;
            }

            try
            {
                bool success = await dropletService.SendCustomOutcome(payload, adapterType);
                Log($"Sent payload to droplet - Success: {success}, Fields: {payload.Count}, Adapter: {adapterType}");
                return success;
            }
            catch (Exception ex)
            {
                Log($"Error sending payload to droplet: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send trade data from NinjaTrader OrderRecordMasterLite - handles all data extraction internally
        /// </summary>
        /// <param name="orderRecordMaster">Complete order record with all trade details</param>
        /// <param name="executionOrder">Exit execution order</param>
        /// <param name="profit">Calculated profit/loss</param>
        /// <param name="pnlPoints">P&L in points</param>
        /// <param name="currentBar">Current bar number for holding period</param>
        /// <param name="dailyProfit">Session cumulative profit</param>
        /// <param name="dailyProfitATH">Session peak profit</param>
        /// <param name="allTradesCount">Total trade count</param>
        /// <param name="consecutiveWins">Current consecutive wins</param>
        /// <param name="consecutiveLosses">Current consecutive losses</param>
        /// <param name="customData">Optional additional custom data</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendTradeFromOrderRecord(
            OrderRecordMasterLite orderRecordMaster,
            Order executionOrder,
            double profit,
            double pnlPoints,
            int currentBar,
            double dailyProfit,
            double dailyProfitATH,
            int allTradesCount,
            int consecutiveWins,
            int consecutiveLosses,
            MainStrategy strategy = null,
            Dictionary<string, object> customData = null)
        {
            try
            {
                // Extract core trade information
                string instrument = executionOrder.Instrument.FullName.Split(' ')[0];
                string direction = executionOrder.IsLong ? "long" : "short";
                string entryType = "strategy_trade"; // Use generic entry type since patternSubtype is deprecated
                
                // Build comprehensive trade outcome data
                var tradeOutcomeData = new TradeOutcomeData
                {
                    // Core trade results
                    ExitPrice = executionOrder.AverageFillPrice,
                    PnLDollars = profit,
                    PnLPoints = pnlPoints,
                    HoldingBars = currentBar - orderRecordMaster.EntryBar,
                    ExitReason = orderRecordMaster.OrderSupplementals?.thisSignalExitAction.ToString() ?? "unknown",
                    Instrument = instrument,
                    ExitOrderUUID = orderRecordMaster.ExitOrderUUID ?? executionOrder.Name,
                    EntryTime = orderRecordMaster.EntryTime,
                    ExitTime = orderRecordMaster.ExitTime, // Current exit time
                    
                    // Risk management data
                    EntryPrice = orderRecordMaster.PriceStats?.OrderStatsEntryPrice ?? 0,
                    Quantity = executionOrder.Quantity,
                    MaxProfit = orderRecordMaster.PriceStats?.OrderStatsAllTimeHighProfit ?? 0,
                    MaxLoss = orderRecordMaster.PriceStats?.OrderStatsAllTimeLowProfit ?? 0,
                    StopLoss = orderRecordMaster.PriceStats?.OrderMaxLoss ?? 0,
                    TakeProfit = orderRecordMaster.PriceStats?.OrderStatsHardProfitTarget ?? 0,
                    RiskRewardRatio = CalculateRiskRewardRatio(
                        orderRecordMaster.PriceStats?.OrderStatsEntryPrice ?? 0,
                        orderRecordMaster.PriceStats?.OrderStatsHardProfitTarget ?? 0,
                        orderRecordMaster.PriceStats?.OrderMaxLoss ?? 0,
                        direction),
                    
                    // Pattern information
                    PatternType = entryType,
                    PatternConfidence = orderRecordMaster.OrderSupplementals?.divergence ?? 0,
                    EntrySignalType = orderRecordMaster.OrderSupplementals.relaySignalType ?? "Unknown",
                    ProfitByBar = orderRecordMaster.PriceStats?.profitByBar,
                    
                    // Session context metrics
                    CumulativeProfit = dailyProfit,
                    TradeNumber = allTradesCount,
                    CurrentDrawdown = dailyProfitATH - dailyProfit,
                    ConsecutiveWins = consecutiveWins,
                    ConsecutiveLosses = consecutiveLosses,
                    
                    // Trade correlation analysis fields
                    EntryHour = orderRecordMaster.EntryTime.Hour,
                    EntryMinute = orderRecordMaster.EntryTime.Minute,
                    DayOfWeek = orderRecordMaster.EntryTime.DayOfWeek.ToString(),
                    TradeDurationMinutes = (orderRecordMaster.ExitTime - orderRecordMaster.EntryTime).TotalMinutes,
                    EntryVolume = strategy?.Volume?[0] ?? 0,
                    AtrAtEntry = strategy?.ATR(14)?[0] ?? 0,
                    VwapDistance = CalculateVwapDistance(strategy),
                    EmaDistance = CalculateEmaDistance(strategy),
                    DailyTradeSequence = strategy?.GetDailyTradeCount() ?? allTradesCount,
                    PreviousTradeResult = strategy?.GetLastTradeResult() ?? "NONE",
                    
                    // Trade efficiency metrics (can be calculated if needed)
                    EntryEfficiency = CalculateEntryEfficiency(orderRecordMaster),
                    ExitEfficiency = CalculateExitEfficiency(orderRecordMaster, profit),
                    TotalEfficiency = CalculateTotalEfficiency(orderRecordMaster, profit),
                    MaxAdverseExcursion = Math.Abs(orderRecordMaster.PriceStats?.OrderStatsAllTimeLowProfit ?? 0),
                    MaxFavorableExcursion = Math.Abs(orderRecordMaster.PriceStats?.OrderStatsAllTimeHighProfit ?? 0),
                    NetProfitPercent = CalculateNetProfitPercent(orderRecordMaster, profit)
					
			
                };
                
                // Step 1-3: Complete droplet flow  
                // Note: sessionId not available in this context, will be null
                bool success = await SendTradeToDroplet(tradeOutcomeData, instrument, direction, entryType, orderRecordMaster.OrderSupplementals.SessionID , customData: customData);
                
				Log($"Trade from OrderRecord sent: {instrument} {direction} stop {tradeOutcomeData.StopLoss} take {tradeOutcomeData.TakeProfit} {entryType} ${profit:F2} - Success: {success}");
                return success;
            }
            catch (Exception ex)
            {
                Log($"Error sending trade from OrderRecord: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Convenience method: Complete 3-step flow in one call
        /// </summary>
        /// <param name="outcomeData">Trade outcome data</param>
        /// <param name="instrument">Trading instrument</param>
        /// <param name="direction">Trade direction</param>
        /// <param name="entryType">Entry signal type</param>
        /// <param name="customData">Optional custom data</param>
        /// <param name="customPrefix">Optional prefix for custom fields</param>
        /// <param name="adapterType">Adapter type for droplet processing</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendTradeToDroplet(
            TradeOutcomeData outcomeData,
            string instrument,
            string direction, 
            string entryType,
            string sessionId = null,
            Dictionary<string, object> customData = null,
            string customPrefix = "custom_",
            string adapterType = "ninjatrader_trade")
        {
            // Step 1: Compose
            var basePayload = ComposeDropletPayload(outcomeData, instrument, direction, entryType, sessionId);
            
            // Step 2: Append custom
            var finalPayload = AppendCustomPayload(basePayload, customData, customPrefix);
            
            // Step 3: Send
            return await SendPayloadToDroplet(finalPayload, adapterType);
        }

        private void Log(string message)
        {
            logger?.Invoke($"[CLOUD-RELAY-INTEGRATION] {DateTime.Now:HH:mm:ss} {message}");
        }

        private double CalculateRiskRewardRatio(double entryPrice, double targetPrice, double stopPrice, string direction)
        {
            if (entryPrice <= 0 || targetPrice <= 0 || stopPrice <= 0) return 0.0;
            
            double reward, risk;
            
            if (direction.ToLower() == "long")
            {
                reward = Math.Abs(targetPrice - entryPrice);
                risk = Math.Abs(entryPrice - stopPrice);
            }
            else // short
            {
                reward = Math.Abs(entryPrice - targetPrice);
                risk = Math.Abs(stopPrice - entryPrice);
            }
            
            return risk > 0 ? reward / risk : 0.0;
        }

        private double CalculateEntryEfficiency(OrderRecordMasterLite orderRecord)
        {
            // Entry efficiency: how quickly price moved in favorable direction after entry
            var maxFavorable = Math.Abs(orderRecord.PriceStats?.OrderStatsAllTimeHighProfit ?? 0);
            var entryPrice = orderRecord.PriceStats?.OrderStatsEntryPrice ?? 0;
            
            if (entryPrice <= 0 || maxFavorable <= 0) return 0.0;
            
            // Calculate efficiency as percentage of immediate favorable movement
            return Math.Min(maxFavorable / (entryPrice * 0.01), 100.0); // Cap at 100%
        }

        private double CalculateExitEfficiency(OrderRecordMasterLite orderRecord, double finalProfit)
        {
            // Exit efficiency: how much of max profit was captured
            var maxProfit = Math.Abs(orderRecord.PriceStats?.OrderStatsAllTimeHighProfit ?? 0);
            
            if (maxProfit <= 0) return finalProfit >= 0 ? 100.0 : 0.0;
            
            return Math.Max(0, (finalProfit / maxProfit) * 100.0);
        }

        private double CalculateTotalEfficiency(OrderRecordMasterLite orderRecord, double finalProfit)
        {
            // Total efficiency: combination of entry and exit efficiency
            var entryEff = CalculateEntryEfficiency(orderRecord);
            var exitEff = CalculateExitEfficiency(orderRecord, finalProfit);
            
            return (entryEff + exitEff) / 2.0;
        }

        private double CalculateNetProfitPercent(OrderRecordMasterLite orderRecord, double finalProfit)
        {
            var entryPrice = orderRecord.PriceStats?.OrderStatsEntryPrice ?? 0;
            if (entryPrice <= 0) return 0.0;
            
            return (finalProfit / entryPrice) * 100.0;
        }

        /// <summary>
        /// Calculate distance from VWAP at entry time in ticks/points
        /// </summary>
        private double CalculateVwapDistance(MainStrategy strategy)
        {
            if (strategy == null) return 0.0;
            
            try
            {
                var currentPrice = strategy.Close?[0] ?? 0;
                // Use VWAP() with default parameters - no resolution parameter needed
                var vwapValue = strategy.custom_VWAP(15)?[0] ?? currentPrice;
                return currentPrice - vwapValue;
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Calculate distance from EMA(21) at entry time in ticks/points
        /// </summary>
        private double CalculateEmaDistance(MainStrategy strategy)
        {
            if (strategy == null) return 0.0;
            
            try
            {
                var currentPrice = strategy.Close?[0] ?? 0;
                var emaValue = strategy.EMA(21)?[0] ?? currentPrice;
                return currentPrice - emaValue;
            }
            catch (Exception)
            {
                return 0.0;
            }
        }
    }
}