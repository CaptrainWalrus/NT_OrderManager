#region Using declarations
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// MainStrategy partial file for custom droplet functions
    /// Handles user-defined custom data processing and specialized formats
    /// </summary>
    public partial class MainStrategy
    {
        /// <summary>
        /// Send custom trade data using flexible variable system
        /// Allows complete flexibility in data structure and processing
        /// </summary>
        /// <param name="customData">Dictionary containing user-defined trade data</param>
        /// <param name="adapterType">Custom adapter type identifier</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendCustomDropletData(Dictionary<string, object> customData, string adapterType = "custom")
        {
            if (dropletService == null)
            {
                Print("[DROPLET-CUSTOM] DropletService not initialized");
                return false;
            }

            try
            {
                Print($"[DROPLET-CUSTOM] Sending custom data with adapter type: {adapterType}");
                Print($"[DROPLET-CUSTOM] Data fields: {string.Join(", ", customData.Keys)}");
                
                // Convert dictionary to variable collection for consistent handling
                var variables = new DropletVariableCollection();
                foreach (var kvp in customData)
                {
                    string dataType = kvp.Value?.GetType()?.Name?.ToLower() ?? "object";
                    variables.AddCustomVariable(kvp.Key, kvp.Value, dataType);
                }
                
                var customPayload = variables.BuildCustomPayload();
                bool success = await dropletService.SendCustomOutcome(customPayload, adapterType);
                
                Print($"[DROPLET-CUSTOM] Sent {variables.GetCustomVariables().Count} custom variables - Success: {success}");
                return success;
            }
            catch (Exception ex)
            {
                Print($"[DROPLET-CUSTOM] Error in SendCustomDropletData: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send custom data using variable collection builder pattern
        /// More structured approach for building custom payloads
        /// </summary>
        /// <param name="variables">Pre-built variable collection</param>
        /// <param name="adapterType">Custom adapter type identifier</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendCustomVariables(DropletVariableCollection variables, string adapterType = "custom_variables")
        {
            if (dropletService == null || variables == null)
            {
                Print("[DROPLET-CUSTOM] Service or variables not available");
                return false;
            }

            try
            {
                var customPayload = variables.BuildCustomPayload();
                bool success = await dropletService.SendCustomOutcome(customPayload, adapterType);
                
                Print($"[DROPLET-CUSTOM] Variable collection sent - Custom: {variables.GetCustomVariables().Count}, Success: {success}");
                return success;
            }
            catch (Exception ex)
            {
                Print($"[DROPLET-CUSTOM] Error in SendCustomVariables: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Build custom variables using builder pattern - more flexible approach
        /// </summary>
        /// <param name="outcomeData">Position outcome data</param>
        /// <param name="orderRecordMaster">Order record with trade details</param>
        /// <returns>Builder for constructing custom payload</returns>
        public DropletPayloadBuilder BuildCustomTradeVariables(PositionOutcomeData outcomeData, OrderRecordMasterLite orderRecordMaster)
        {
            var builder = new DropletPayloadBuilder();
            
            // Add NinjaTrader-specific custom variables
            builder.AddEntryOrderUUID(orderRecordMaster?.EntryOrderUUID)
                   .AddPatternInfo(orderRecordMaster?.OrderSupplementals?.patternSubtype, orderRecordMaster?.OrderSupplementals?.patternId)
                   .AddMaxProfitLoss(orderRecordMaster?.PriceStats?.OrderStatsAllTimeHighProfit ?? 0, orderRecordMaster?.PriceStats?.OrderStatsAllTimeLowProfit ?? 0)
                   .AddDailyMetrics(dailyProfit, 0) // Could track trade number
                   .AddQuantity(outcomeData?.Quantity ?? 1);
            
            // Add more custom NT-specific fields
            builder.AddCustom("exitOrderUUID", orderRecordMaster?.ExitOrderUUID, "string")
                   .AddCustom("entryBar", orderRecordMaster?.EntryBar ?? 0, "int")
                   .AddCustom("currentBar", CurrentBars?[0] ?? 0, "int")
                   .AddCustom("entryPrice", orderRecordMaster?.PriceStats?.OrderStatsEntryPrice ?? 0, "double")
                   .AddCustom("stopLossTarget", orderRecordMaster?.PriceStats?.OrderMaxLoss ?? 0, "double")
                   .AddCustom("takeProfitTarget", orderRecordMaster?.PriceStats?.OrderStatsHardProfitTarget ?? 0, "double")
                   .AddCustom("divergence", orderRecordMaster?.OrderSupplementals?.divergence ?? 0, "double")
                   .AddCustom("signalExitAction", orderRecordMaster?.OrderSupplementals?.thisSignalExitAction.ToString(), "string")
                   .AddCustom("sessionId", curvesService?.sessionID, "string")
                   .AddCustom("isBacktest", IsInStrategyAnalyzer, "bool")
                   .AddCustom("strategyName", Name, "string")
                   .AddCustom("tickSize", TickSize, "double")
                   .AddCustom("pointValue", Instrument?.MasterInstrument?.PointValue ?? 1, "double")
                   .AddCustom("serverTime", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), "string")
                   .AddCustom("barTime", (Time?[0] ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), "string")
                   .AddCustom("epochMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "long");
            
            return builder;
        }

        /// <summary>
        /// Send custom trade outcome with NinjaTrader-specific enhancements
        /// Includes order record details, pricing stats, and strategy-specific metrics
        /// </summary>
        /// <param name="outcomeData">Standard outcome data</param>
        /// <param name="orderRecordMaster">Complete order record</param>
        /// <param name="customFields">Additional user-defined fields</param>
        /// <param name="adapterType">Custom adapter identifier</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendCustomTradeOutcome(PositionOutcomeData outcomeData, OrderRecordMasterLite orderRecordMaster, Dictionary<string, object> customFields = null, string adapterType = "ninjatrader_enhanced")
        {
            if (dropletService == null)
            {
                Print("[DROPLET-CUSTOM] DropletService not initialized for custom trade outcome");
                return false;
            }

            try
            {
                var customData = new Dictionary<string, object>
                {
                    // Core outcome data
                    ["exitPrice"] = outcomeData.ExitPrice,
                    ["pnlDollars"] = outcomeData.PnLDollars,
                    ["pnlPoints"] = outcomeData.PnLPoints,
                    ["holdingBars"] = outcomeData.HoldingBars,
                    ["exitReason"] = outcomeData.ExitReason,
                    ["entryTime"] = outcomeData.EntryTime,
                    ["exitTime"] = outcomeData.ExitTime,

                    // NinjaTrader-specific enhancements
                    ["entryOrderUUID"] = orderRecordMaster?.EntryOrderUUID,
                    ["exitOrderUUID"] = orderRecordMaster?.ExitOrderUUID,
                    ["entryBar"] = orderRecordMaster?.EntryBar ?? 0,
                    ["currentBar"] = CurrentBars?[0] ?? 0,
                    
                    // Price statistics
                    ["entryPrice"] = orderRecordMaster?.PriceStats?.OrderStatsEntryPrice ?? 0,
                    ["maxProfit"] = orderRecordMaster?.PriceStats?.OrderStatsAllTimeHighProfit ?? 0,
                    ["maxLoss"] = orderRecordMaster?.PriceStats?.OrderStatsAllTimeLowProfit ?? 0,
                    ["stopLossTarget"] = orderRecordMaster?.PriceStats?.OrderMaxLoss ?? 0,
                    ["takeProfitTarget"] = orderRecordMaster?.PriceStats?.OrderStatsHardProfitTarget ?? 0,
                    
                    // Signal information
                    ["patternSubtype"] = orderRecordMaster?.OrderSupplementals?.patternSubtype,
                    ["patternId"] = orderRecordMaster?.OrderSupplementals?.patternId,
                    ["divergence"] = orderRecordMaster?.OrderSupplementals?.divergence ?? 0,
                    ["signalExitAction"] = orderRecordMaster?.OrderSupplementals?.thisSignalExitAction.ToString(),
                    
                    // Strategy context
                    ["dailyProfit"] = dailyProfit,
                    ["sessionId"] = curvesService?.sessionID,
                    ["isBacktest"] = IsInStrategyAnalyzer,
                    ["strategyName"] = Name,
                    
                    // Market context
                    ["instrument"] = Instrument?.FullName,
                    ["tickSize"] = TickSize,
                    ["pointValue"] = Instrument?.MasterInstrument?.PointValue ?? 1,
                    
                    // Timing data
                    ["serverTime"] = DateTime.Now,
                    ["barTime"] = Time?[0] ?? DateTime.Now,
                    ["epochMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // Add trajectory data if available
                if (outcomeData.profitByBar != null)
                {
                    customData["profitTrajectory"] = outcomeData.profitByBar;
                }

                // Merge in any additional custom fields
                if (customFields != null)
                {
                    foreach (var kvp in customFields)
                    {
                        customData[kvp.Key] = kvp.Value;
                    }
                }

                Print($"[DROPLET-CUSTOM] Sending enhanced trade outcome with {customData.Count} fields");
                
                return await SendCustomDropletData(customData, adapterType);
            }
            catch (Exception ex)
            {
                Print($"[DROPLET-CUSTOM] Error in SendCustomTradeOutcome: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send custom market data snapshot to droplet
        /// For real-time market analysis, indicator values, and custom metrics
        /// </summary>
        /// <param name="indicatorData">Dictionary of indicator values</param>
        /// <param name="marketData">Current market state</param>
        /// <param name="customMetrics">User-defined metrics</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendCustomMarketSnapshot(Dictionary<string, double> indicatorData = null, Dictionary<string, object> marketData = null, Dictionary<string, object> customMetrics = null)
        {
            if (dropletService == null)
            {
                Print("[DROPLET-CUSTOM] DropletService not initialized for market snapshot");
                return false;
            }

            try
            {
                var snapshotData = new Dictionary<string, object>
                {
                    // Core market data
                    ["instrument"] = Instrument?.FullName,
                    ["currentPrice"] = Close?[0] ?? 0,
                    ["high"] = High?[0] ?? 0,
                    ["low"] = Low?[0] ?? 0,
                    ["open"] = Open?[0] ?? 0,
                    ["volume"] = Volume?[0] ?? 0,
                    ["barTime"] = Time?[0] ?? DateTime.Now,
                    ["currentBar"] = CurrentBars?[0] ?? 0,
                    
                    // Strategy state
                    ["openPositions"] = openOrderTest,
                    ["dailyPnl"] = dailyProfit,
                    ["isMarketHours"] = true, // Could enhance this
                    
                    // Metadata
                    ["snapshotType"] = "market",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // Add indicator data
                if (indicatorData != null)
                {
                    snapshotData["indicators"] = indicatorData;
                }

                // Add market data
                if (marketData != null)
                {
                    foreach (var kvp in marketData)
                    {
                        snapshotData[$"market_{kvp.Key}"] = kvp.Value;
                    }
                }

                // Add custom metrics
                if (customMetrics != null)
                {
                    foreach (var kvp in customMetrics)
                    {
                        snapshotData[$"custom_{kvp.Key}"] = kvp.Value;
                    }
                }

                Print($"[DROPLET-CUSTOM] Sending market snapshot with {snapshotData.Count} fields");
                
                return await SendCustomDropletData(snapshotData, "market_snapshot");
            }
            catch (Exception ex)
            {
                Print($"[DROPLET-CUSTOM] Error in SendCustomMarketSnapshot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send custom session summary to droplet
        /// End-of-session analytics and performance summary
        /// </summary>
        /// <param name="sessionSummary">Dictionary containing session summary data</param>
        /// <returns>Success status</returns>
        public async Task<bool> SendCustomSessionSummary(Dictionary<string, object> sessionSummary = null)
        {
            if (dropletService == null)
            {
                Print("[DROPLET-CUSTOM] DropletService not initialized for session summary");
                return false;
            }

            try
            {
                var summaryData = sessionSummary ?? new Dictionary<string, object>
                {
                    // Performance metrics
                    ["totalPnl"] = dailyProfit,
                    ["longPnl"] = dailyProfit_Long,
                    ["shortPnl"] = dailyProfit_Short,
                    ["maxDrawdown"] = dailyProfitATH - dailyProfit,
                    ["peakProfit"] = dailyProfitATH,
                    
                    // Trade statistics  
                    ["totalTrades"] = 0, // Would need to track this
                    ["openOrders"] = openOrderTest,
                    
                    // Session info
                    ["sessionId"] = curvesService?.sessionID,
                    ["sessionStart"] = Times?[0]?[0] ?? DateTime.Now,
                    ["sessionEnd"] = DateTime.Now,
                    ["instrument"] = Instrument?.FullName,
                    ["strategyName"] = Name,
                    ["isBacktest"] = IsInStrategyAnalyzer,
                    
                    // System info
                    ["finalBar"] = CurrentBars?[0] ?? 0,
                    ["summaryType"] = "session_end",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                Print($"[DROPLET-CUSTOM] Sending session summary - Total P&L: ${dailyProfit:F2}");
                
                return await SendCustomDropletData(summaryData, "session_summary");
            }
            catch (Exception ex)
            {
                Print($"[DROPLET-CUSTOM] Error in SendCustomSessionSummary: {ex.Message}");
                return false;
            }
        }
    }
}