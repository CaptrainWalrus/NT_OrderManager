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
        private ManualResetEvent stopSignalOrders = new ManualResetEvent(false); // Signal for thread termination
        private readonly object statsLock = new object(); // Lock for thread-safe operations

        // List to hold the order records to be updated
        private List<OrderRecordMasterLite> orderRecords = new List<OrderRecordMasterLite>();


       

       

        /// <summary>
        /// Starts the stats update thread.
        /// </summary>
        private void StartOrderObjectStatsThread()
        {
            Print("StartOrderObjectStatsThread");

            if (statsThread != null && statsThread.IsAlive)
            {
               // Print("[INFO] Stats thread is already running.");
                return;
            }

            stopStatsThread = false;
            stopSignalOrders.Reset(); // Reset the signal

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
            stopSignalOrders.Set(); // Signal the thread to stop

            if (statsThread != null && statsThread.IsAlive)
            {
                try
                {
                    statsThread.Join(); // Wait for the thread to terminate
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
        /// Worker thread to update stats for all tracked orders.
        /// </summary>
		private void UpdateStatsWorker()
		{
			string msg = "start";
		    try
		    {
		        while (!stopStatsThread)
		        {
		            try
		            {
		                List<simulatedStop> stopsToUpdate;
						msg = "1";
		                // Safely process updates
		                lock (statsLock)
		                {
		                    stopsToUpdate = MasterSimulatedStops
		                        .Where(s => s?.OrderRecordMasterLite != null)
		                        .ToList();
		                }
						msg = "2";
		                foreach (var simStop in stopsToUpdate)
		                {
							msg = "3";
		                    if (stopStatsThread) break; // Check termination flag inside the loop
							msg = "4";
		                    try
		                    {
								msg = "5";
		                        // Process stats update
		                        Dispatcher.Invoke(() => UpdateOrderStats(simStop));
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
		
		            // Wait for stop signal or throttle the loop
		            if (stopSignalOrders.WaitOne(100)) break;
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
							openOrders.Add(simStop.OrderRecordMasterLite);
							
							int instrumentSeriesIndexThis = simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex;
				            double maxLoss = GetMaxLoss(simStop);
				
				            if (profit < -maxLoss)
				            {
				                // Set forceExit to true and specify the exit action
				                order.OrderSupplementals.forceExit = true;
								
				                order.OrderSupplementals.thisSignalExitAction = order.EntryOrder.OrderAction == OrderAction.Buy ? signalExitAction.MLL : (order.EntryOrder.OrderAction == OrderAction.SellShort ? signalExitAction.MLS : signalExitAction.NA);
				
				                // Log or track the flagged order if necessary
				                Print($"{Time[0]} Force exit flagged for {order.EntryOrderUUID} at profit: {profit}");
								
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
							            Print($"{Time[0]}  SELL {simStop.OrderRecordMasterLite.ExitOrderUUID} to Close {simStop.OrderRecordMasterLite.EntryOrderUUID} at {thisSignalExitAction} PROFIT ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit} ");
										
										//SubmitOrderUnmanaged(instrumentSeriesIndex, OrderAction.Sell, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  simStop.OrderRecordMasterLite.EntryOrderUUID, simStop.OrderRecordMasterLite.ExitOrderUUID);
										simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
							      		simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
										
							            MastersimulatedStopToDelete.Enqueue(simStop);
										break;
							
							        case signalExitAction.TBPS:
							        case signalExitAction.PBS:
							        case signalExitAction.FUNCS:
							        case signalExitAction.AGES:
										
							            simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
							            simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
										Print($"{Time[0]} BUYTOCOVER {simStop.OrderRecordMasterLite.ExitOrderUUID} to Close {simStop.OrderRecordMasterLite.EntryOrderUUID} at {thisSignalExitAction} PROFIT ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}  ");								
							       
										simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
										simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
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




