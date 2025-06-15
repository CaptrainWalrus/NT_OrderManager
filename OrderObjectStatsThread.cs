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
                                    UpdateOrderStats(simStop); //// update pricing
									
									
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
	        
	

		private OrderActionResult UpdateOrderStats(simulatedStop simStop)
		{
			OrderActionResult action = new OrderActionResult();
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
				bool hasScaledIn = simStop.OrderRecordMasterLite?.OrderSupplementals?.hasScaledIn == true;
				
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
				double stopModifier = simStop.OrderRecordMasterLite.OrderSupplementals.stopModifier;
				double pullbackModifier = simStop.OrderRecordMasterLite.OrderSupplementals.pullbackModifier;
				double maxLoss = microContractStoploss;
        
		        // DEBUG: Verify max loss calculation
		        //Print($"[MAXLOSS-CALC] {simStop.EntryOrderUUID.Substring(0,8)}: stopModifier={stopModifier:F2}, microContractStoploss={microContractStoploss:F2}, maxLoss={maxLoss:F2}");
				double pbMod = simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackPct > 0 ? simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackPct : 1;
				double softProfitTarget = pbMod * softTakeProfitMult * quantity;
				double hardProfitTarget = instrumentSeriesIndex == 3 ? standardContractTakeProfit * quantity : microContractTakeProfit * quantity;
				
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
				
				
		   
		        // DEBUG: Log divergence and max loss values for comparison
		        //Print($"[DEBUG] {simStop.EntryOrderUUID}: Profit=${currentProfit:F2}, thisRecordDivergence={thisRecordDivergence:F3}, dynamicDivergenceThreshold={dynamicDivergenceThreshold:F3}");
		       
				
				// Condition flags
				bool isNewAllTimeHigh = currentProfit > allTimeHighProfit;
				bool isNewAllTimeLow = currentProfit < allTimeLowProfit;
				bool maxLossHit = currentProfit < -simStop.OrderRecordMasterLite.PriceStats.OrderMaxLoss;
				bool hardProfitTargetReached = currentProfit > hardProfitTarget;
				bool softProfitPullbackTarget = allTimeHighProfit > softProfitTarget && 
											   (currentProfit < 0 || (currentProfit > 0 && currentProfit < Math.Max(softProfitTarget, pullBackPct * allTimeHighProfit)));
		       
	

				// VWAP conditions
				bool vwapLongExit = UseVwapStop && isLong && open > BB0.Lower[0] && close < BB0.Lower[0];
				bool vwapShortExit = UseVwapStop && !isLong && open < BB0.Upper[0] && close > BB0.Upper[0];

				        // ========== SEQUENTIAL CONDITION CHECKS ==========
        
        // 1. VWAP STOP CHECK (highest priority)
        if (UseVwapStop)
				{
					foreach(var kvp in vwapStopMapping)
					{
						var key = kvp.Key;
						var value = kvp.Value;
						key.VWAPValue = VWAP1;
						
						if (key.isLong && vwapLongExit && vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady)
						{
							vwapStopMapping[key].OrderSupplementals.thisSignalExitAction = signalExitAction.VWAPL;
							vwapStopMapping[key].OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid());
							ExitLong(1, simStop.OrderRecordMasterLite.EntryOrder.Quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
							vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady = false;
							vwapStopMapping[key].OrderSupplementals.forceExit = true;
							MastersimulatedStopToDelete.Enqueue(vwapStopMapping[key].OrderSupplementals.SimulatedStop);
							return action;
						}
						else if (!key.isLong && vwapShortExit && vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady)
						{
							vwapStopMapping[key].OrderSupplementals.thisSignalExitAction = signalExitAction.VWAPL;
							vwapStopMapping[key].OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid());
							ExitShort(1, simStop.OrderRecordMasterLite.EntryOrder.Quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
							vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady = false;
							vwapStopMapping[key].OrderSupplementals.forceExit = true;
							MastersimulatedStopToDelete.Enqueue(vwapStopMapping[key].OrderSupplementals.SimulatedStop);
							return action;
						}
					}
				}

				        // 2. DIVERGENCE CHECK (prioritize over max loss for earlier exits)
   			bool divergenceViolation = false;
			double divergenceScore = 0;
    		if(curvesService.divergenceScores.ContainsKey(simStop.EntryOrderUUID))
			{
		        double dynamicDivergenceThreshold = (DivergenceThreshold);
		        divergenceScore = curvesService.divergenceScores[simStop.EntryOrderUUID];
		        simStop.OrderRecordMasterLite.OrderSupplementals.divergence = divergenceScore;
				divergenceViolation = DivergenceSignalThresholds && divergenceScore > dynamicDivergenceThreshold && simStop.OrderRecordMasterLite.OrderSupplementals.isEntryRegisteredDTW == true;
			}
	        if (divergenceViolation && DivergenceSignalThresholds && currentProfit < -softProfitTarget)
	        {
	            try {
	                Print($"[DIVERGENCE EXIT] {simStop.EntryOrderUUID}: DIV={divergenceScore:F3}, Profit=${currentProfit:F2}");
	                
	                if (isLong)
	                {
	                    simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.DIV_L;
	               
	                    ExitLong(1, quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
	                }
	                else
	                {
	                    simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.DIV_S;
	                    
	                    ExitShort(1, quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
	                }
	                
	                simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
	                simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
	                return action;
	            }
	            catch (Exception ex) {
	                Print($"Divergence catch : {ex}");
	            }
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
	                ExitLong(1, quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
	            }
	            else
	            {
					Print("Exiting Short MLL");
	                ExitShort(1, quantity, simStop.OrderRecordMasterLite.ExitOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID);
	            }
	            
	            simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
	            simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
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
					
					
					// 6. EXECUTE EXIT ACTIONS
					if (thisSignalExitAction == signalExitAction.TBPL || thisSignalExitAction == signalExitAction.TBPS || thisSignalExitAction == signalExitAction.PBL || thisSignalExitAction == signalExitAction.PBS)
					{
						DateTime xMinutesSpacedGoal = StrategyLastScaleInTime.AddMinutes(entriesPerDirectionSpacingTime);
						if(canScaleIn && Times[0][0] >= xMinutesSpacedGoal && entriesPerDirectionSpacingTime > 0)
							   
				             
						{
							
							// Scale in logic
							DebugFreezePrint("SCALE IN TBPS / TBPL / PBL / PBS");
							simStop.OrderRecordMasterLite.OrderSupplementals.hasScaledIn = true;
							
							action.accountEntryQuantity = strategyDefaultQuantity;
							action.appendSignal = "";
							action.builtSignal = new patternFunctionResponse();
							action.builtSignal.patternId = simStop.OrderRecordMasterLite.OrderSupplementals.patternId;
							action.builtSignal.patternSubType = simStop.OrderRecordMasterLite.OrderSupplementals.patternSubtype;
							action.OA = simStop.EntryOrderAction;
							action.orderType = OrderType.Market;
							action.signalPackageParam = simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage;
							action.thisBar = CurrentBars[0];
							StrategyLastScaleInTime = Time[0]; ///no full order but will stop rapid-fire orders
						
						}
						else
						{
							// Exit logic
							simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
							simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
							
							if (isLong)
							{
								DebugFreezePrint($"Just exit long TBPL");
								ExitLong(1, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
							}
							else
							{
								DebugFreezePrint($"Just exit Short TBPS");
								ExitShort(1, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
							}
							
							simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
							MastersimulatedStopToDelete.Enqueue(simStop);
							return action;
						}
					}
					else if (thisSignalExitAction == signalExitAction.FUNCL || thisSignalExitAction == signalExitAction.FUNCS ||
							 thisSignalExitAction == signalExitAction.AGEL || thisSignalExitAction == signalExitAction.AGES)
					{
						simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
						simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
						
						if (isLong)
						{
							DebugFreezePrint("EXIT OTHER LONG");
							ExitLong(1, simStop.quantity, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
						}
						else
						{
							DebugFreezePrint("EXIT OTHER SHORT");
							ExitShort(1, 1, simStop.ExitOrderUUID, simStop.EntryOrderUUID);
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




