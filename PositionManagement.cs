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
										if(simEntry.EntryOrderAction == OrderAction.Buy && GetMarketPositionByIndex(0) != MarketPosition.Short && GetMarketPositionByIndex(1) != MarketPosition.Short)
										{
											tryCatchSelection = "simulatedEntryConditions 2a ";
												
												
											
									  			int Q = getAllcustomPositionsCombined();

												simEntry.isEnterReady = false;
												Print($"ENTRY {simEntry.EntryOrderUUID} LONG, >>> exit will be {simEntry.ExitOrderUUID}");
												
												/// set stop in Rithmic if we are concerned about NT stalling	
												SetStopLoss(simEntry.EntryOrderUUID,CalculationMode.Currency,microContractStoploss,false);
												
												if (selectedBroker == brokerSelection.BlueSky_projectx)
												{
													_ = Task.Run(() => ExecuteProjectXEntryLong(simEntry.quantity, simEntry.EntryOrderUUID));
												}
												else
												{
													EnterLong(strategyDefaultQuantity,simEntry.quantity,simEntry.EntryOrderUUID);
												}
												continue;
												
										}
										else if(simEntry.EntryOrderAction == OrderAction.SellShort && GetMarketPositionByIndex(0) != MarketPosition.Long && GetMarketPositionByIndex(1) != MarketPosition.Long)
										{
												tryCatchSelection = "simulatedEntryConditions b";
												Print($"ENTRY {simEntry.EntryOrderUUID} SHORT, >>> exit will be {simEntry.ExitOrderUUID}");
												simEntry.isEnterReady = false;
											
												//Print($"SubmitOrderUnmanaged : openOrderTest {openOrderTest}");
												//Print($"BarsInProgress {BarsInProgress} BAR:{CurrentBars[BarsInProgress]} TIME: {Time[0]} SubmitOrderUnmanaged!  {simEntry.EntryOrderUUID}, Quantity {Q} EnterShort {GetMarketPositionByIndex(BarsInProgress)}");
												/// set stop in Rithmic if we are concerned about NT stalling	
												SetStopLoss(simEntry.EntryOrderUUID,CalculationMode.Currency,microContractStoploss,false);
												
												if (selectedBroker == brokerSelection.BlueSky_projectx)
												{
													_ = Task.Run(() => ExecuteProjectXEntryShort(simEntry.quantity, simEntry.EntryOrderUUID));
												}
												else
												{
													EnterShort(strategyDefaultQuantity,simEntry.quantity,simEntry.EntryOrderUUID);
												}
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