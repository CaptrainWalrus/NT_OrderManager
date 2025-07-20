using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Net.Http;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    public partial class MainStrategy : Strategy
    {

/// <summary>
/// NONCRITICAL FILE
/// WHEN THIS STRATEGY IS DEPLOYED TO THE CHART, THIS FILE WITH LOAD A SMALL UI TO ASSIST WITH HUMAN-IN-THE-LOOP ENTRIES AND EXITS.
/// </summary>
/// <param name="entryOrderAction"></param>
/// <param name="isThisMarketPosition"></param>
/// <param name="isNotMarketPosition"></param>
		public void OnMyButtonClickActions(OrderAction entryOrderAction, MarketPosition? isThisMarketPosition, MarketPosition? isNotMarketPosition)
		{
		    Print($"Button clicked! Action: {entryOrderAction}, IsPosition: {isThisMarketPosition}, NotPosition: {isNotMarketPosition}");
		
		    // Handle Buy/Sell logic
		    if (entryOrderAction == OrderAction.Buy || entryOrderAction == OrderAction.SellShort)
		    {
		        // Pass to entry logic
		        OnMyButtonClickEntry(entryOrderAction, isThisMarketPosition, isNotMarketPosition);
		    }
		    else if (entryOrderAction == OrderAction.Sell || entryOrderAction == OrderAction.BuyToCover)
		    {
		        // Handle Exit logic
		        OnMyButtonClickExit();
		    }
		}
		
		public void OnMyButtonClickEntry(OrderAction entryOrderAction, MarketPosition? isThisMarketPosition, MarketPosition? isNotMarketPosition)
		{
	
			if (Instrument == null)
			{
			    Print("Instrument is not initialized.");
			    return;
			}
			
			
		    if (entryOrderAction == null || isThisMarketPosition == null || isNotMarketPosition == null)
		    {
		        Print("Invalid parameters passed to OnMyButtonClickEntry.");
		        return;
		    }
			Print($"OnMyButtonClickEntry {entryOrderAction} {isThisMarketPosition}");
			
					
			int totalAccountQuantity = getAllcustomPositionsCombined();
			Print($"OnMyButtonClickEntry 1");

			if(GetMarketPositionByIndex(BarsInProgress) != isNotMarketPosition)
			{
				if(totalAccountQuantity+strategyDefaultQuantity <= strategyMaxQuantity && totalAccountQuantity+strategyDefaultQuantity <= (accountMaxQuantity) && getAllcustomPositionsCombined() < strategyMaxQuantity) /// eg mcl = 1, sil = 1 , and we're considering mcg 2.  if 2 is less than 5 do something.
				{
				
						signalReturnActionType srt = entryOrderAction == OrderAction.Buy ? signalReturnActionType.Bullish : signalReturnActionType.Bearish;

						signalPackage btnSignalPackage = new signalPackage
						{
		
						SignalReturnAction = new signalReturnAction("Manual", srt),
			            Sentiment = srt,
						instrumentSeriesIndex = 0,
						instrumentSeriesTickValue = instrumentSeriesMicroTickValue,
						price = GetCurrentBid(0)
								
						};
						Print($"OnMyButtonClickEntry 4");
				
						patternFunctionResponse thisSignal = new patternFunctionResponse();
						thisSignal.newSignal = FunctionResponses.NoAction;
					    thisSignal.patternSubType = "noneManual";
						thisSignal.patternId = "";
						thisSignal.recStop = 1;
						thisSignal.recPullback = 1;
					
							if (GetMarketPositionByIndex(BarsInProgress) == isThisMarketPosition)
                        	{ 
								Print($"OnMyButtonClickEntry 5");
								Print(Time[0] + " MANUAL LONG FROM LONG");
								EntryLimitFunctionLite(1, entryOrderAction, btnSignalPackage, "", CurrentBars[0], mainEntryOrderType,thisSignal); 
                                return;
									
							}
							else if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Flat)
							{
								Print(Time[0] + " MANUAL LONG FROM FLAT");
								
								
                                EntryLimitFunctionLite(1, entryOrderAction, btnSignalPackage, "", CurrentBars[0], mainEntryOrderType,thisSignal); 
                                return;
							}
							else
							{
								Print(Time[0] + " MANUAL LONG WAS REJECTED");
							}
							
                        
						
							
				}
			}
		  
		 
			
		}

		public void OnMyButtonClickExit()
		{
			Print($"OnMyButtonClickExit > ExitActiveOrders");
			ExitActiveOrders(ExitOrderType.Manual,signalExitAction.MAN,false);
			
			
		}
		

	}
}



