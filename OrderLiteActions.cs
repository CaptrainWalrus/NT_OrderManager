#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
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
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    public partial class MainStrategy : Strategy
    {
		private List<OrderRecordMasterLite> openOrders = new List<OrderRecordMasterLite>();

      	protected void EntryLimitFunctionLite(int accountEntryQuantity,OrderAction OA, signalPackage signalPackageParam,string appendSignal,bool isOverleveraged,bool canScaleIn,bool isForcedEntryVal,OrderType orderType)
		{	 
		
		
		string tryCatchSection = "Begin Order Management Lite "+OA+" "+signalPackageParam.SignalReturnAction;
	
		try
		{
		//Print(tryCatchSection);
		
		// Usage in your logic
		/// eg instrument 3L
		if (PositionAccount.Quantity == accountMaxQuantity) /// NEVER go above MAX-1 because we still need 1 to exit! (3L+4L) > 4 Yes
		{
		    Print("Order block in position check 2");
			return;
		}
	
		 tryCatchSection = "Section 3 Order Management Lite "+OA+" "+signalPackageParam.SignalReturnAction;
			
		if(signalPackageParam.SignalReturnAction.SignalName != signalReturnActionEnum.noAction.ToString() && signalPackageParam.SignalReturnAction.SignalName != signalReturnActionEnum.exitAction.ToString() )						
		{
				
				
				
					
			
				lock (eventLock)
				{
					
					/// map out all components all at once, then later fill in a few parts like the order objects, and toggle entry/exit stops as needed
					tryCatchSection = "Section 3a Order Management Lite "+OA+" "+signalPackageParam.SignalReturnAction;
					DebugPrint(debugSection.EntryLimitFunction," L 9");
					string uuid = GenerateSignalId();
					string entryUUID =  uuid+"_Entry";
					string exitUUID = uuid+"_Exit";
					

					//Print($"BarsInProgress {BarsInProgress} BAR: {CurrentBars[BarsInProgress]}  TIME{Time[0]}, EntryLimitFunctionLite entryUUID created {entryUUID}");
			
					tryCatchSection = "Section 3b Order Management Lite ";
					
					OrderSupplementals orderSupplementals = new OrderSupplementals()
					{
						SimulatedStop = null,
						SimulatedEntry = null,
						forceExit = false,
						thisSignalExitAction = signalExitAction.NA,
						sourceSignalPackage = signalPackageParam,
			
					};
					tryCatchSection = "Section 3c Order Management Lite ";
					OrderPriceStats orderPriceStats = new OrderPriceStats()
					{
						OrderStatsEntryPrice = signalPackageParam.price,
						OrderStatsExitPrice = 0,
						OrderStatsProfit = 0,
						OrderStatsHardProfitTarget = 0,/// Profit is scalable so dont let to overscale
						OrderStatsAllTimeHighProfit = 0,
						OrderStatsAllTimeLowProfit = 0,
						OrderStatspullBackThreshold = 0,
						OrderStatspullBackPct = pullBackPct,
						OrderMaxLoss = 0,/// loss is scalable so dont let to overscale
					};
					tryCatchSection = "Section 3d Order Management Lite ";
					
					ExitFunctions exitFunctions = new ExitFunctions();
					
					
					OrderRecordMasterLite orderRecordMasterLite = new OrderRecordMasterLite()
					{
						EntrySignalReturnAction = signalPackageParam.SignalReturnAction,
						EntryOrderUUID = entryUUID,
						EntryOrder = null,
						ExitOrder = null,
						ExitOrderUUID = exitUUID,
						PriceStats = orderPriceStats,
						OrderSupplementals = orderSupplementals,
						ExitFunctions = exitFunctions,
						SignalContextId = signalPackageParam.SignalContextId
					};
					tryCatchSection = "Section 3e Order Management Lite ";
					simulatedEntry simulatedEntryAction = new simulatedEntry
					{
						 EntryOrderUUID = entryUUID,
						 ExitOrderUUID = exitUUID,
						 EntryOrderAction = signalPackageParam.SignalReturnAction.Sentiment == signalReturnActionType.Bullish ? OrderAction.Buy : OrderAction.SellShort,
						 quantity = accountEntryQuantity,
						 isEnterReady = true,
						 EntryBar = CurrentBars[0],
						 EntrySignalReturnAction =  new signalReturnAction(signalPackageParam.SignalReturnAction.SignalName,signalReturnActionType.Bullish), // Use the SignalId directly,
						 EntryOrderType = mainEntryOrderType,
						// stopPrice = signalPackageParam.SignalReturnAction.Sentiment == signalReturnActionType.Bullish ? orderFlowVWAP.StdDev3Upper[0] + (TickSize*3) : orderFlowVWAP.StdDev3Lower[0] - (TickSize*3),
						 OrderRecordMasterLite = orderRecordMasterLite,
						 EMATrack = EMA3,
						 instrumentSeriesIndex = signalPackageParam.instrumentSeriesIndex,
						 instrumentSeriesTickValue = signalPackageParam.instrumentSeriesTickValue
					};
					
					
					
					tryCatchSection = "Section 3f Order Management Lite ";
					simulatedStop simulatedExitAction = new simulatedStop
					{
						 EntryOrderUUID = entryUUID,
						 ExitOrderUUID = exitUUID,
						 EntryOrderAction = signalPackageParam.SignalReturnAction.Sentiment == signalReturnActionType.Bullish ? OrderAction.Buy : OrderAction.SellShort,
						 quantity = accountEntryQuantity,
						 isExitReady = false,
						 OrderRecordMasterLite = orderRecordMasterLite,
						 instrumentSeriesIndex = signalPackageParam.instrumentSeriesIndex,
						 instrumentSeriesTickValue = signalPackageParam.instrumentSeriesTickValue
					};
						
					
				

					tryCatchSection = "Section 3g Order Management Lite ";
					/// add simulatedEntryAction to a new list
					
					orderSupplementals.SimulatedEntry = simulatedEntryAction; 
					orderSupplementals.SimulatedStop = simulatedExitAction;
					tryCatchSection = "Section 3h Order Management Lite ";
					
					MasterSimulatedEntries.Add(orderSupplementals.SimulatedEntry);
					MasterSimulatedStops.Add(orderSupplementals.SimulatedStop);
					
					
					if(!MasterSimulatedStops.Contains(orderSupplementals.SimulatedStop))
					{
						Print("Added Stop was not added!");
					}
					if(!MasterSimulatedEntries.Contains(orderSupplementals.SimulatedEntry))
					{
						Print("Added Entry was not added!");
					}
					
					tryCatchSection = "Section 3i Order Management Lite ";
					LiteMasterRecords.Add(orderRecordMasterLite);
					tryCatchSection = "Section 3j Order Management Lite ";
					
				
					OrderRecordMasterLiteEntrySignals[entryUUID] = orderRecordMasterLite;

					//Print("Storing "+entryUUID);
					if(OrderRecordMasterLiteEntrySignals.ContainsKey(entryUUID))
					{
						//Print("OrderRecordMasterLiteEntrySignals EntryUUID stored: "+entryUUID);
						
					}
					
					if(MasterSimulatedEntries.Contains(orderSupplementals.SimulatedEntry))
					{
						//Print("MasterSimulatedEntries EntryUUID stored: "+orderSupplementals.SimulatedEntry.EntryOrderUUID);
						
					}
					
					
					
					//Print("Storing "+exitUUID);
					
					OrderRecordMasterLiteExitSignals[exitUUID] = orderRecordMasterLite;
					tryCatchSection = "Section 5 Order Management Lite ";
					
					//simulatedEntryConditions();
				
					signalQueue.Clear();
				}
					
			}
			
		
		
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch OrderLite Section: "+tryCatchSection+" - Exception error; "+ex);
		
		}
			
	}
		
	private string NormalizeUUID(string orderName, bool toEntry)
	{
		
	    if (toEntry && !orderName.EndsWith("_Entry"))
	        return orderName + "_Entry";
	
	    if (!toEntry && !orderName.EndsWith("_Exit"))
	        return orderName + "_Exit";
	
	    return orderName; // Already normalized
		

	}
		


        
    }
}
