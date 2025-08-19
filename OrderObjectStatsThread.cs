using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	/// <summary>
	///  THIS FILE IS FOR ENTRY/EXIT FUNCTIONS, IDEALLY ON LOW TIMEFRAMES
	/// </summary>
    public partial class MainStrategy : Strategy
    {
        private Thread statsThread; // Worker thread for stats
        private volatile bool stopStatsThread = false; // To signal thread termination
        

        // Replace single ManualResetEvent with two event signals
        private AutoResetEvent workSignal = new AutoResetEvent(false); // Signal to start checking cycle
        private ManualResetEvent stopSignal = new ManualResetEvent(false); // Signal to terminate thread
        
        private readonly object statsLock = new object(); // Lock for thread-safe operations
        
        // Queue for stops that need stats updated on next cycle
        private ConcurrentQueue<simulatedStop> statsUpdateQueue = new ConcurrentQueue<simulatedStop>();

        // List to hold the order records to be updated
        private List<OrderRecordMasterLite> orderRecords = new List<OrderRecordMasterLite>();

        /// <summary>
        /// Starts the stats update thread.
        /// </summary>
        public void StartOrderObjectStatsThread()
        {
            Print("StartOrderObjectStatsThread");

            if (statsThread != null && statsThread.IsAlive)
            {
               // Print("[INFO] Stats thread is already running.");
                return;
            }

            stopStatsThread = false;
            stopSignal.Reset(); // Reset stop signal
            workSignal.Reset(); // Ensure work signal is initially unset

            statsThread = new Thread(UpdateStatsWorker)
            {
                IsBackground = true
            };
            statsThread.Start();
        }

        /// <summary>
        /// Stops the stats update thread.
        /// </summary>
        private void StopOrderObjectStatsThread()
        {
           // Print("[INFO] Stopping stats thread...");
            stopStatsThread = true;
            stopSignal.Set(); // Signal the thread to stop waiting
            workSignal.Set(); // Also signal work signal in case it's waiting there

            if (statsThread != null && statsThread.IsAlive)
            {
                try
                {
                    statsThread.Join(1000); // Wait for the thread to terminate with timeout
                }
                catch (Exception ex)
                {
                    Print($"[ERROR] Error stopping stats thread: {ex.Message}");
                }
                finally
                {
                    statsThread = null; // Clear the thread reference
                }
            }

            //Print("[INFO] Stats thread stopped successfully.");
        }

        /// <summary>
        /// Call this from OnBarUpdate to trigger background checks
        /// </summary>
        public void SignalBackgroundCheck()
        {
            if (!stopStatsThread && statsThread != null && statsThread.IsAlive)
            {
                workSignal.Set(); // Wake up the worker thread
            }
        }

        /// <summary>
        /// Worker thread to update stats for all tracked orders.
        /// </summary>
		private void UpdateStatsWorker()
		{
			string msg = "start";
            Print("[INFO] UpdateStatsWorker started.");
            WaitHandle[] waitHandles = { stopSignal, workSignal }; // Wait for either stop or work
            
		    try
		    {
		        while (!stopStatsThread)
		        {
                    // Wait indefinitely for either stop signal or work signal
                    int signaledHandle = WaitHandle.WaitAny(waitHandles);

                    if (stopStatsThread || signaledHandle == 0) // Check stop flag after wake-up
                    {
                        //Print("[INFO] UpdateStatsWorker stopping loop.");
                        break;
                    }

                    // If signaledHandle == 1, it means workSignal was Set
                    if (signaledHandle == 1)
                    {
		                try
		                {
		                    List<simulatedStop> stopsToCheck;
						    msg = "1";
		                    // Safely get the list of stops to check
		                    lock (statsLock)
		                    {
		                        stopsToCheck = MasterSimulatedStops
		                            .Where(s => s?.OrderRecordMasterLite != null)
		                            .ToList();
		                    }
						    msg = "2";
		                    foreach (var simStop in stopsToCheck)
		                    {
							    msg = "3";
		                        if (stopStatsThread) break; // Check termination flag inside the loop
							    msg = "4";
		                        try
		                        {
								    msg = "5";
                                    // Instead of Dispatcher.Invoke, call UpdateOrderStats directly
                                    // This is safe because we're in a background thread
                                    UpdateOrderStats(simStop, BarsInProgress, CurrentBars[BarsInProgress]); //// update pricing
									
									
		                        }
		                        catch (Exception ex)
		                        {
		                            Print($"[ERROR] Error updating stats for simulated stop: {ex.Message} + {msg}");
		                        }
		                    }
		                }
		                catch (Exception ex)
		                {
		                    Print($"[ERROR] Error during stats processing: {ex.Message} + {msg}");
		                }
                    }
		        }
		    }
		    catch (Exception ex)
		    {
		        Print($"[ERROR] Exception in UpdateStatsWorker: {ex.Message} + {msg}");
		    }
		    finally
		    {
		        //Print("[INFO] Stats thread terminated.");
		    }
		}
	        
	

		private OrderActionResult UpdateOrderStats(simulatedStop simStop,int bip,int thisBar)
		{
			OrderActionResult action = new OrderActionResult();
			string contractId = GetProjectXContractId();
			string msg = "";
			//Print($"UpdateOrderStats {simStop.EntryOrderUUID}");
			try 
			{
				// ========== DEFINE ALL VALUES UPFRONT ==========
				
				// Basic order info
				bool hasValidEntry = simStop.OrderRecordMasterLite?.EntryOrder != null;
				bool hasValidExit = simStop.OrderRecordMasterLite?.ExitOrder != null;
				bool isExitReady = simStop.OrderRecordMasterLite?.OrderSupplementals?.SimulatedStop?.isExitReady == true;
				bool forceExit = simStop.OrderRecordMasterLite?.OrderSupplementals?.forceExit == true;
				bool hasScaledIn = simStop.OrderRecordMasterLite?.OrderSupplementals?.hasScaledIn == false;
				
				if (!hasValidEntry || hasValidExit || forceExit || !isExitReady)
					return action;

				// Position info
				bool isLong = simStop.OrderRecordMasterLite.EntryOrder.IsLong;
				int instrumentSeriesIndex = simStop.instrumentSeriesIndex;
				int quantity = simStop.OrderRecordMasterLite.EntryOrder.Quantity;
				double entryPrice = simStop.OrderRecordMasterLite.EntryOrder.AverageFillPrice;
				
				// Price info
				double currentAsk = GetCurrentAsk(instrumentSeriesIndex);
				double currentBid = GetCurrentBid(instrumentSeriesIndex);
				double open = Open[0];
				double close = Close[0];
				
				// Profit calculations
				double longProfit = (currentAsk - entryPrice) * quantity * BarsArray[instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
				double shortProfit = (entryPrice - currentAsk) * quantity * BarsArray[instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
				double currentProfit = isLong ? longProfit : shortProfit;
				double allTimeHighProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit;
				double allTimeLowProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeLowProfit;
				
				// Thresholds and modifiers
				
				double maxLoss = simStop.OrderRecordMasterLite.PriceStats.OrderMaxLoss * quantity;
        
		        // DEBUG: Verify max loss calculation
		        //Print($"[MAXLOSS-CALC] {simStop.EntryOrderUUID.Substring(0,8)}: stopModifier={stopModifier:F2}, microContractStoploss={microContractStoploss:F2}, maxLoss={maxLoss:F2}");
				double softProfitTarget = simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold * quantity;
				double hardProfitTarget = simStop.OrderRecordMasterLite.PriceStats.OrderStatsHardProfitTarget * quantity;
				
				// Position management
				int totalAccountQuantity = getAllcustomPositionsCombined();
				bool canScaleIn = totalAccountQuantity + strategyDefaultQuantity <= strategyMaxQuantity && 
								 totalAccountQuantity + strategyDefaultQuantity <= accountMaxQuantity && 
								 getAllcustomPositionsCombined() < strategyMaxQuantity && 
								 !hasScaledIn;
				
				// Divergence info
		        string patternId = null;
		        curvesService.TryGetPatternId(simStop.EntryOrderUUID, out patternId);
		        double thompsonScoreModifier = 1;
		        if (!string.IsNullOrEmpty(patternId) && curvesService.thompsonScores.TryGetValue(patternId, out double score))
		        {
		            thompsonScoreModifier = score;
		        }
				
				
					int age = thisBar - simStop.OrderRecordMasterLite.EntryBar;
					simStop.OrderRecordMasterLite.PriceStats.profitByBar[age] = currentProfit;
				
		        // DEBUG: Log divergence and max loss values for comparison
		        //Print($"[DEBUG] {simStop.EntryOrderUUID}: Profit=${currentProfit:F2}, thisRecordDivergence={thisRecordDivergence:F3}, dynamicDivergenceThreshold={dynamicDivergenceThreshold:F3}");
		       
				
				// Condition flags
				bool isNewAllTimeHigh = currentProfit > allTimeHighProfit;
				bool isNewAllTimeLow = currentProfit < allTimeLowProfit;
				bool maxLossHit = currentProfit < -simStop.OrderRecordMasterLite.PriceStats.OrderMaxLoss;
				bool hardProfitTargetReached = currentProfit > hardProfitTarget;
				bool softProfitPullbackTarget = allTimeHighProfit > softProfitTarget && (currentProfit > 0 && currentProfit < Math.Max(softProfitTarget, pullBackPct * allTimeHighProfit));
		       
				// NEW: Check for trailing stop activation
				double trailingStopThreshold = pullBackPct * softTakeProfitMult * quantity;
				bool shouldActivateTrailingStop = currentProfit >= trailingStopThreshold && 
												  !simStop.OrderRecordMasterLite.OrderSupplementals.trailingStopActivated;
		
				if (shouldActivateTrailingStop && selectedBroker == brokerSelection.BlueSky_projectx)
				{
					// Trigger trailing stop conversion
					_ = Task.Run(() => ConvertToTrailingStop(simStop, currentProfit));
					simStop.OrderRecordMasterLite.OrderSupplementals.trailingStopActivated = true;
					Print($"[TRAILING-STOP] Activating for {simStop.EntryOrderUUID}: Profit ${currentProfit:F2} >= Threshold ${trailingStopThreshold:F2}");
				}
	
	        // 3. UPDATE PRICE STATS
	        if (isNewAllTimeHigh)
	        {
	            simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit = currentProfit;
	            if (currentProfit > softTakeProfitMult && simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold == 0)
	            {
	                simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold = GetCurrentBid(0);
	            }
	        }
	        
	        if (isNewAllTimeLow)
	        {
	            simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeLowProfit = currentProfit;
	        }
	        
	        if (simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit != currentProfit)
	        {
	            simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit = currentProfit;
	        }
			
			// ========== PROJECTX PROFIT SYNC CHECK ==========
			if (selectedBroker == brokerSelection.BlueSky_projectx)
			{
				_ = Task.Run(() => UpdateProjectXProfit(simStop));
				
				double ntProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
				double pxProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProjectXInfo.calculatedProfit;
				
				bool isProfitSynced = CheckProfitSync(ntProfit, pxProfit, simStop.EntryOrderUUID);
				
				if (!isProfitSynced)
				{
					Print($"🚨 PROFIT DRIFT: {simStop.EntryOrderUUID} NT:{ntProfit:F2} PX:{pxProfit:F2}");
					_ = Task.Run(() => HandleProfitDrift(simStop, ntProfit, pxProfit));
				}
			}
			
			DebugFreezePrint($"[PROFIT CHECK] {simStop.EntryOrderUUID}: Profit=${currentProfit:F2}, Max Loss is ${-maxLoss:F2}");
	        // 4. MAX LOSS CHECK (after divergence)
	        if (maxLossHit)
	        {
	            Print($"[MAX LOSS HIT] {simStop.EntryOrderUUID}: Profit=${currentProfit:F2}, MaxLoss=${-maxLoss:F2} (divergence didn't trigger)");
	            
	            if (!statsUpdateQueue.Contains(simStop)) {
	                statsUpdateQueue.Enqueue(simStop);
	            }
	            
	            simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
	            simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = isLong ? signalExitAction.MLL : signalExitAction.MLS;
	            
	            if (isLong)
	            {
					Print("Exiting Long MLL");
	                int orderSeriesIndex = GetOrderSeriesIndex();
	                ExitLong(orderSeriesIndex, quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
					if(!IsInStrategyAnalyzer && isRealTime == true)  _ = Task.Run(() =>  projectXBridge.ProjectXExitLong(quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID,contractId));

	            }
	            else
	            {
					Print("Exiting Short MLL");
	                int orderSeriesIndex = GetOrderSeriesIndex();
	                ExitShort(orderSeriesIndex, quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
					if(!IsInStrategyAnalyzer && isRealTime == true)  _ = Task.Run(() =>  projectXBridge.ProjectXExitShort(quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID,contractId));

	            }
	            
	            simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
	            simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
				MastersimulatedStopToDelete.Enqueue(simStop);
	            return action;
	        }
	
	                // 5. PROFIT TARGET CHECKS
					signalExitAction thisSignalExitAction = signalExitAction.NULL;
					
					if (hardProfitTargetReached && TakeBigProfitEnabled)
					{
						DebugFreezePrint("FLAG TBPS / PBS");
						thisSignalExitAction = isLong ? signalExitAction.TBPL : signalExitAction.TBPS;
					}
					else if (softProfitPullbackTarget && PullBackExitEnabled)
					{
						DebugFreezePrint("FLAG PBL / PBS");
						thisSignalExitAction = isLong ? signalExitAction.PBL : signalExitAction.PBS;
						
					}
					

					
						//canScaleIn = false;
						string instrumentCode = GetInstrumentCode();
						DateTime xMinutesSpacedGoal = StrategyLastScaleInTime.AddMinutes(entriesPerDirectionSpacingTime);
						
						 if(thisSignalExitAction == signalExitAction.PBL || thisSignalExitAction == signalExitAction.PBS)
						{
							// Exit logic
							simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
							simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
							
							if (isLong)
							{
								DebugFreezePrint($"Just exit long PBL");
								int orderSeriesIndex = GetOrderSeriesIndex();
								ExitLong(orderSeriesIndex, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
								if(!IsInStrategyAnalyzer && isRealTime == true)  _ = Task.Run(() =>  projectXBridge.ProjectXExitLong(quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID,contractId));
							}
							else
							{
								DebugFreezePrint($"Just exit Short PBS");
								int orderSeriesIndex = GetOrderSeriesIndex();
								ExitShort(orderSeriesIndex, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
								if(!IsInStrategyAnalyzer && isRealTime == true)  _ = Task.Run(() =>  projectXBridge.ProjectXExitShort(quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID,contractId));
							}
							
							simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
							MastersimulatedStopToDelete.Enqueue(simStop);
							return action;
						}
						
						else if(thisSignalExitAction == signalExitAction.TBPL || thisSignalExitAction == signalExitAction.TBPS)
						{
							// Exit logic
							simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
							simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
							
							if (isLong)
							{
								DebugFreezePrint($"Just exit long TBPL");
								int orderSeriesIndex = GetOrderSeriesIndex();
								ExitLong(orderSeriesIndex, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
								if(!IsInStrategyAnalyzer && isRealTime == true)  _ = Task.Run(() =>  projectXBridge.ProjectXExitLong(quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID,contractId));

							}
							else
							{
								DebugFreezePrint($"Just exit Short TBPS");
								int orderSeriesIndex = GetOrderSeriesIndex();
								ExitShort(orderSeriesIndex, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
								if(!IsInStrategyAnalyzer && isRealTime == true)  _ = Task.Run(() =>  projectXBridge.ProjectXExitShort(quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID,contractId));

							}
							
							simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
							MastersimulatedStopToDelete.Enqueue(simStop);
							return action;
						}
					
			
	
					
			return action;
			}
			catch (Exception ex)
			{
				Print($"Error in UpdateOrderStats: {ex.Message} + {msg}");
				// Ensure exit action is set even in error cases
				if (simStop.OrderRecordMasterLite?.OrderSupplementals?.thisSignalExitAction == signalExitAction.NULL)
				{
					simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.MLL; // Default to max loss
					simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "Error exit";
				}
				return action;
			}
				
		}

        /// <summary>
        /// Process the openOrders in statsUpdateQueue - add this to OnBarUpdate
        /// </summary>
        private void ProcessStatsQueue()
        {
            simulatedStop simStop;
            while (statsUpdateQueue.TryDequeue(out simStop))
            {
                if (simStop?.OrderRecordMasterLite != null)
                {
                    try
                    {
                        // Add to openOrders collection (this needs to happen on main thread)
                        openOrders.Add(simStop.OrderRecordMasterLite);
                    }
                    catch (Exception ex)
                    {
                        Print($"[ERROR] Error processing stats queue item: {ex.Message}");
                    }
                }
            }
        }
		
		private double GetMaxLoss(simulatedStop simStop)
		{
		    return microContractStoploss;
		}
	

        // Example entry condition
        private bool IsEntryConditionMet(OrderRecordMasterLite order)
        {
            return GetCurrentAsk(order.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex)< order.PriceStats.OrderStatsEntryPrice;
        }

        // Example exit condition
        private bool IsExitConditionMet(OrderRecordMasterLite order)
        {
            return GetCurrentAsk(order.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex) > order.PriceStats.OrderStatsAllTimeHighProfit;
        }
    }
}




