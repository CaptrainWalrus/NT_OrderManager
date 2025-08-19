#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// ProjectX Bridge for BlueSky integration
    /// Handles bracket orders, position monitoring, and emergency exits
    /// </summary>
    public class ProjectXBridge
    {
        private ProjectXApiClient client;
        private Dictionary<string, int> entryUUIDToProjectXOrderId;
        private Dictionary<string, ProjectXOrderSet> activeOrderSets;
        private MainStrategy strategy;

        public ProjectXBridge(MainStrategy strategy)
        {
            this.strategy = strategy;
            client = new ProjectXApiClient();
            entryUUIDToProjectXOrderId = new Dictionary<string, int>();
            activeOrderSets = new Dictionary<string, ProjectXOrderSet>();
        }

        #region Initialization

        /// <summary>
        /// Initialize ProjectX connection and authentication
        /// </summary>
        public async Task<bool> InitializeAsync(string userName, string apiKey, int accountId)
        {
            try
            {
                bool authenticated = await client.AuthenticateAsync(userName, apiKey, accountId);
                
                if (authenticated)
                {
                    strategy.Print("✅ ProjectX Bridge initialized successfully");
                    
                    // Perform startup reconciliation
                    await PerformStartupReconciliation();
                    
                    return true;
                }
                else
                {
                    strategy.Print("❌ ProjectX Bridge initialization failed - authentication error");
                    return false;
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ ProjectX Bridge initialization exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Bracket Order Management

        /// <summary>
        /// Place bracket order with stop and target
        /// </summary>
        public async Task<ProjectXOrderSet> PlaceBracketOrder(
            int quantity, 
            string entryUUID,
            double stopLoss,
            double takeProfit,
            bool isLong,
            string contractId)
        {
            try
            {
                strategy.Print($"🔄 Placing ProjectX bracket order: {entryUUID} {(isLong ? "LONG" : "SHORT")} {quantity}");

                // 1. Place parent market order
                var parentOrder = new ProjectXOrder
                {
                    accountId = strategy.ProjectXAccountId,
                    contractId = contractId,
                    type = 2, // Market
                    side = isLong ? 0 : 1, // 0=Bid (Buy), 1=Ask (Sell)
                    size = quantity,
                    customTag = entryUUID
                };

                var parentResponse = await client.PlaceOrderAsync(parentOrder);
                
                if (!parentResponse.success)
                {
                    strategy.Print($"❌ ProjectX parent order failed: {parentResponse.errorMessage}");
                    throw new Exception($"Parent order failed: {parentResponse.errorMessage}");
                }

                // 2. Place stop loss order (linked to parent)
                var stopOrder = new ProjectXOrder
                {
                    accountId = strategy.ProjectXAccountId,
                    contractId = contractId,
                    type = 4, // Stop
                    side = isLong ? 1 : 0, // Opposite of entry (1=Ask to sell long, 0=Bid to buy short)
                    size = quantity,
                    stopPrice = (decimal)stopLoss,
                    linkedOrderId = parentResponse.orderId,
                    customTag = $"{entryUUID}_STOP"
                };

                var stopResponse = await client.PlaceOrderAsync(stopOrder);
                
                if (!stopResponse.success)
                {
                    strategy.Print($"⚠️ ProjectX stop order failed: {stopResponse.errorMessage}");
                    // Continue without stop - will be handled by monitoring
                }

                // 3. Place take profit order (linked to parent)
                var targetOrder = new ProjectXOrder
                {
                    accountId = strategy.ProjectXAccountId,
                    contractId = contractId,
                    type = 1, // Limit
                    side = isLong ? 1 : 0, // Opposite of entry (1=Ask to sell long, 0=Bid to buy short)
                    size = quantity,
                    limitPrice = (decimal)takeProfit,
                    linkedOrderId = parentResponse.orderId,
                    customTag = $"{entryUUID}_TARGET"
                };

                var targetResponse = await client.PlaceOrderAsync(targetOrder);
                
                if (!targetResponse.success)
                {
                    strategy.Print($"⚠️ ProjectX target order failed: {targetResponse.errorMessage}");
                    // Continue without target - will be handled by monitoring
                }

                // 4. Create order set
                var orderSet = new ProjectXOrderSet
                {
                    ParentOrderId = parentResponse.orderId,
                    StopOrderId = stopResponse?.success == true ? stopResponse.orderId : 0,
                    TargetOrderId = targetResponse?.success == true ? targetResponse.orderId : 0,
                    EntryUUID = entryUUID,
                    ContractId = contractId,
                    IsLong = isLong
                };

                // 5. Track the order set
                entryUUIDToProjectXOrderId[entryUUID] = (int)parentResponse.orderId;
                activeOrderSets[entryUUID] = orderSet;

                strategy.Print($"✅ ProjectX bracket order placed: Entry={orderSet.ParentOrderId}, Stop={orderSet.StopOrderId}, Target={orderSet.TargetOrderId}");
                
                return orderSet;
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ ProjectX bracket order exception: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Entry long with bracket order
        /// </summary>
        public async Task<bool> ProjectXEnterLong(int quantity, string entryUUID, double stopLoss, double takeProfit, string contractId)
        {
            try
            {
                var orderSet = await PlaceBracketOrder(quantity, entryUUID, stopLoss, takeProfit, true, contractId);
                return orderSet != null;
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ ProjectX enter long failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Entry short with bracket order
        /// </summary>
        public async Task<bool> ProjectXEnterShort(int quantity, string entryUUID, double stopLoss, double takeProfit, string contractId)
        {
            try
            {
                var orderSet = await PlaceBracketOrder(quantity, entryUUID, stopLoss, takeProfit, false, contractId);
                return orderSet != null;
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ ProjectX enter short failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Exit long position
        /// </summary>
        public async Task<bool> ProjectXExitLong(int quantity, string exitUUID, string entryUUID, string contractId)
        {
            return await ExitPosition(quantity, exitUUID, entryUUID, contractId, true);
        }

        /// <summary>
        /// Exit short position
        /// </summary>
        public async Task<bool> ProjectXExitShort(int quantity, string exitUUID, string entryUUID, string contractId)
        {
            return await ExitPosition(quantity, exitUUID, entryUUID, contractId, false);
        }

        /// <summary>
        /// Generic exit position method
        /// </summary>
        private async Task<bool> ExitPosition(int quantity, string exitUUID, string entryUUID, string contractId, bool wasLong)
        {
            try
            {
                strategy.Print($"🔄 ProjectX exit: {exitUUID} for entry {entryUUID}");

                // Cancel any remaining bracket orders first
                if (activeOrderSets.ContainsKey(entryUUID))
                {
                    var orderSet = activeOrderSets[entryUUID];
                    await CancelBracketOrders(orderSet);
                }

                // Place market order to close position
                var exitOrder = new ProjectXOrder
                {
                    accountId = strategy.ProjectXAccountId,
                    contractId = contractId,
                    type = 2, // Market
                    side = wasLong ? 1 : 0, // Opposite of original position
                    size = quantity,
                    customTag = exitUUID
                };

                var exitResponse = await client.PlaceOrderAsync(exitOrder);
                
                if (exitResponse.success)
                {
                    strategy.Print($"✅ ProjectX exit order placed: {exitResponse.orderId}");
                    
                    // Clean up tracking
                    entryUUIDToProjectXOrderId.Remove(entryUUID);
                    activeOrderSets.Remove(entryUUID);
                    
                    return true;
                }
                else
                {
                    strategy.Print($"❌ ProjectX exit failed: {exitResponse.errorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ ProjectX exit exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cancel bracket orders (stop and target)
        /// </summary>
        private async Task CancelBracketOrders(ProjectXOrderSet orderSet)
        {
            try
            {
                if (orderSet.StopOrderId > 0)
                {
                    await client.CancelOrderAsync(orderSet.StopOrderId);
                }
                
                if (orderSet.TargetOrderId > 0)
                {
                    await client.CancelOrderAsync(orderSet.TargetOrderId);
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"⚠️ Error cancelling bracket orders: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Cancel stop order only (preserve target)
        /// </summary>
        public async Task<bool> CancelStopOrder(string entryUUID)
        {
            try
            {
                if (!activeOrderSets.TryGetValue(entryUUID, out var orderSet))
                {
                    strategy.Print($"⚠️ No order set found for {entryUUID}");
                    return false;
                }
                
                if (orderSet.StopOrderId > 0)
                {
                    bool cancelSuccess = await client.CancelOrderAsync(orderSet.StopOrderId);
                    if (cancelSuccess)
                    {
                        strategy.Print($"✅ Stop order cancelled for {entryUUID}");
                        orderSet.StopOrderId = 0; // Clear the stop order ID
                        return true;
                    }
                    else
                    {
                        strategy.Print($"❌ Cancel stop failed for order ID {orderSet.StopOrderId}");
                        return false;
                    }
                }
                
                strategy.Print($"⚠️ No stop order to cancel for {entryUUID}");
                return false;
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ Error cancelling stop: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Place a trailing stop order
        /// </summary>
        public async Task<bool> PlaceTrailingStop(string entryUUID, double stopPrice, int quantity)
        {
            try
            {
                if (!activeOrderSets.TryGetValue(entryUUID, out var orderSet))
                {
                    strategy.Print($"⚠️ No order set found for {entryUUID}");
                    return false;
                }
                
                var stopOrder = new ProjectXOrder
                {
                    accountId = strategy.ProjectXAccountId,
                    contractId = orderSet.ContractId,
                    type = 5, // TrailingStop per API docs
                    side = orderSet.IsLong ? 1 : 0, // Opposite side for stop (sell to exit long, buy to exit short)
                    size = quantity,
                    trailPrice = (decimal)stopPrice,
                    customTag = $"{entryUUID}_TRAIL",
                    linkedOrderId = orderSet.ParentOrderId
                };
                
                var stopResponse = await client.PlaceOrderAsync(stopOrder);
                
                if (stopResponse.success)
                {
                    strategy.Print($"✅ Trailing stop placed at ${stopPrice:F2} for {entryUUID}");
                    orderSet.StopOrderId = stopResponse.orderId; // Update with new trailing stop ID
                    return true;
                }
                else
                {
                    strategy.Print($"❌ Trailing stop failed: {stopResponse.errorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ Trailing stop exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get order set for external access
        /// </summary>
        public ProjectXOrderSet GetOrderSet(string entryUUID)
        {
            activeOrderSets.TryGetValue(entryUUID, out var orderSet);
            return orderSet;
        }

        #endregion

        #region Position Monitoring

        /// <summary>
        /// Get ProjectX position info by entry UUID
        /// </summary>
        public async Task<ProjectXPositionInfo> GetProjectXPositionByUUID(string entryUUID)
        {
            try
            {
                var positions = await client.GetOpenPositionsAsync();
                
                // Find position by customTag (our entryUUID)
                var position = positions.FirstOrDefault(p => p.customTag == entryUUID);
                
                if (position != null)
                {
                    // Get current market price
                    var marketData = await client.GetCurrentPriceAsync(position.contractId);
                    
                    return new ProjectXPositionInfo
                    {
                        positionId = position.id,
                        contractId = position.contractId,
                        size = position.size,
                        entryPrice = (double)position.averagePrice,
                        currentPrice = marketData?.currentPrice ?? (double)position.averagePrice,
                        unrealizedPnL = (double)position.unrealizedPnL,
                        lastUpdate = DateTime.Now,
                        isActive = true
                    };
                }
                
                return null;
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ Error getting ProjectX position: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Verify stop order is still active
        /// </summary>
        public async Task<bool> VerifyStopOrder(long stopOrderId)
        {
            try
            {
                if (stopOrderId <= 0)
                    return false;

                var orderStatus = await client.GetOrderStatusAsync(stopOrderId);
                
                return orderStatus != null && 
                       (orderStatus.state == "WORKING" || orderStatus.state == "SUBMITTED");
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ Error verifying stop order: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Emergency & Reconciliation

        /// <summary>
        /// Emergency exit position (called on profit drift)
        /// </summary>
        public async Task EmergencyExitPosition(string entryUUID, string contractId)
        {
            try
            {
                strategy.Print($"🚨 EMERGENCY EXIT: {entryUUID}");
                
                // Get current position info
                var positions = await client.GetOpenPositionsAsync();
                var position = positions.FirstOrDefault(p => p.customTag == entryUUID);
                
                if (position != null)
                {
                    bool wasLong = position.type == 1;
                    await ExitPosition(position.size, $"EMERGENCY_{entryUUID}", entryUUID, contractId, wasLong);
                }
                else
                {
                    strategy.Print($"⚠️ No ProjectX position found for emergency exit: {entryUUID}");
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ Emergency exit exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Startup reconciliation to find unprotected positions
        /// </summary>
        private async Task PerformStartupReconciliation()
        {
            try
            {
                strategy.Print("🔄 ProjectX startup reconciliation starting...");
                
                var positions = await client.GetOpenPositionsAsync();
                strategy.Print($"📊 Found {positions.Count} open ProjectX positions");
                
                foreach (var position in positions)
                {
                    await EnsurePositionHasStop(position);
                }
                
                strategy.Print("✅ ProjectX startup reconciliation complete");
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ CRITICAL: ProjectX reconciliation failed: {ex.Message}");
                strategy.Print("🛑 Trading disabled - manual intervention required");
                
                // Disable strategy
                strategy.IsEnabled = false;
            }
        }

        /// <summary>
        /// Ensure position has protective stop
        /// </summary>
        private async Task EnsurePositionHasStop(ProjectXPosition position)
        {
            try
            {
                // For startup reconciliation, we can't easily determine if stops exist
                // This is a simplified version - in production might need more sophisticated logic
                strategy.Print($"📊 Position: {position.contractId} {position.size} @ {position.averagePrice}");
                
                // Could implement emergency stop placement here if needed
                // For now, just log the position
            }
            catch (Exception ex)
            {
                strategy.Print($"❌ Error checking position protection: {ex.Message}");
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            client?.Dispose();
        }

        #endregion
    }
}