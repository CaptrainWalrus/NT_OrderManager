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
	        
	

		private void UpdateOrderStats(simulatedStop simStop)
		{
			string msg = " 1 ";
			try{
		    var order = simStop.OrderRecordMasterLite;
			 msg = " 2 ";
		    if (order?.PriceStats != null)
		    {
		  
				
				  
				
				
				if(UseVwapStop)
				{
					foreach(var kvp in vwapStopMapping)
					{
					    var key = kvp.Key;
					    var value = kvp.Value;
									
						key.VWAPValue = VWAP1;
						
	
						if(key.isLong == true)
						{
							if(Open[0] > key.BBValue.Lower[0] && Close[0] < key.BBValue.Lower[0] && vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady == true)
							//if(Open[0] > key.VWAPValue[0] && Close[0] < key.VWAPValue[0] && vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady == true)
							{
								
								vwapStopMapping[key].OrderSupplementals.thisSignalExitAction = signalExitAction.VWAPL;
					            vwapStopMapping[key].OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid());
								
								ExitLong(1,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);

								//SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  simStop.EntryOrderUUID, simStop.ExitOrderUUID);
								
								vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady = false;
					      		vwapStopMapping[key].OrderSupplementals.forceExit = true;
								//Print("Enqueue MastersimulatedStopToDelete");
							            // Queue for deletion on main thread
					            MastersimulatedStopToDelete.Enqueue(vwapStopMapping[key].OrderSupplementals.SimulatedStop);
								continue;
							}
						}
						else if(key.isLong == false)
						{
							if(Open[0] < key.BBValue.Upper[0] && Close[0] > key.BBValue.Upper[0] && vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady == true)
							//if(Open[0] < key.VWAPValue[0] && Close[0] > key.VWAPValue[0] && vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady == true)
							{
								vwapStopMapping[key].OrderSupplementals.thisSignalExitAction = signalExitAction.VWAPL;
					            vwapStopMapping[key].OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid());
					       		
								ExitShort(1,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
								//SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  simStop.EntryOrderUUID, simStop.ExitOrderUUID);
								
								vwapStopMapping[key].OrderSupplementals.SimulatedStop.isExitReady = false;
								vwapStopMapping[key].OrderSupplementals.forceExit = true;
								//Print("Enqueue MastersimulatedStopToDelete");
					            // Queue for deletion on main thread
					            MastersimulatedStopToDelete.Enqueue(vwapStopMapping[key].OrderSupplementals.SimulatedStop);
								break;
							}
						}
					}
				}
				
		        if(simStop.OrderRecordMasterLite.ExitOrder == null && simStop.OrderRecordMasterLite.EntryOrder != null && simStop.OrderRecordMasterLite.OrderSupplementals.forceExit == false)
				{
					
					/// ALLTIME HIGH
					msg = "ATH";
						if (simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit > simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit)
						{
						    simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
							
							///PULLBACK THRESHOLD

							if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit > softTakeProfitMult && simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold == 0)
							{
								simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold = GetCurrentBid(0); // set the price at which we went past the mult
							}
						}
						///ALLTIME LOW
						msg = "ATL";
						if (simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeLowProfit)
						{
						    simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeLowProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
						}
						
						
						
						int instrumentSeriesIndex = simStop.instrumentSeriesIndex;
						
						double longProfit = (GetCurrentAsk(instrumentSeriesIndex) - simStop.OrderRecordMasterLite.EntryOrder.AverageFillPrice) * simStop.OrderRecordMasterLite.EntryOrder.Quantity * BarsArray[instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
						double shortProfit = (simStop.OrderRecordMasterLite.EntryOrder.AverageFillPrice-GetCurrentAsk(instrumentSeriesIndex)) * simStop.OrderRecordMasterLite.EntryOrder.Quantity * BarsArray[instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
						double profit = simStop.OrderRecordMasterLite.EntryOrder.IsLong ? longProfit : shortProfit;	
						
						msg = "Profit";
						if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit != profit)
						{
							/// profit update
							simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit = profit;
							
							
						}
						/// MAX LOSS 
						/// 
					
						msg = "MLL";
						if(simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady == true)
						{
							// NOTE: This might need to be moved to the main thread if it modifies
							// collections accessed by the main thread
							if (!statsUpdateQueue.Contains(simStop)) {
								statsUpdateQueue.Enqueue(simStop);
							}
							
							int instrumentSeriesIndexThis = simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex;
				            double maxLoss = GetMaxLoss(simStop);
				
				            if (profit < -maxLoss)
				            {
				                // Only flag for exit if not already flagged to prevent repetitive messages
				                if (!order.OrderSupplementals.forceExit)
				                {
				                    // Set forceExit to true and specify the exit action
				                    order.OrderSupplementals.forceExit = true;
				                    
				                    order.OrderSupplementals.thisSignalExitAction = order.EntryOrder.OrderAction == OrderAction.Buy ? signalExitAction.MLL : (order.EntryOrder.OrderAction == OrderAction.SellShort ? signalExitAction.MLS : signalExitAction.NA);
 									if(simStop.EntryOrderAction == OrderAction.Buy)
									{
										
	
										ExitLong(1,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
										simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							      		simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
										return;
									}
									if(simStop.EntryOrderAction == OrderAction.SellShort)
									{
										
										ExitShort(1,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
										simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							      		simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
										return;
									}
                				}
				               
				            }
				        
								

						}
						
						////HARD TAKE, SOFT TAKE
						/// 
						msg = "TP";
						if(simStop.OrderRecordMasterLite.EntryOrderUUID == simStop.EntryOrderUUID && simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady == true && simStop.OrderRecordMasterLite.OrderSupplementals.forceExit == false)
						{
							
							
							// Confirm matching record isn't yet filled
							if (simStop.OrderRecordMasterLite.EntryOrder != null && 
							    simStop.OrderRecordMasterLite.EntryOrder.OrderState == OrderState.Filled && 
							    (simStop.OrderRecordMasterLite.EntryOrder.OrderAction == OrderAction.SellShort || simStop.OrderRecordMasterLite.EntryOrder.OrderAction == OrderAction.Buy) && 
							    simStop.OrderRecordMasterLite.ExitOrder == null)
							{
								
								int instrumentSeriesIndexVar = simStop.instrumentSeriesIndex;
								double tickValue = simStop.instrumentSeriesTickValue;

								maximumOpenInstrumentIndex = Math.Max(maximumOpenInstrumentIndex,instrumentSeriesIndexVar);
								
							
		           
							    double currentProfit = 	CalculateTotalOpenPositionProfitForInstrument(instrumentSeriesIndex);
							    double allTimeHighProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit;
								
								bool EMA3_ok = (simStop.OrderRecordMasterLite.EntryOrder.IsLong) ? IsRising(EMA3) : IsFalling(EMA3);
								
								int positionAge = CurrentBars[0] - simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedEntry.EntryBar;
								
								string patternId = null;
								// Get the pattern ID from the registered position mapping
								curvesService.TryGetPatternId(simStop.EntryOrderUUID, out patternId);
							
								double thompsonScoreModifier = 1;
		                        if (!string.IsNullOrEmpty(patternId))
		                        {
		                            if (curvesService.thompsonScores.TryGetValue(patternId, out double score))
		                            {
		                                thompsonScoreModifier = score;
		                            }
		                        }
								// Use cached divergence value (no HTTP call)
								double thisRecordDivergence = curvesService.CheckDivergence(simStop.EntryOrderUUID)*Math.Sign((int)currentProfit);
								simStop.OrderRecordMasterLite.OrderSupplementals.divergence = thisRecordDivergence;
								simStop.OrderRecordMasterLite.OrderSupplementals.maxDivergence = Math.Max(simStop.OrderRecordMasterLite.OrderSupplementals.maxDivergence, thisRecordDivergence);
								
								bool divergencePullback = simStop.OrderRecordMasterLite.OrderSupplementals.maxDivergence > softTakeProfitMult && simStop.OrderRecordMasterLite.OrderSupplementals.divergence < (pullBackPct * simStop.OrderRecordMasterLite.OrderSupplementals.maxDivergence);
								
								double dynamicDivergenceThreshold = (thompsonScoreModifier*DivergenceThreshold);
								
								if(DivergenceSignalThresholds)
								{ 
									if(simStop.OrderRecordMasterLite.OrderSupplementals.divergence > dynamicDivergenceThreshold && currentProfit < simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit)
									{
									
									if(simStop.OrderRecordMasterLite.OrderSupplementals.isEntryRegisteredDTW == true)
									{
										try {
									            Print($"[DIVERGENCE VIOLATION!] {simStop.EntryOrderUUID}: DIV={curvesService.divergenceScores[simStop.EntryOrderUUID]:F3}, Profit=${currentProfit:F2}");

									            // Check divergence using the new adaptive system
											//if (curvesService.GetExitSignal(simStop.EntryOrderUUID) > 0)
									           // {
													// Enhanced logging with adaptive divergence information
													string adaptiveInfo = "";
													if (CurvesV2Service.CurrentShouldExit)
													{
														adaptiveInfo = $" [ADAPTIVE: {CurvesV2Service.CurrentConsecutiveBars}/{CurvesV2Service.CurrentConfirmationBarsRequired} bars confirmed]";
													}
													else
													{
														adaptiveInfo = $" [LEGACY: Score > threshold]";
													}
													

											        if(simStop.EntryOrderAction == OrderAction.Buy)// && IsFalling(SMA200) && SMA200[0] < Close[0])
													{
														
					
														simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.DIV_L;
														simStop.OrderRecordMasterLite.OrderSupplementals.divergence = curvesService.divergenceScores[simStop.EntryOrderUUID];
														ExitLong(1,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
														simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							      						simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
														return;
													}
													if(simStop.EntryOrderAction == OrderAction.SellShort)// && IsRising(SMA200) && SMA200[0] > Close[0])
													{
														simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.DIV_S;
														simStop.OrderRecordMasterLite.OrderSupplementals.divergence = curvesService.divergenceScores[simStop.EntryOrderUUID];
														ExitShort(1,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
														simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							      						simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
														return;
													}
									            
											
									        }
									        catch (Exception ex) {
									          // Print($"Divergence catch : {ex}");
									        }
										}
									}
								}
								
								
								
								
							  	int indexQuantity = GetPositionCountByIndex(instrumentSeriesIndex);/// quantity across instrument size
							
						
								double softProfitTarget = softTakeProfitMult*simStop.OrderRecordMasterLite.EntryOrder.Quantity;
							    double hardProfitTarget = instrumentSeriesIndex == 3 ? standardContractTakeProfit*simStop.OrderRecordMasterLite.EntryOrder.Quantity : microContractTakeProfit*simStop.OrderRecordMasterLite.EntryOrder.Quantity;
								
								//softProfitTarget *= ATR(100)[0];
								//hardProfitTarget *= ATR(100)[0];
								/// this version doesnt use age
							   	// bool softProfitPullbackTarget = (allTimeHighProfit > softProfitTarget && 
							    //                                 currentProfit < Math.Max(softProfitTarget, pullBackPct * allTimeHighProfit));
							    bool hardProfitTargetReached = (currentProfit > hardProfitTarget);
							    int age = CurrentBars[0] - simStop.OrderRecordMasterLite.EntryBar;
							
							
								
								bool softProfitPullbackTarget = (allTimeHighProfit > softProfitTarget && (currentProfit < 0 || (currentProfit > 0 && currentProfit < Math.Max(softProfitTarget, pullBackPct * allTimeHighProfit))));
																
								signalExitAction thisSignalExitAction = signalExitAction.NULL;
							
		            		  
							  
							    if (hardProfitTargetReached && TakeBigProfitEnabled && simStop.isExitReady)/// dont do take for large movements
							    {
							        thisSignalExitAction = (simStop.OrderRecordMasterLite.EntryOrder.IsLong) ? signalExitAction.TBPL : signalExitAction.TBPS;
							    }
							    else if (softProfitPullbackTarget && PullBackExitEnabled && simStop.isExitReady)
							    {
							     
							
									thisSignalExitAction = (simStop.OrderRecordMasterLite.EntryOrder.IsLong) ? signalExitAction.PBL : signalExitAction.PBS;
									
							    }
							 	
								
								
								
								
								
								switch (thisSignalExitAction)
							    {
							        case signalExitAction.TBPL:
							        case signalExitAction.PBL:
							        case signalExitAction.FUNCL:
							        case signalExitAction.AGEL:
										
										
							            simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
							            simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
							            //Print($"{Time[0]}  SELL {simStop.OrderRecordMasterLite.ExitOrderUUID} to Close {simStop.OrderRecordMasterLite.EntryOrderUUID} at {thisSignalExitAction} PROFIT ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit} ");
										
										ExitLong(1,1,simStop.ExitOrderUUID,simStop.EntryOrderUUID);
										//SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  null, simStop.ExitOrderUUID);
										
										simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							      		simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
										//Print("Enqueue MastersimulatedStopToDelete");
							            // Queue for deletion on main thread
							            MastersimulatedStopToDelete.Enqueue(simStop);
										break;
										
							
							        case signalExitAction.TBPS:
							        case signalExitAction.PBS:
							        case signalExitAction.FUNCS:
							        case signalExitAction.AGES:
										
										
							            simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
							            simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
										//Print($"{Time[0]} BUYTOCOVER {simStop.OrderRecordMasterLite.ExitOrderUUID} to Close {simStop.OrderRecordMasterLite.EntryOrderUUID} at {thisSignalExitAction} PROFIT ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}  ");								
							       
										ExitShort(1,1,simStop.ExitOrderUUID,simStop.EntryOrderUUID);

										//SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  null, simStop.ExitOrderUUID);
										
										simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
										simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
										//Print("Enqueue MastersimulatedStopToDelete");
							            // Queue for deletion on main thread
							            MastersimulatedStopToDelete.Enqueue(simStop);
										break;
										
									default:
								            // No exit action, continue processing
								            break;
							
							    }
							}

						}
						
						
				}
		    }
			}
			 catch (Exception ex)
	        {
	            Print($"Error in UpdateOrderStats: {ex.Message} + {msg}");
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




