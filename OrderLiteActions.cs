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

      	protected void EntryLimitFunctionLite(int accountEntryQuantity,OrderAction OA, signalPackage signalPackageParam,string overWriteSignal,int thisBar,OrderType orderType,patternFunctionResponse builtSignal)
		{	 
		
		string subTypeParam = builtSignal.patternSubType;
		string patternIdString = builtSignal.patternId;
		double recMaxLoss = builtSignal.recStop;
		int recQty = builtSignal.recQty;
		double recPullback = builtSignal.recPullback;
		double recTarget = builtSignal.recTarget;
		string tryCatchSection = "Begin Order Management Lite "+OA+" "+signalPackageParam.SignalReturnAction;
	 	Print($" EntryLimitFunctionLite recMaxLoss {recMaxLoss} recTarget {recTarget}");
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
					
					// Store features for this position if they exist
					Print($"[FEATURE-STORAGE] Checking for pending features with patternId: {patternIdString}");
					if (HasPendingFeatures(patternIdString))
					{
						var pendingFeatures = GetPendingFeatures(patternIdString);
						positionFeatures[entryUUID] = pendingFeatures;
						RemovePendingFeatures(patternIdString);
						Print($"[FEATURE-STORAGE] Stored features for position {entryUUID} (from patternId: {patternIdString})");
						Print($"[FEATURE-STORAGE] Feature count: {pendingFeatures?.Features?.Count ?? 0}");
					}
					else
					{
						Print($"[FEATURE-STORAGE] No pending features found for patternId: {patternIdString}");
					}

					//Print($"BarsInProgress {BarsInProgress} BAR: {CurrentBars[BarsInProgress]}  TIME{Time[0]}, EntryLimitFunctionLite entryUUID created {entryUUID}");
			
					tryCatchSection = "Section 3b Order Management Lite ";
					
					OrderSupplementals orderSupplementals = new OrderSupplementals()
					{
						SimulatedStop = null,
						SimulatedEntry = null,
						forceExit = false,
						thisSignalExitAction = signalExitAction.NA,
						sourceSignalPackage = signalPackageParam,
						patternSubtype = subTypeParam,
						patternId = patternIdString,
						pullbackModifier = 1,
						stopModifier = 1,
			
					};
					tryCatchSection = "Section 3c Order Management Lite ";
					OrderPriceStats orderPriceStats = new OrderPriceStats()
					{
						OrderStatsEntryPrice = signalPackageParam.price,
						OrderStatsExitPrice = 0,
						OrderStatsProfit = 0,
						OrderStatsHardProfitTarget = recTarget != 0 ? recTarget : 999,/// Profit is scalable so dont let to overscale
						OrderStatsAllTimeHighProfit = 0,
						OrderStatsAllTimeLowProfit = 0,
						OrderStatspullBackThreshold = recPullback > softTakeProfitMult ? recPullback : softTakeProfitMult,
						OrderStatspullBackPct =  pullBackPct,
						OrderMaxLoss = recMaxLoss != 0 ? recMaxLoss : 666,/// loss is scalable so dont let to overscale
						profitByBar = new Dictionary<int,double>()
					};
					tryCatchSection = "Section 3d Order Management Lite ";
					
					ExitFunctions exitFunctions = new ExitFunctions();
					
					
					OrderRecordMasterLite orderRecordMasterLite = new OrderRecordMasterLite()
					{
						EntrySignalReturnAction = signalPackageParam.SignalReturnAction,
						EntryOrderUUID = entryUUID,
						EntryOrder = null,
						ExitOrder = null,
						EntryBar = thisBar,
						ExitOrderUUID = exitUUID,
						PriceStats = orderPriceStats,
						OrderSupplementals = orderSupplementals,
						ExitFunctions = exitFunctions,
						SignalContextId = signalPackageParam.SignalContextId,
						builtSignal = builtSignal
					};
					tryCatchSection = "Section 3e Order Management Lite ";
					simulatedEntry simulatedEntryAction = new simulatedEntry
					{
						 EntryOrderUUID = entryUUID,
						 ExitOrderUUID = exitUUID,
						 EntryOrderAction = signalPackageParam.SignalReturnAction.Sentiment == signalReturnActionType.Bullish ? OrderAction.Buy : OrderAction.SellShort,
						 quantity = recQty > 0 ? recQty : accountEntryQuantity,
						 isEnterReady = true,
						 EntryBar = thisBar,
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
						 quantity = recQty > 0 ? recQty : accountEntryQuantity,
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
					
					//MasterSimulatedEntries.Add(orderSupplementals.SimulatedEntry);
					MasterSimulatedStops.Add(orderSupplementals.SimulatedStop);
					
					
					if(!MasterSimulatedStops.Contains(orderSupplementals.SimulatedStop))
					{
						Print("Added Stop was not added!");
					}
				//	if(!MasterSimulatedEntries.Contains(orderSupplementals.SimulatedEntry))
				//	{
				//		Print("Added Entry was not added!");
				//	}
					
					tryCatchSection = "Section 3i Order Management Lite ";
					LiteMasterRecords.Add(orderRecordMasterLite);
					tryCatchSection = "Section 3j Order Management Lite ";
					
				
					OrderRecordMasterLiteEntrySignals[entryUUID] = orderRecordMasterLite;
					
					vwapStop thisVwapStop = new vwapStop();
					thisVwapStop.EMAValue = EMA3;
					thisVwapStop.VWAPValue = VWAP1;
					thisVwapStop.BBValue = BB0;
					thisVwapStop.exitOrderUUID = orderRecordMasterLite.ExitOrderUUID;
					thisVwapStop.entryOrderUUID = orderRecordMasterLite.EntryOrderUUID;
					thisVwapStop.isLong = OA == OrderAction.Buy ? true  :false;
					
					vwapStopMapping[thisVwapStop] = orderRecordMasterLite;
					
					//Print("Storing "+exitUUID);
					
					OrderRecordMasterLiteExitSignals[exitUUID] = orderRecordMasterLite;
					tryCatchSection = "Section 5 Order Management Lite ";
					
					//simulatedEntryConditions();
				
					
					
					if(OA == OrderAction.Buy && GetMarketPositionByIndex(0) != MarketPosition.Short)
					{
						if(selectedBroker == brokerSelection.BlueSky_projectx && !IsInStrategyAnalyzer && isRealTime == true)
						{
							_ = Task.Run(() => ExecuteProjectXEntryLong(1, entryUUID));
						}
						else
						{
							EnterLong(1,1,entryUUID);
							Print($"enterlong {entryUUID}");
						}
						return;
					}
					else if(OA == OrderAction.SellShort && GetMarketPositionByIndex(0) != MarketPosition.Long)
					{
						if(selectedBroker == brokerSelection.BlueSky_projectx && !IsInStrategyAnalyzer && isRealTime == true)
						{
							_ = Task.Run(() => ExecuteProjectXEntryShort(1, entryUUID));
						}
						else
						{
							EnterShort(1,1,entryUUID);
							Print($"entershort {entryUUID}");
						}
						return;
					}
					else
					{
						Print($"ERROR {OA} && {GetMarketPositionByIndex(0)} && {GetMarketPositionByIndex(1)} {entryUUID}");
					}
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
