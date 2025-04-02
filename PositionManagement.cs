#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
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
using System.IO;
using System.Xml.Serialization;
using System.IO;

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	public partial class MainStrategy : Strategy
	{


/// POSITION MANAGMENT

	public int maximumOpenInstrumentIndex = 1;
	public MarketPosition customMarketPosition;

	/*
	protected void updateSimulatedEntryConditions()
	{
		lastFunction = "simulatedEntryConditions";
		string tryCatchSelection = "Begin simulatedEntryConditions";
		try
		{		
			DebugPrint(debugSection.ProcessAdjustments,"Begin simulatedEntryConditions");
			/// scroll through sims
			if(MasterSimulatedEntries.Count() > 0)
			{
				DebugPrint(debugSection.ProcessAdjustments,"Begin simulatedEntryConditions 2");
				//if(manualEntriesOnly) Print("MasterSimulatedEntries.Count > 0");	
				foreach(simulatedEntry simEntry in MasterSimulatedEntries)
				{
					lastFunction = "simulatedEntryConditions loop "+MasterSimulatedEntries.Count() ;
					if(simEntry.isEnterReady == true)
					{
						if(simEntry.EMATrack != null)
						{
							BackBrush = Brushes.Blue;
							simEntry.stopPrice = simEntry.EMATrack[0];
							//Draw.Diamond(this,"EMATrack"+CurrentBars[0],true,0,simEntry.stopPrice,Brushes.Cyan);

						}
						else if(simEntry.EMATrack == null)
						{
								BackBrush = Brushes.Red;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch updateSimulatedEntryConditions: "+tryCatchSelection+" - Exception error; "+ex);
		
		}
	}
	*/
	protected void simulatedEntryConditions()
	{
		lastFunction = "simulatedEntryConditions";
		string tryCatchSelection = "Begin simulatedEntryConditions";
		try
		{		
		
			DebugPrint(debugSection.ProcessAdjustments,"Begin simulatedEntryConditions");
			/// scroll through sims
			if(MasterSimulatedEntries.Count() > 0)
			{
				DebugPrint(debugSection.ProcessAdjustments,"Begin simulatedEntryConditions 2");
				//if(manualEntriesOnly) Print("MasterSimulatedEntries.Count > 0");	
				foreach(simulatedEntry simEntry in MasterSimulatedEntries)
				{
					
					lastFunction = "simulatedEntryConditions loop "+MasterSimulatedEntries.Count() ;

					if(simEntry.isEnterReady == true)
					{

						int targetedSeriesIndex =  simEntry.instrumentSeriesIndex; 
						if(BarsInProgress == 0)
						{
					

									int age = CurrentBars[0]-simEntry.EntryBar;
									tryCatchSelection = "simulatedEntryConditions 2";
									if(simEntry.EntryOrderType == OrderType.Market)
									{
								
										tryCatchSelection = "simulatedEntryConditions 2aa";
										if(simEntry.EntryOrderAction == OrderAction.Buy)
										{
											tryCatchSelection = "simulatedEntryConditions 2a ";
												
												
											
									  			int Q = getAllcustomPositionsCombined();

												simEntry.isEnterReady = false;
												//Print($"BarsInProgress {BarsInProgress} BAR:{CurrentBars[BarsInProgress]} TIME: {Time[0]} SubmitOrderUnmanaged!  {simEntry.EntryOrderUUID} ,Quantity {Q} EnterLong {GetMarketPositionByIndex(BarsInProgress)}");
												SubmitOrderUnmanaged(targetedSeriesIndex, OrderAction.Buy, OrderType.Market, simEntry.quantity, 0, 0, null, simEntry.EntryOrderUUID);
	
												continue;
												
										}
										else if(simEntry.EntryOrderAction == OrderAction.SellShort)
										{
												tryCatchSelection = "simulatedEntryConditions b";
												
												simEntry.isEnterReady = false;
												//Print($"BarsInProgress {BarsInProgress} BAR:{CurrentBars[BarsInProgress]} TIME: {Time[0]} SubmitOrderUnmanaged!  {simEntry.EntryOrderUUID}, Quantity {Q} EnterShort {GetMarketPositionByIndex(BarsInProgress)}");
												SubmitOrderUnmanaged(targetedSeriesIndex, OrderAction.SellShort, OrderType.Market, simEntry.quantity, 0, 0, null, simEntry.EntryOrderUUID);
	
												continue;
												
											
										}
									}
									
							
						}
					
					
					}
					if(simEntry.isEnterReady == false)
					{
					
						DebugPrint(debugSection.ProcessAdjustments,"simEntry "+simEntry.EntryOrderUUID+" isEnterReady == false");
						MastersimulatedEntryToDelete.Enqueue(simEntry);
						
					}
					
				}
				while(MastersimulatedEntryToDelete.Count > 0)
				{
					
					simulatedEntry simulatedEntryDelete = MastersimulatedEntryToDelete.Dequeue();
				
					MasterSimulatedEntries.Remove(simulatedEntryDelete);
				}
			}
			else if(MasterSimulatedEntries.Count == 0)
			{
			}
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch Section: "+tryCatchSelection+" - Exception error; "+ex);
		
		}
	
		}
	/*
	protected void updatePriceStats()
	{
		try
		{
			lastFunction = "updatePriceStats";
			if(MasterSimulatedStops.Count() > 0)
			{
				foreach(simulatedStop simStop in MasterSimulatedStops) 
				{
					
					if(simStop.OrderRecordMasterLite.ExitOrder == null && simStop.OrderRecordMasterLite.EntryOrder != null)
					{
						
							//forceDrawDebug((orderRecordMaster.ExitOrder == null)+" && "+(orderRecordMaster.EntryOrder != null),-1,0,Low[0],Brushes.Lime,true);
							if(BarsInProgress == 0 && simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.instrumentSeriesIndex == 3 && simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady == true)
							{
								
								//Print($"updatePriceStats FOUND {simStop.OrderRecordMasterLite.EntryOrderUUID} index {simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.instrumentSeriesIndex} exit?{simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady == true}  ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}");
							}
							if (simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit > simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit)
							{
							    simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
								if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeHighProfit > softTakeProfitMult && simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold == 0)
								{
									simStop.OrderRecordMasterLite.PriceStats.OrderStatspullBackThreshold = GetCurrentBid(0); // set the price at which we went past the mult
								}
							}
							if (simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeLowProfit)
							{
							    simStop.OrderRecordMasterLite.PriceStats.OrderStatsAllTimeLowProfit = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
							}
							
							int instrumentSeriesIndex = simStop.instrumentSeriesIndex;
							
							double longProfit = (GetCurrentBid(instrumentSeriesIndex) - simStop.OrderRecordMasterLite.EntryOrder.AverageFillPrice) * simStop.OrderRecordMasterLite.EntryOrder.Quantity * BarsArray[instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
							double shortProfit = (simStop.OrderRecordMasterLite.EntryOrder.AverageFillPrice-GetCurrentBid(instrumentSeriesIndex)) * simStop.OrderRecordMasterLite.EntryOrder.Quantity * BarsArray[instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
							
							double profit = simStop.OrderRecordMasterLite.EntryOrder.IsLong ? longProfit : shortProfit;	
							
							
							if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit != profit)
							{
								/// profit update
								simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit = profit;
								
								
							}
							
							
					}
					
				}
			}
			if(LiteMasterRecords.Count() == 0 && getAllcustomPositionsCombined() > 0 && GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Flat)
			{
				if(!IsInStrategyAnalyzer)
				{
					Print("LiteMasterRecords is empty but the account position is "+GetMarketPositionByIndex(BarsInProgress));
				}
			}
		}
		catch (Exception ex)
		{
		    Print("Error in updatePriceStats  " + ex.Message +" ");
		}
	}

	*/
	protected double CalculateTotalOpenPositionProfit()
	{
		double profitUnrealized = 0;
		if(MasterSimulatedStops.Count() > 0)
		{
		    try
		    {
				
		        foreach (simulatedStop simStop in MasterSimulatedStops)
		        {
					if(simStop.isExitReady == true)
					{
					
		            // Skip if no entry order or already exited
		            if (simStop.OrderRecordMasterLite.EntryOrder == null || simStop.OrderRecordMasterLite.ExitOrder != null)
		                continue;
		
		            // Update the profit for the individual simulated stop
		           	profitUnrealized += simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
					
					//Print($"profitUnrealized ${profitUnrealized} , {simStop.EntryOrderUUID}  ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}");
					}
		        }
				return profitUnrealized;
		    }
		    catch (Exception ex)
		    {
		        Print($"Error in CalculateTotalOpenPositionProfit: {ex.Message}");
		    }
		
		    
		}
		return profitUnrealized;
	}

	protected double CalculateTotalOpenPositionProfitForInstrument(int barsArrayIndex)
	{
		double profitUnrealized = 0;
		if(MasterSimulatedStops.Count() > 0)
		{
		    try
		    {
		        foreach (simulatedStop simStop in MasterSimulatedStops)
		        {
					if(simStop.isExitReady == true)
					{
					
						if(simStop.instrumentSeriesIndex == barsArrayIndex)
						{
				            // Skip if no entry order or already exited
				            if (simStop.OrderRecordMasterLite.EntryOrder == null || simStop.OrderRecordMasterLite.ExitOrder != null)
				                continue;
				
				            // Update the profit for the individual simulated stop
				           	profitUnrealized += simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit;
						}
					//Print($"profitUnrealized ${profitUnrealized} , {simStop.EntryOrderUUID}  ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}");
					}
		        }
				return profitUnrealized;
		    }
		    catch (Exception ex)
		    {
		        Print($"Error in CalculateTotalOpenPositionProfit: {ex.Message}");
		    }
		
		    
		}
		return profitUnrealized;
	}
	
	public MarketPosition GetMarketPositionByIndex(int instrumentSeriesIndex)
	{
	    MarketPosition positionByInstrument = MarketPosition.Flat; // Default to Flat
	
	    if (MasterSimulatedStops.Count() > 0)
	    {
	        try
	        {
	            foreach (simulatedStop simStop in MasterSimulatedStops)
	            {
	                // Skip stops that are not exit-ready or have no entry/exit orders
	                if (!simStop.isExitReady || simStop.OrderRecordMasterLite.EntryOrder == null || simStop.OrderRecordMasterLite.ExitOrder != null)
	                    continue;
	
	                // Check if the instrument series index matches
	                if (simStop.instrumentSeriesIndex == instrumentSeriesIndex)
	                {
	                    // Determine the market position for this instrument
	                    if (simStop.OrderRecordMasterLite.EntryOrder.IsLong)
	                    {
	                        positionByInstrument = MarketPosition.Long;
	                        break; // Found a match, no need to continue
	                    }
	                    else if (simStop.OrderRecordMasterLite.EntryOrder.IsShort)
	                    {
	                        positionByInstrument = MarketPosition.Short;
	                        break; // Found a match, no need to continue
	                    }
	                }
	            }
	        }
	        catch (Exception ex)
	        {
	            Print($"Error in GetMarketPositionByIndex: {ex.Message}");
	        }
	    }
	
	    return positionByInstrument; // Return the determined market position
	}

	public int GetPositionCountByIndex(int instrumentSeriesIndex)
	{
	    int positionCount = 0; // Initialize position count to 0
	
	    if (MasterSimulatedStops.Count() > 0)
	    {
	        try
	        {
	            foreach (simulatedStop simStop in MasterSimulatedStops)
	            {
	                // Skip stops that are not exit-ready or have no entry/exit orders
	                if (!simStop.isExitReady || simStop.OrderRecordMasterLite.EntryOrder == null || simStop.OrderRecordMasterLite.ExitOrder != null)
	                    continue;
	
	                // Check if the instrument series index matches
	                if (simStop.instrumentSeriesIndex == instrumentSeriesIndex)
	                {
	                    // Add the position quantity for this instrument
	                    positionCount += simStop.OrderRecordMasterLite.EntryOrder.Quantity;
	                }
	            }
	        }
	        catch (Exception ex)
	        {
	            Print($"Error in GetPositionCountByIndex: {ex.Message}");
	        }
	    }
	
	    return positionCount; // Return the total position count
	}

	protected void clearExpiredWorkingOrders()
	{
		    // Loop through all orders associated with the strategy
		    foreach (Order order in Orders)
		    {
		        // Check if the order is working
		        if (order.OrderState == OrderState.Working)
		        {
		            // Calculate the time difference between the current time and the order's entry time
		            TimeSpan timeSinceEntry = Time[0] - order.Time;
		
		            // If the order is more than 5 minutes old, cancel it
		            if (timeSinceEntry.TotalMinutes > 5)
		            {
		                Print("Cancelling order: " + order.ToString() + " as it is older than 5 minutes.");
		               	CancelOrder(order);
		            }
		        }
		    }
	}
	/*
	protected void syncStops()
	{
		openOrders.Clear();
		lastFunction = "syncStops";
	
		if(MasterSimulatedStops.Count() > 0)
			{
				foreach(simulatedStop simStop in MasterSimulatedStops) 
				{			
					
						if(simStop.OrderRecordMasterLite.ExitOrder == null && simStop.OrderRecordMasterLite.EntryOrder != null)
						{
							///process by bar it belongs to
							if(BarsInProgress == (simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex))
							{
								if(simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop != null)
								{
									
									if(MasterSimulatedStops.Contains(simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop))
									{
											
										if(simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady == true)
										{
											openOrders.Add(simStop.OrderRecordMasterLite);
											int age = CurrentBars[0]-simStop.OrderRecordMasterLite.EntryBar;
											
											int instrumentSeriesIndex = simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex;
											double tickValue = simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesTickValue; /// scaled to pct of tickvalue
											
											int quantityPerIndex = GetPositionCountByIndex(instrumentSeriesIndex);
											
											///standardContractStoploss is a fixed value
											double maxLossLong = instrumentSeriesIndex == InstrumentStandardIndex ? standardContractStoploss : microContractStoploss;
											double maxLossShort = instrumentSeriesIndex == InstrumentStandardIndex ? standardContractStoploss : microContractStoploss;
							
											if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < -microContractStoploss && instrumentSeriesIndex == InstrumentMicroIndex)
											{
												//	Print($"{Time[0]} Order {simStop.OrderRecordMasterLite.EntryOrderUUID} profit {profit} microContractStoploss -{microContractStoploss}");
											}
											/// make sure when we exit that it's due to a reversal trend and not a brief pullback 
											if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < -standardContractStoploss && instrumentSeriesIndex == 3)
											{
												//Print($"{Time[0]} Order {simStop.OrderRecordMasterLite.EntryOrderUUID} profit {simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit} standardContractStoploss {standardContractStoploss} ");
											}
											if (simStop.OrderRecordMasterLite.EntryOrder.OrderAction == OrderAction.Buy && simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < -maxLossLong)
											{
												
												simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.MLL;
		
												//if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < 0 )Print($"{Time[0]} SELL {simStop.OrderRecordMasterLite.ExitOrderUUID} to close {simStop.OrderRecordMasterLite.EntryOrderUUID} at MAXLOSS ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit} < ${-maxLossLong}");
												SubmitOrderUnmanaged(instrumentSeriesIndex, OrderAction.Sell, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0, simStop.OrderRecordMasterLite.EntryOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID + "_Exit_" + DateTime.Now.Ticks);
												simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
		
											}
										
											if (simStop.OrderRecordMasterLite.EntryOrder.OrderAction == OrderAction.SellShort &&  simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < -maxLossShort )
											{
												
												simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.MLS;
												//if(simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit < 0 ) Print($"{Time[0]}  MAXLOSS BUY TO COVER {simStop.OrderRecordMasterLite.ExitOrderUUID} to close {simStop.OrderRecordMasterLite.EntryOrderUUID} at MAXLOSS ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit} < ${-maxLossShort}");
												SubmitOrderUnmanaged(instrumentSeriesIndex, OrderAction.BuyToCover, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0, simStop.OrderRecordMasterLite.EntryOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID + "_Exit_" + DateTime.Now.Ticks);
												simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
											}
		
										}
										
									}
								
								}
							}
							if(simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop == null)
							{
								
							}
						
						
						else if(simStop.OrderRecordMasterLite.ExitOrder != null)
						{
							//BackBrush = Brushes.Violet;	
						}
						else if(simStop.OrderRecordMasterLite.EntryOrder == null)
						{
							
							//BackBrush = Brushes.Red;	
						}
					}
			}
		}
	}
		
	protected void simulatedExitConditions()
	{
		maximumOpenInstrumentIndex = 1;
		///tick value for SIL
		
		string msg = "start";
		try
		{
			lastFunction = "simulatedExitConditions";
			DebugPrint(debugSection.ProcessAdjustments,"Begin simulatedExitConditions");
			/// scroll through sims
			foreach(simulatedStop simStop in MasterSimulatedStops) 
			{
				
				if(BarsInProgress == (simStop.OrderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex))
				{
				if(simStop.OrderRecordMasterLite != null && simStop.OrderRecordMasterLite.ExitOrder == null && simStop.OrderRecordMasterLite.EntryOrder != null)
					{
							OrderRecordMasterLite orderRecordMaster = simStop.OrderRecordMasterLite;
					
							//if(BarsInProgress == 0)
							//{
								//bool entrynull = simStop.OrderRecordMasterLite.EntryOrder == null;
								//Print($"simulatedExitConditions {simStop.EntryOrderUUID} isEntryNull {entrynull}, profit : {simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}");
							//}
							
							if(orderRecordMaster.EntryOrderUUID == simStop.EntryOrderUUID && simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady == true)
							{
								
								
								// Confirm matching record isn't yet filled
								if (orderRecordMaster.EntryOrder != null && 
								    orderRecordMaster.EntryOrder.OrderState == OrderState.Filled && 
								    (orderRecordMaster.EntryOrder.OrderAction == OrderAction.SellShort || orderRecordMaster.EntryOrder.OrderAction == OrderAction.Buy) && 
								    orderRecordMaster.ExitOrder == null)
								{
									
									int instrumentSeriesIndex = simStop.instrumentSeriesIndex;
									double tickValue = simStop.instrumentSeriesTickValue;
	
									maximumOpenInstrumentIndex = Math.Max(maximumOpenInstrumentIndex,instrumentSeriesIndex);
									
								
			           
								    double currentProfit = 	CalculateTotalOpenPositionProfitForInstrument(instrumentSeriesIndex);
								    double allTimeHighProfit = orderRecordMaster.PriceStats.OrderStatsAllTimeHighProfit;
									
									
								  	int indexQuantity = GetPositionCountByIndex(instrumentSeriesIndex);/// quantity across instrument size
								
							
									double softProfitTarget = softTakeProfitMult*orderRecordMaster.EntryOrder.Quantity;
								    double hardProfitTarget = instrumentSeriesIndex == 3 ? standardContractTakeProfit*orderRecordMaster.EntryOrder.Quantity : microContractTakeProfit*orderRecordMaster.EntryOrder.Quantity;
									
									//softProfitTarget *= ATR(100)[0];
									//hardProfitTarget *= ATR(100)[0];
									/// this version doesnt use age
								   	// bool softProfitPullbackTarget = (allTimeHighProfit > softProfitTarget && 
								    //                                 currentProfit < Math.Max(softProfitTarget, pullBackPct * allTimeHighProfit));
								    bool hardProfitTargetReached = (currentProfit > hardProfitTarget);
								    int age = CurrentBars[0] - orderRecordMaster.EntryBar;
								
								    FunctionResponses exitState = exitFunctionResponse();
									
									bool exitLong = orderRecordMaster.EntryOrder.IsLong && exitState == FunctionResponses.ExitAll;
								    bool exitShort = orderRecordMaster.EntryOrder.IsShort && exitState == FunctionResponses.ExitAll;
									
										
										
									bool isEMACrossed = orderRecordMaster.EntryOrder.IsLong && Low[0] < EMA3[0] ? true : ( orderRecordMaster.EntryOrder.IsShort ? High[0] > EMA3[0] : false);
									double decayRate = 0.001; // Adjust this rate to control how quickly the threshold decreases with age
									
									// Adjust pullBackPct based on age to make the pullback threshold less strict over time
							
									
									// Adjust pullBackPct based on age to make the pullback threshold less strict over time
									double adjustedPullBackPct = pullBackPct / (1 + age * decayRate);
									
									bool softProfitPullbackTarget = (allTimeHighProfit > softProfitTarget &&
																	//isEMACrossed == true &&
									                                //currentProfit > -softProfitTarget &&
																	(currentProfit < 0 || (currentProfit > 0 && currentProfit < Math.Max(softProfitTarget, pullBackPct * allTimeHighProfit))));
																	
									
								  
								
								    signalExitAction thisSignalExitAction = signalExitAction.NULL;
								
								    if (age == 2 && allTimeHighProfit < 0)
								    {
								        thisSignalExitAction = (simStop.EntryOrderAction == OrderAction.Buy) ? signalExitAction.AGEL : signalExitAction.AGES;
								    }
								    else if (hardProfitTargetReached && TakeBigProfitEnabled && simStop.isExitReady)/// dont do take for large movements
								    {
								        thisSignalExitAction = (orderRecordMaster.EntryOrder.IsLong) ? signalExitAction.TBPL : signalExitAction.TBPS;
								    }
								    else if (softProfitPullbackTarget && PullBackExitEnabled && simStop.isExitReady)
								    {
								        thisSignalExitAction = (orderRecordMaster.EntryOrder.IsLong) ? signalExitAction.PBL : signalExitAction.PBS;
										
								    }
								    else if (exitLong || exitShort)
								    {
								        thisSignalExitAction = (orderRecordMaster.EntryOrder.IsLong) ? signalExitAction.FUNCL : signalExitAction.FUNCS;
								    }
								 	
									
									
								    switch (thisSignalExitAction)
								    {
								        case signalExitAction.TBPL:
								        case signalExitAction.PBL:
								        case signalExitAction.FUNCL:
								        case signalExitAction.AGEL:
											
								            orderRecordMaster.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
								            orderRecordMaster.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
								            Print($"{Time[0]}  SELL {simStop.OrderRecordMasterLite.ExitOrderUUID} to Close {simStop.OrderRecordMasterLite.EntryOrderUUID} at {thisSignalExitAction} PROFIT ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit} ");
											SubmitOrderUnmanaged(instrumentSeriesIndex, OrderAction.Sell, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  simStop.OrderRecordMasterLite.EntryOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID + "_Exit_" + DateTime.Now.Ticks);
											simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
								       
								            MastersimulatedStopToDelete.Enqueue(simStop);
											
								            break;
								
								        case signalExitAction.TBPS:
								        case signalExitAction.PBS:
								        case signalExitAction.FUNCS:
								        case signalExitAction.AGES:
											
								            orderRecordMaster.OrderSupplementals.thisSignalExitAction = thisSignalExitAction;
								            orderRecordMaster.OrderSupplementals.ExitReason = "stop @" + Math.Round(GetCurrentBid(instrumentSeriesIndex));
											 Print($"{Time[0]} BUYTOCOVER {simStop.OrderRecordMasterLite.ExitOrderUUID} to Close {simStop.OrderRecordMasterLite.EntryOrderUUID} at {thisSignalExitAction} PROFIT ${simStop.OrderRecordMasterLite.PriceStats.OrderStatsProfit}  ");								
											SubmitOrderUnmanaged(instrumentSeriesIndex, OrderAction.BuyToCover, OrderType.Market, simStop.OrderRecordMasterLite.EntryOrder.Quantity, 0, 0,  simStop.OrderRecordMasterLite.EntryOrderUUID, simStop.OrderRecordMasterLite.EntryOrderUUID + "_Exit_" + DateTime.Now.Ticks);
								       
											simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
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
			
		}
		catch (Exception ex)
		{
		    Print("Error in updatePriceStats  " + ex.Message +" "+ msg);
		}
	}
		
	*/

	


	
	protected simulatedEntry getsimEntryFromEntryUUID(string entryUUID)
	{
	
		foreach(simulatedEntry simEntry in MasterSimulatedEntries) 
		{
	
			if(simEntry.EntryOrderUUID == entryUUID)
			{		
			
				return simEntry;
			}
		}
		return null;
	}
	
	protected simulatedStop getsimStopFromEntryUUID(string entryUUID)
	{
	
		foreach(simulatedStop simStop in MasterSimulatedStops) 
		{
	
			if(simStop.EntryOrderUUID == entryUUID)
			{
				return simStop;
			}
		}
		return null;
	}
	protected int countSimulatedEntriesReady()
	{
		int c = 0;
		foreach(simulatedEntry simEntry in MasterSimulatedEntries) 
		{
			if(simEntry.isEnterReady == true)
			{
				c++;
			}
		}
		
		return c;
	}
	protected void cancelPriorSimulatedOrders()
	{
		foreach(simulatedEntry simEntry in MasterSimulatedEntries) 
		{
			if(simEntry.isEnterReady == true)
			{
				
				simEntry.isEnterReady = false;
				MastersimulatedEntryToDelete.Enqueue(simEntry); /// also remove entry
			
			}
		}
	}			
		
	public bool IsPriceOutsideStdDev(double currentPrice, int period, double stdDevMultiplier)
	{
	    if (CurrentBar < period) return false; // Ensure enough data

	    // Manually calculate the mean of price changes
	    double sumPriceChanges = 0;
	    for (int i = 1; i <= period; i++)
	    {
	        sumPriceChanges += (Close[i] - Open[i]);
	    }
	    double meanPriceChange = sumPriceChanges / period;

	    // Manually calculate the variance of price changes
	    double sumSquaredDifferences = 0;
	    for (int i = 1; i <= period; i++)
	    {
	        double priceChange = (Close[i] - Open[i]);
	        sumSquaredDifferences += Math.Pow(priceChange - meanPriceChange, 2);
	    }
	    double variance = sumSquaredDifferences / period;
	    double stdDevPriceChange = Math.Sqrt(variance);

	    // Calculate the current price change
	    double currentPriceChange = currentPrice - Open[0];

	    // Calculate bounds based on the standard deviation multiplier
	    double upperBound = meanPriceChange + (stdDevPriceChange * stdDevMultiplier);
	    double lowerBound = meanPriceChange - (stdDevPriceChange * stdDevMultiplier);

	    // Check if the current price change is outside the bounds
	    return currentPriceChange > upperBound || currentPriceChange < lowerBound;
	}


	}
}
			

		
#endregion