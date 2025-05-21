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
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Text.RegularExpressions;


#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	public partial class MainStrategy : Strategy
	{
	List<LosingTrade> losingTrades = new List<LosingTrade>();
	private PerformanceTracker performanceTracker = new PerformanceTracker();
	private readonly object eventLock = new object();

	protected double bullishness;

	protected double bearishness;
	private bool isPositionActive = false;

	private Order BuyOrder;
	private Order SellOrder;
	private Order SellShortOrder;
	private Order BuyToCoverOrder;
 	private Dictionary<string, (OrderUpdateInfo? OrderInfo, ExecutionUpdateInfo? ExecInfo)> pairedEvents = new Dictionary<string, (OrderUpdateInfo? OrderInfo, ExecutionUpdateInfo? ExecInfo)>();
	private HashSet<string> processedExitOrders = new HashSet<string>();


	public class ExecutionUpdateInfo
	{
	    public Order ExecutionOrder { get; set; }
	    public string ExecutionId { get; set; }
	    public double Price { get; set; }
	    public int Quantity { get; set; }
	    public MarketPosition MarketPosition { get; set; }
	    public string OrderId { get; set; }
	    public DateTime Time { get; set; }
		public string fromEntrySignal { get; set; }
	
	}
	
	public class OrderUpdateInfo
	{
	    public Order Order { get; set; }
	    public double LimitPrice { get; set; }
	    public double StopPrice { get; set; }
	    public int Quantity { get; set; }
	    public int Filled { get; set; }
	    public double AverageFillPrice { get; set; }
	    public OrderState OrderState { get; set; }
	    public DateTime Time { get; set; }
	    public ErrorCode Error { get; set; }
	    public string NativeError { get; set; }
	}

    	// Queues to store the eventsKey
    private Queue<ExecutionUpdateInfo> executionQueue = new Queue<ExecutionUpdateInfo>();
    private Queue<OrderUpdateInfo> orderQueue = new Queue<OrderUpdateInfo>();

	// In your strategy class
	private Dictionary<string, bool> registeredPositions = new Dictionary<string, bool>();
	/// debugging prints

	private bool debugOrderPrints = false;
	
  	protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
	{
		myStrategyControlPane.updateStates();
		   // Print(CurrentBars[0]+" "+Time[0]+" OnExecutionUpdate 1");
		if (selectedBroker == brokerSelection.Topstep && execution.Order.OrderState == OrderState.Filled)
	    {
			ExecutionUpdateInfo executionUpdateInfo = new ExecutionUpdateInfo
		    {
		        ExecutionOrder = execution.Order,
		        ExecutionId = executionId,
		        Price = price,
		        Quantity = quantity,
		        MarketPosition = marketPosition,
		        OrderId = orderId,
		        Time = time,
		        fromEntrySignal = execution.Order.FromEntrySignal
		    };
		    // Queue the execution event
		   // executionQueue.Enqueue(executionUpdateInfo);
			EnqueueExecutionUpdate(executionUpdateInfo);
	       // OnRithmicExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);
			
        	  //Print($"BAR: {CurrentBars[0]}, TIME: {Time[0]} OnExecutionUpdate Process Queued for key {execution.Order.Name}");
			  ProcessQueuedEvents();
	       
	    }
		else if(selectedBroker == brokerSelection.NinjaTrader)
		{

			OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);
		}
		
	 
	}

	
    protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
	{
		
		myStrategyControlPane.updateStates();

		if (selectedBroker == brokerSelection.Topstep && order.OrderState == OrderState.Filled)
	    {
			/// Log the broker-specific condition
			
	       if(debugOrderPrints) Print($"{order.Name} brokerSelection.Topstep, Queueing order {orderState}-{order.OrderAction} for synchronized processing.");
	        
	        /// Queue the order event
//	        orderQueue.Enqueue(new OrderUpdateInfo

			OrderUpdateInfo orderUpdateInfo = new OrderUpdateInfo
	        {
	            Order = order,
	            LimitPrice = limitPrice,
	            StopPrice = stopPrice,
	            Quantity = quantity,
	            Filled = filled,
	            AverageFillPrice = averageFillPrice,
	            OrderState = orderState,
	            Time = time,
	            Error = error,
	            NativeError = nativeError
	        };
			
			EnqueueOrderUpdate(orderUpdateInfo);
			ProcessQueuedEvents();
	       
	    }
		else if(selectedBroker == brokerSelection.NinjaTrader && order.OrderState == OrderState.Filled)
		{
		   
			///Print(order.Name+" "+CurrentBars[0]+" "+Time[0]+" Process Non-Topstep OnOrderUpdate " + order.OrderState);
	        // Process these states immediately, no need to queue
	        customOnOrderUpdate(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, error, nativeError);
		   
		}
	}

	

	private void ProcessQueuedEvents()
{
    // Check for matching pairs in the dictionary
    foreach (var key in pairedEvents.Keys.ToList())
    {
        var (orderInfo, execInfo) = pairedEvents[key];

        // Process the pair only if both are available
        if (orderInfo != null && execInfo != null)
        {
			// Remove the processed pair from the dictionary
           
            lock (eventLock)
            {
				if(debugOrderPrints) Print($"BAR: {CurrentBars[0]}, TIME: {Times[BarsInProgress][0]}********ProcessQueuedEvents for key {key}. OrderInfo: {orderInfo != null}, ExecInfo: {execInfo != null} CONTINUE");
				
                // Process the OrderUpdateInfo
                customOnOrderUpdate(
                    orderInfo.Order,
                    orderInfo.LimitPrice,
                    orderInfo.StopPrice,
                    orderInfo.Quantity,
                    orderInfo.Filled,
                    orderInfo.AverageFillPrice,
                    orderInfo.OrderState,
                    orderInfo.Time,
                    orderInfo.Error,
                    orderInfo.NativeError
                );
				//Print($"{execInfo.ExecutionOrder.Name} BAr {CurrentBars[0]} {Time[0]} OnExecutionUpdate GO");

                // Process the ExecutionUpdateInfo
                customOnExecutionUpdate(
                    execInfo.ExecutionOrder,
                    execInfo.OrderId,
                    execInfo.Price,
                    execInfo.Quantity,
                    execInfo.MarketPosition,
                    execInfo.OrderId,
                    execInfo.Time,
                    execInfo.fromEntrySignal
                );
				
            }
			 pairedEvents.Remove(key);

            
        }
		else
        {
          // Print($"Incomplete pair for key {key}. OrderInfo: {orderInfo != null}, ExecInfo: {execInfo != null}");
        }
    }
}

	// Enqueue OrderUpdateInfo and ExecutionUpdateInfo
	private void EnqueueOrderUpdate(OrderUpdateInfo orderUpdateInfo)
	{
	    string key = orderUpdateInfo.Order.Name;
	
	    if (pairedEvents.ContainsKey(key) && pairedEvents[key].OrderInfo != null)
	    {
	        if(debugOrderPrints) Print($"Duplicate OrderUpdate for {key}. Skipping.");
	        return; // Avoid requeueing
	    }
	
	    if (!pairedEvents.ContainsKey(key))
	    {
	        pairedEvents[key] = (null, null);
	    }
	    pairedEvents[key] = (orderUpdateInfo, pairedEvents[key].ExecInfo);
	    if(debugOrderPrints) Print($"EnqueueOrderUpdate for {key}");
	}
	
	private void EnqueueExecutionUpdate(ExecutionUpdateInfo executionUpdateInfo)
	{
	    string key = executionUpdateInfo.ExecutionOrder.Name;
	
	    if (pairedEvents.ContainsKey(key) && pairedEvents[key].ExecInfo != null)
	    {
	        if(debugOrderPrints) Print($"Duplicate ExecutionUpdate for {key}. Skipping.");
	        return; // Avoid requeueing
	    }
	
	    if (!pairedEvents.ContainsKey(key))
	    {
	        pairedEvents[key] = (null, null);
	    }
	    pairedEvents[key] = (pairedEvents[key].OrderInfo, executionUpdateInfo);
	    if(debugOrderPrints) Print($"EnqueueExecutionUpdate for {key}");
	}



	/// not to use an execution object in OnOrderUpdate(). 							
	protected void customOnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
	{
			myStrategyControlPane.updateStates();
			string uuid_entry = order.Name;
			string uuid_exit = order.Name;
			if (orderState == OrderState.Rejected)
		    {
		        Print($"Order Rejected: {order.Name}, Error: {error}, NativeError: {nativeError}");
		    }
		   
			string tryCatchSection = "customOnOrderUpdate Begin";
			try
			{
				if ((order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort) && order.OrderState == OrderState.Filled)
				{
					openOrderTest++;
					NinjaTrader.Code.Output.Process($"openOrderTest incrment {openOrderTest},{order.Name}", PrintTo.OutputTab2);
					if(debugOrderPrints) Print($" RECIEVED customOnOrderUpdate for {order.Name} , {order.OrderAction}-{order.OrderState}");
					
					tryCatchSection = "customOnOrderUpdate 1";
				    ///ENTRY PATH
					if(order.Name.EndsWith("_Entry"))
					{
					
						tryCatchSection = "customOnOrderUpdate 2";
						if(OrderRecordMasterLiteEntrySignals.ContainsKey(uuid_entry))
						{
							tryCatchSection = "customOnOrderUpdate 4";
							OrderRecordMasterLite orderRecordMasterLite = OrderRecordMasterLiteEntrySignals[uuid_entry];
							{
								tryCatchSection = "customOnOrderUpdate 4";
								if(orderRecordMasterLite.EntryOrder == null)
								{
									tryCatchSection = "customOnOrderUpdate 5";
									if(debugOrderPrints) Print($"{order.OrderAction} ORDER IS ASSIGNED TO ENTRY ORDER");
									orderRecordMasterLite.EntryOrder = order;
									MasterSimulatedEntries.Remove(orderRecordMasterLite.OrderSupplementals.SimulatedEntry);
									customPosition.OnOrderFilled(orderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex, order.OrderAction, quantity, averageFillPrice);
									
									string direction = order.OrderAction == OrderAction.Buy ? "long" : "short";
									///register
									orderRecordMasterLite.OrderSupplementals.isEntryRegisteredDTW = curvesService.RegisterPosition(orderRecordMasterLite.EntryOrderUUID,orderRecordMasterLite.OrderSupplementals.patternId, orderRecordMasterLite.EntryOrder.Instrument.FullName.Split(' ')[0],time);
							
									
								}
							}
						}
						if (!OrderRecordMasterLiteEntrySignals.ContainsKey(uuid_entry))
						{
							tryCatchSection = "customOnOrderUpdate 6";
						    if(debugOrderPrints) Print($"Warning: UUID {uuid_entry} not found in OrderRecordMasterLiteEntrySignals.");
						    return; // Skip further processing
						}
					}			
					else
					{
						if(debugOrderPrints) Print($"ENTRY CONFLICT? {order.Name} != {uuid_entry}");
						tryCatchSection = "customOnOrderUpdate 7";
						/// OTHER TYPES OF ORDER NAME
						if(debugOrderPrints) Print($" MUST DO HandleEntryOrder for uuid_entry > {uuid_entry}");
						HandleEntryOrder(order,uuid_entry,"customOnOrderUpdate "+order.OrderAction);		
					}
				}
				else if ((order.OrderAction == OrderAction.Sell || order.OrderAction == OrderAction.BuyToCover) && order.OrderState == OrderState.Filled)
				{
				   		// In your exit order processing:
						if (!processedExitOrders.Contains(order.Name))
						{
						    openOrderTest--;
						    processedExitOrders.Add(order.Name);
						}
						NinjaTrader.Code.Output.Process($"openOrderTest decrment {openOrderTest},{order.Name}", PrintTo.OutputTab2);
					 // Extra safety check - recreate counter if needed
					
		              	if(debugOrderPrints) Print($" RECIEVED customOnOrderUpdate for {order.Name} , {order.OrderAction}-{order.OrderState}");

						
						tryCatchSection = "customOnOrderUpdate 8";
						/// ********assign the Exit Order
						if(order.Name.EndsWith("_Exit"))
						{
							if(debugOrderPrints) Print($"{order.Name} ENDS WITH EXIT!");
							tryCatchSection = "customOnOrderUpdate 9";
							if(OrderRecordMasterLiteExitSignals.ContainsKey(uuid_exit))
							{
								if(debugOrderPrints) Print($"{order.Name} ContainsKey");
								tryCatchSection = "customOnOrderUpdate 10";
								
								OrderRecordMasterLite orderRecordMasterLite = OrderRecordMasterLiteExitSignals[uuid_exit];
								
									tryCatchSection = "customOnOrderUpdate 11";
									if(orderRecordMasterLite.ExitOrder != null)
									{
										if(debugOrderPrints) Print($"{order.Name} ExitOrder ISN'T NULL! Skip!");
										return;
									}
									if(orderRecordMasterLite.ExitOrder == null)
									{
									
										if(debugOrderPrints) Print($"{order.Name} ORDER IS ASSIGNED TO EXIT ORDER");

										orderRecordMasterLite.ExitOrder = order;
										orderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
										MasterSimulatedStops.Remove(orderRecordMasterLite.OrderSupplementals.SimulatedStop);
										customPosition.OnOrderFilled(orderRecordMasterLite.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex, order.OrderAction, quantity, averageFillPrice);


										
									}
								
							}
							if (!OrderRecordMasterLiteExitSignals.ContainsKey(uuid_exit))
							{
								tryCatchSection = "customOnOrderUpdate 13";
							    if(debugOrderPrints) Print($"Warning: UUID {uuid_exit} not found in OrderRecordMasterLiteExitSignals.");
							    return; // Skip further processing
							}
						}
						/// ******** EOSC, CLOSE POSITION ETC
						else
						{
							
							tryCatchSection = "customOnOrderUpdate 14";
							if(debugOrderPrints) Print($" MUST DO HandleExitOrder for uuid_exit > {uuid_exit}");
							HandleExitOrder(order,uuid_exit,"customOnOrderUpdate "+order.OrderAction);
						}
						
					
				}
				///*****************************REJECTED
				else if (order.OrderState == OrderState.Rejected)
			   {
					tryCatchSection = "customOnOrderUpdate 16";
				     if(debugOrderPrints) Print($"Order rejected: {order}. Order Action {order.OrderAction} ,  Native error: {nativeError}");
			        
			
			        string[] patterns = {
			            @"Total buy quantity of account.*exceed.*limit",
			            @"Order size exceeds.*limits",
			            @"Margin call.*insufficient funds"
			        };
			
			        foreach (var pattern in patterns)
			        {
			            if (Regex.IsMatch(nativeError, pattern, RegexOptions.IgnoreCase))
			            {
			               if(debugOrderPrints) Print($"Native error detected: {nativeError}");
			                Log($"Native error detected: {nativeError}", LogLevel.Error);
			                return; // Gracefully handle and continue
			            }
			        }
			    }
			   
			   
				
			}
			catch (Exception ex)
			{
			    // Handle the exception
			    Print("tryCatch Section: "+tryCatchSection+" - Exception error; "+ex);
				CloseAndStopStrategy("Error in customOnOrderUpdate: " + ex.Message);
	
		
			}

	}
	/// not to use an order object in OnExecutionUpdate() 
	protected void customOnExecutionUpdate(Order executionOrder, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time, string fromEntrySignal)
	{
		myStrategyControlPane.updateStates();
		string uuid_entry = executionOrder.Name;
		string uuid_exit = executionOrder.Name;
		string tryCatchSection = "customOnExecutionUpdate Begin";
		///*********ENTRY PATH
		try
		{
		 
			if (executionOrder.OrderAction == OrderAction.Buy || executionOrder.OrderAction == OrderAction.SellShort)
			{
				if(debugOrderPrints) Print($" {Time[0]} customOnExecutionUpdate RECIEVED {executionOrder.OrderAction} for {executionOrder.Name} , {executionOrder.OrderAction}-{executionOrder.OrderState}");

				
				if(executionOrder != null && executionOrder.OrderState == OrderState.Cancelled)
		        {
					OrderRecordMasterLite orderRecordMaster = OrderRecordMasterLiteEntrySignals[uuid_entry];
					if(orderRecordMaster != null)
					{	
						/// dont let it enter
						if(debugOrderPrints) Print($" {Time[0]} {executionOrder.Name} was cancelled?");
						orderRecordMaster.OrderSupplementals.SimulatedEntry = null;
						return;
						
					}
	
				}
				if(executionOrder != null && (executionOrder.OrderState == OrderState.PartFilled || executionOrder.OrderState == OrderState.Filled))
		        {
					
					
					tryCatchSection = "ENTERING ORDERS";
					OrderRecordMasterLite orderRecordMaster = OrderRecordMasterLiteEntrySignals[uuid_entry];
					
					if(debugOrderPrints) Print($" ORDER FILLED: exec name {executionOrder.Name} , ORM name {orderRecordMaster.EntryOrderUUID} STOP Name {orderRecordMaster.OrderSupplementals.SimulatedStop.ExitOrderUUID}, TIME:{Time[0]}");
					
					if(!MasterSimulatedStops.Contains(orderRecordMaster.OrderSupplementals.SimulatedStop))
					{
						if(debugOrderPrints) Print("MasterSimulatedStops missing something");
					}
					if(orderRecordMaster == null)
					{
						BackBrush = Brushes.Orange;
						Print(Time[0]+" RECORD UNUSUAL :"+executionOrder.Name);
					}
					//Print(" BOUGHT ORDER with "+orderRecordMaster.EntryOrderUUID);
					//Print(" THIS ACTION IS FOR "+executionOrder.Name);
					if(orderRecordMaster.EntryOrder == null)
					{
						if(debugOrderPrints) Print($"ASSIGNING ENTRY ORDER for {uuid_entry}");
						orderRecordMaster.EntryOrder = executionOrder;
	
					}
					if(orderRecordMaster.EntryOrder != null)
					{
					orderRecordMaster.OrderSupplementals.SimulatedStop.isExitReady = true;
					lastActionBar = CurrentBars[0];
					tryCatchSection = "ENTER";
					

					/// DELETE THE ENTRY AFTER WE'VE ASSIGNED THE ORDER OBJECT
					MastersimulatedEntryToDelete.Enqueue(orderRecordMaster.OrderSupplementals.SimulatedEntry);
					}
		        }
			}
			///*********EXIT PATH
			else if (executionOrder.OrderAction == OrderAction.Sell || executionOrder.OrderAction == OrderAction.BuyToCover)
			{
				if(debugOrderPrints) Print($" {Time[0]} customOnExecutionUpdate RECIEVED {executionOrder.OrderAction} for {executionOrder.Name} , {executionOrder.OrderAction}-{executionOrder.OrderState}");

				if(debugOrderPrints) Print($" RECIEVED customOnExecutionUpdate for {executionOrder.Name} , {executionOrder.OrderAction}-{executionOrder.OrderState}");
				
				
				if(executionOrder.Name == "Exit on session close" || executionOrder.Name == "Close position")
				{
					  /// 
					if(debugOrderPrints) Print("{Time[0]} {executionOrder.Name} DEAL WITH EOSC!");
					OrderRecordMasterLite ORML = new OrderRecordMasterLite();
					
					//ORML = HandleEOSC(executionOrder,uuid_exit,"customOnExecutionUpdate");
					
					ExitFollowUpAction_EOSC(executionOrder);
				}
				if(executionOrder.Name == uuid_exit)
				{
					///IF NOT NULL OR EMPTY, PROCEED
					if(OrderRecordMasterLiteExitSignals.ContainsKey(uuid_exit))/// find use the entry key
					{
						OrderRecordMasterLite orderRecordMaster = OrderRecordMasterLiteExitSignals[uuid_exit];
						lock(eventLock)
						{
							ExitFollowUpAction(orderRecordMaster,executionOrder,uuid_entry,uuid_exit);
						}
						if(executionOrder.OrderAction == OrderAction.Sell)
						{
							
							Print($"{Time[0]}  SELL {orderRecordMaster.ExitOrderUUID} to Close {orderRecordMaster.EntryOrderUUID} at {orderRecordMaster.OrderSupplementals.thisSignalExitAction} PROFIT ${orderRecordMaster.PriceStats.OrderStatsProfit} ");
						}
						if(executionOrder.OrderAction == OrderAction.Sell)
						{
							
							Print($"{Time[0]} BUY TO COVER {orderRecordMaster.ExitOrderUUID} to Close {orderRecordMaster.EntryOrderUUID} at {orderRecordMaster.OrderSupplementals.thisSignalExitAction} PROFIT ${orderRecordMaster.PriceStats.OrderStatsProfit} ");
						}

					}
					else
					{
						Print($"UUID Exit Key Not found for {uuid_exit}");
					}
				}
				
			}
			else
			{
			    if(debugOrderPrints) Print($"Unexpected OrderAction: {executionOrder.OrderAction}  {executionOrder.OrderState} for Order: {executionOrder.Name}");
			    uuid_exit = null; // Handle gracefully
				uuid_entry = null; // Handle gracefully
			}
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch Section: "+tryCatchSection+" - Exception error; "+ex);
			CloseAndStopStrategy("Error in customOnExecutionUpdate: " + ex.Message);

	
		}
	}
	
    protected void ExitFollowUpAction_EOSC(Order executionOrder)
	{
		string tryCatchSection = "ExitFollowUpAction_EOSC ";
		
		try
			{
			
				/// EXITING ORDERS
		        if ((executionOrder.OrderAction == OrderAction.BuyToCover || executionOrder.OrderAction == OrderAction.Sell) && (executionOrder.OrderState == OrderState.Filled || executionOrder.OrderState == OrderState.PartFilled)) // Buy to close out
		        {
					double profit = 0.0;

					string instrument = executionOrder.Instrument.ToString();
				
				
			    	tryCatchSection = "ENTRY IS PAIRED 2";
			        /// Calculate the total cost of the entry
					if (executionOrder.OrderAction == OrderAction.Sell || executionOrder.OrderAction == OrderAction.BuyToCover)
					{
					    profit = (executionOrder.AverageFillPrice - avgRollingEntryPrice) * executionOrder.Quantity * executionOrder.Instrument.MasterInstrument.PointValue;
					}
					else if (executionOrder.OrderAction == OrderAction.Buy || executionOrder.OrderAction == OrderAction.SellShort)
					{
				
					    profit = (avgRollingEntryPrice - executionOrder.AverageFillPrice) * executionOrder.Quantity * executionOrder.Instrument.MasterInstrument.PointValue;
					}
						     
				
					tryCatchSection = "ENTRY IS PAIRED 3";
					long epochMs = (long)(Time[0].ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;

					
					/// update daily profit
					if(profit != null)
					{

						dailyProfit += profit;
						dailyProfitATH = Math.Max(dailyProfit,dailyProfitATH);
						if( IsInStrategyAnalyzer)
						{
							virtualCashAccount += profit;
						}
						tryCatchSection = "ENTRY IS PAIRED 3A";	
					}
				
				
				
							
					if(profit > 0) 
					{

						tryCatchSection = "profit > 0";
					
						
						/// penalty tracker
						if(executionOrder.OrderAction == OrderAction.Sell)
						{
							dailyProfit_Long += profit;

						}														
						else if(executionOrder.OrderAction == OrderAction.BuyToCover)
						{
							
							dailyProfit_Short += profit;

						}
						overallModifier.Alpha++;
						///*********DRAW PROFIT GAIN***************
						tryCatchSection = "profit > 0 2";
						if(Math.Round(profit,2) > enableProfitColors)
						{
							
							if(executionOrder.OrderAction == OrderAction.BuyToCover)
							{
								//forceDrawDebug("["+instrument+"] | (EOSC-BuyToCover) $"+Math.Round(profit,2)+"",1,0,Low[0]-(TickSize*10),Brushes.Green,true);	
								
							}
							else if (executionOrder.OrderAction == OrderAction.Sell)
							{
								
								//forceDrawDebug("["+instrument+"] | (EOSC-Sell) $"+Math.Round(profit,2)+"",1,0,High[0]+(TickSize*10),Brushes.Green,true);	

							}
						}

					}
					///**********DRAW NEUTRAL WIN				
					else if(profit == 0)
					{
						

						tryCatchSection = "ENTRY IS PAIRED 5";
						
						//forceDrawDebug("EOSC  profit = "+profit,1,0,High[0],Brushes.Orange,true);
					}
					/// *********DRAW LOSS
					else if(profit < 0) 
					{
					

						///******** IF LONG LOSS
						if(executionOrder.OrderAction == OrderAction.Sell)
						{
							dailyProfit_Long += profit;			
							
						}	
						///******** IF SHORT LOSS
						else if(executionOrder.OrderAction == OrderAction.BuyToCover)
						{
							dailyProfit_Short += profit;
						
						}
						
						if(profit <= HardMaxLoss_Long || dailyProfit < 0)
						{
							overallModifier.Beta++;
						}
						/// DRAW LABELS				
						tryCatchSection = "profit < 0 A";
						
						if(Math.Round(profit,2) < -enableLossColors)
						{
						
							if(executionOrder.OrderAction == OrderAction.BuyToCover )
							{
								
								//forceDrawDebug("["+instrument+"] | (EOSC-BuyToCover) $"+Math.Round(profit,2)+"",1,0,High[0]-(TickSize*10),Brushes.Red,true);	
								
							}
							else if (executionOrder.OrderAction == OrderAction.Sell )
							{
								
								//forceDrawDebug("["+instrument+"] | (EOSC-Sell) $"+Math.Round(profit,2)+"",1,0,High[0]-(TickSize*10),Brushes.Red,true);	

							}
							
						}
						
					}

						
			
			}
			
		}
		///try catch
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch Section: "+tryCatchSection+" - Exception error; "+ex);
			CloseAndStopStrategy("Error in OnExecutionUpdate: " + ex.Message);

	
		}
	}
	protected void ExitFollowUpAction(OrderRecordMasterLite ORML, Order executionOrder, string uuid_entry,string uuid_exit)
	{
		string tryCatchSection = "ExitFollowUpAction "+uuid_exit;
		
		try
			{
			
					/// EXITING ORDERS
			        if ((executionOrder.OrderAction == OrderAction.BuyToCover || executionOrder.OrderAction == OrderAction.Sell) && (executionOrder.OrderState == OrderState.Filled || executionOrder.OrderState == OrderState.PartFilled)) // Buy to close out
			        {
						if(debugOrderPrints) Print($" RECIEVED ExitFollowUpAction {executionOrder.Name}");
						tryCatchSection = "EXITING ORDERS 1 "+uuid_exit;
				
						///IF NULL, likely EOSC
						if(ORML == null)
						{
							tryCatchSection = "EXITING ORDERS 2 ORMN NULL! ";
						}
						
						OrderRecordMasterLite orderRecordMaster = ORML;//OrderRecordMasterLiteExitSignals[ORML.ExitOrderUUID];	
						
						if(debugOrderPrints) Print($" ORDER FILLED EXIT: exec name {executionOrder.Name} , ORM name {orderRecordMaster.EntryOrderUUID} STOP Name {orderRecordMaster.OrderSupplementals.SimulatedStop.ExitOrderUUID}, TIME:{Time[0]}");

						
						orderRecordMaster.ExitOrder = executionOrder;
						tryCatchSection = "EXITING ORDERS 3";

						/// OrderRecordMasterLite are define dby the entry when we pull up the records

						tryCatchSection = "EXIT FILLED 2";
						if(orderRecordMaster != null)
						{
							
							tryCatchSection = "customOnExecutionUpdate 3";
							if(orderRecordMaster.ExitOrder != null)
							{
								//BackBrush = Brushes.Yellow;
								tryCatchSection = "EXIT PAIRED UUID";
								DebugPrint(debugSection.OnExecutionUpdate,"executionOrder.Name "+executionOrder.Name+" exitOrderRecord "+orderRecordMaster.ExitOrderUUID);
									
					
								double profit = 0.0;

					           	if (orderRecordMaster.EntryOrder != null)
					            {
										
									tryCatchSection = "ExitOrder IS PAIRED";
									DebugPrint(debugSection.OnExecutionUpdate,"entryOrderRecord "+orderRecordMaster.ExitOrder+" != null");
							
								
									signalReturnAction signal = orderRecordMaster.EntrySignalReturnAction;
								
							    	tryCatchSection = "ENTRY IS PAIRED 2";
							        /// Calculate the total cost of the entry
									if(executionOrder.OrderAction == OrderAction.Sell)
									{
										tryCatchSection = "ENTRY IS PAIRED 2.1";
										 profit = (executionOrder.AverageFillPrice - orderRecordMaster.EntryOrder.AverageFillPrice) * executionOrder.Quantity * Bars.Instrument.MasterInstrument.PointValue;		
										//Print("BuyToCover Sell "+profit);
									}
									 /// Calculate the total cost of the entry
									else if(executionOrder.OrderAction == OrderAction.BuyToCover)
									{
										tryCatchSection = "ENTRY IS PAIRED 2.2";
									 profit = (orderRecordMaster.EntryOrder.AverageFillPrice - executionOrder.AverageFillPrice) * executionOrder.Quantity * Bars.Instrument.MasterInstrument.PointValue;	
									
									}
										     
									//Print("PROFIT "+profit);
									tryCatchSection = "ENTRY IS PAIRED 3";
								
									/// update daily profit
									if(profit != null)
									{

										dailyProfit += profit;
										dailyProfitATH = Math.Max(dailyProfit,dailyProfitATH);
										
										if(orderRecordMaster.OrderSupplementals.patternId != "") 
											if(patternIdProfits.ContainsKey(orderRecordMaster.OrderSupplementals.patternId))
											{
												patternIdProfits[orderRecordMaster.OrderSupplementals.patternId] += profit;
											}
											else patternIdProfits[orderRecordMaster.OrderSupplementals.patternId] = profit;

										

										if( IsInStrategyAnalyzer)
										{
											virtualCashAccount += profit;
										}
										tryCatchSection = "ENTRY IS PAIRED 3A";	
									}
									
									
									if(profit > 0) 
									{
										
										tryCatchSection = "profit > 0";
										curvesService.DeregisterPosition(orderRecordMaster.EntryOrderUUID,true,orderRecordMaster.OrderSupplementals.divergence);

										signalPackage lastTradeSignalPackage = orderRecordMaster.OrderSupplementals.sourceSignalPackage;
										
										double alphaIncrement = 1;//Math.Abs(orderRecordMaster.OrderSupplementals.sourceSignalPackage.thisOrderFlowPattern.predictionScore);
										/// penalty tracker
										if(executionOrder.OrderAction == OrderAction.Sell)
										{
											dailyProfit_Long += profit;

										}														
										else if(executionOrder.OrderAction == OrderAction.BuyToCover)
										{
											
											dailyProfit_Short += profit;

										}
										overallModifier.Alpha++;
										///*********DRAW PROFIT GAIN***************
										tryCatchSection = "profit > 0 2";
										if(Math.Round(profit,2) > enableProfitColors)
										{
											string instrument = BarsArray[orderRecordMaster.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex].Instrument.FullName;
											if(executionOrder.OrderAction == OrderAction.BuyToCover)
											{
												forceDrawDebug("["+instrument+"] ["+orderRecordMaster.OrderSupplementals.patternSubtype+"]| ("+orderRecordMaster.OrderSupplementals.thisSignalExitAction+") $"+Math.Round(profit,2)+"",1,0,High[0]-(TickSize*10),Brushes.Green,true);	
												
											}
											else if (executionOrder.OrderAction == OrderAction.Sell)
											{
												
												forceDrawDebug("["+instrument+"] ["+orderRecordMaster.OrderSupplementals.patternSubtype+"] | ("+orderRecordMaster.OrderSupplementals.thisSignalExitAction+") $"+Math.Round(profit,2)+"",1,0,High[0]-(TickSize*10),Brushes.Green,true);	

											}
										}

									}
									///**********DRAW NEUTRAL WIN				
									else if(profit == 0)
									{
										
										curvesService.DeregisterPosition(orderRecordMaster.EntryOrderUUID,false);

										tryCatchSection = "ENTRY IS PAIRED 5";
										
										//forceDrawDebug("ID "+orderRecordMaster.EntrySignalReturnAction+" profit = "+profit,1,0,High[0],Brushes.Orange,true);
									}
									/// *********DRAW LOSS
									else if(profit < 0) 
									{

										curvesService.DeregisterPosition(orderRecordMaster.EntryOrderUUID,false,orderRecordMaster.OrderSupplementals.divergence);

									    HardTakeProfit_Long = microContractTakeProfit;
									    SoftTakeProfit_Long =  softTakeProfitMult;
									
									    HardTakeProfit_Short =  microContractTakeProfit;
									    SoftTakeProfit_Short =  softTakeProfitMult;
								
							
							       	 	 tryCatchSection = "profit < 0";
										if(profit <= HardMaxLoss_Long || dailyProfit < 0)
										{
											overallModifier.Beta++;
										}
										///******** IF LONG LOSS
										if(executionOrder.OrderAction == OrderAction.Sell)
										{
											dailyProfit_Long += profit;
											
										// Calculate the time/distance since the last loss
									    int barDistance = CurrentBars[0] - lastLossBar;
									
									    // Example: Use barDistance or timeDistance to scale the beta increment
									    double proximityScalingFactor = 1.0 / (barDistance + 1);  // or use timeDistance if working with time
									    
									    // Scale the beta increment based on proximity to the last loss
									    double lossImpact = 1.0 * proximityScalingFactor;  // Modify the constant as needed
										
										lastLossBar = CurrentBars[0];
										lastLossTime = Time[0];
										}	
										///******** IF SHORT LOSS
										else if(executionOrder.OrderAction == OrderAction.BuyToCover)
										{
											dailyProfit_Short += profit;
										
										    // Calculate the time/distance since the last loss
										    int barDistance = CurrentBars[0] - lastLossBar;
										
										    // Example: Use barDistance or timeDistance to scale the beta increment
										    double proximityScalingFactor = 1.0 / (barDistance + 1);  // or use timeDistance if working with time
										    
										    // Scale the beta increment based on proximity to the last loss
										    double lossImpact = 1.0 * proximityScalingFactor;  // Modify the constant as needed

											lastLossBar = CurrentBars[0];
											lastLossTime = Time[0];
											
																					
										}
										/// DRAW LABELS				
										tryCatchSection = "profit < 0 A";
										
										if(Math.Round(profit,2) < -enableLossColors)
										{
										
											string instrument = BarsArray[orderRecordMaster.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex].Instrument.FullName;

											if(executionOrder.OrderAction == OrderAction.BuyToCover )
											{
												
												//forceDrawDebug("("+orderRecordMaster.EntrySignalReturnAction+") $"+Math.Round(profit,2)+" ("+orderRecordMaster.OrderSupplementals.thisSignalExitAction+" ) MaxLoss: "+Math.Round(orderRecordMaster.PriceStats.OrderMaxLoss,0)+" ATH: "+Math.Round((double)orderRecordMaster.PriceStats.OrderStatsAllTimeHighProfit)+" PB: "+Math.Round(orderRecordMaster.PriceStats.OrderStatspullBackThreshold,0)+"",1,0,High[0]-(TickSize*10),Brushes.Red,true);	
												forceDrawDebug("["+instrument+"] ["+orderRecordMaster.OrderSupplementals.patternSubtype+"] | (("+orderRecordMaster.OrderSupplementals.thisSignalExitAction+") $"+Math.Round(profit,2)+"",1,0,High[0]-(TickSize*10),Brushes.Red,true);	
												
											}
											else if (executionOrder.OrderAction == OrderAction.Sell )
											{
												
												//forceDrawDebug("("+orderRecordMaster.EntrySignalReturnAction+") $"+Math.Round(profit,2)+" ("+orderRecordMaster.OrderSupplementals.thisSignalExitAction+" ) MaxLoss: "+Math.Round(orderRecordMaster.PriceStats.OrderMaxLoss,0)+" ATH: "+Math.Round((double)orderRecordMaster.PriceStats.OrderStatsAllTimeHighProfit)+" PB: "+Math.Round(orderRecordMaster.PriceStats.OrderStatspullBackThreshold,0)+"",1,0,High[0]-(TickSize*100),Brushes.Red,true);	
												forceDrawDebug("["+instrument+"] ["+orderRecordMaster.OrderSupplementals.patternSubtype+"] | (("+orderRecordMaster.OrderSupplementals.thisSignalExitAction+") $"+Math.Round(profit,2)+"",1,0,High[0]-(TickSize*10),Brushes.Red,true);	

											}
											
										}
								
							}
	
							MastersimulatedStopToDelete.Enqueue(orderRecordMaster.OrderSupplementals.SimulatedStop);
							MastersimulatedEntryToDelete.Enqueue(orderRecordMaster.OrderSupplementals.SimulatedEntry);
										
							}
				
						}
						else 
						{
							if(debugOrderPrints) Print($"executionOrder.Name {executionOrder.Name} FromEntrySignal {uuid_entry} Something Else? {executionOrder.OrderAction}, {executionOrder.OrderState}");
						}
				}
				//}
				
				}
				else if(!OrderRecordMasterLiteExitSignals.ContainsKey(uuid_exit))
				{
					if(debugOrderPrints) Print($"!OrderRecordMasterLiteExitSignals DOES NOT ContainsKey({uuid_exit})");
				}
				if(debugOrderPrints) Print($"{executionOrder.Name} FINALLY COMPLETED!");

			}
			///try catch
			catch (Exception ex)
			{
			    // Handle the exception
			    Print("tryCatch Section: "+tryCatchSection+" - Exception error; "+ex);
				CloseAndStopStrategy("Error in OnExecutionUpdate: " + ex.Message);
	
		
			}
	}
			

	///  NEW ENTRY ORDERS
	/// ASSIGN ORDER TO orderRecordMasterBuy.EntryOrder
	
	void HandleEntryOrder(Order order,string uuid,string source) 
	{
		string tryCatchSection = "HandleEntryOrder begin";
		try
		{
			tryCatchSection = "HandleEntryOrder 1 "+uuid;
	
			if(!OrderRecordMasterLiteEntrySignals.ContainsKey(uuid))
			{
				if(debugOrderPrints) Print($"HandleEntryOrder OrderRecordMasterLiteEntrySignals not ContainsKey({uuid})");
				return;
			}
			
			OrderRecordMasterLite orderRecordMasterBuy = OrderRecordMasterLiteEntrySignals[uuid];
	
			if(orderRecordMasterBuy.EntryOrder != null)
		    {
		        if(debugOrderPrints) Print($"HandleEntryOrder: UUID {uuid} already processed. Skipping.");
		        return;
		    }
			else if(orderRecordMasterBuy != null)
			{
					tryCatchSection = "HandleEntryOrder 2";
	
					/// need to associate the entry order 
					if(orderRecordMasterBuy.EntryOrder == null)
					{
						orderRecordMasterBuy.EntryOrder = order;
					}
					
					if(order.OrderState == OrderState.Submitted)
					{
						
						tryCatchSection = "HandleEntryOrder 3";
						orderRecordMasterBuy.EntryOrder.OrderAction = order.OrderAction;
						orderRecordMasterBuy.EntryOrderUUID = uuid;
   						if(debugOrderPrints) Print($"HandleEntryOrder: Assigned EntryOrder {uuid}");
	
						
						
						savedBar = CurrentBars[0];
						/// APPEND THE NEW BUY ORDER RECORD
					
						orderRecordMasterBuy.OrderSupplementals.SimulatedEntry.isEnterReady = true;
	
					}
					if(order.OrderState == OrderState.Filled)
					{
						tryCatchSection = "HandleEntryOrder 4";
						StrategyLastEntryTime = order.Time;///strategy level
						ThrottleAll = order.Time;
						
						/// WHEN FILLED, CORRECT THE ACTUAL ENTRY PRICE
						/// 
						globalStopStart = orderRecordMasterBuy.EntryBar;
				
						tryCatchSection = "HandleEntryOrder 5";
						orderRecordMasterBuy.PriceStats.OrderStatsEntryPrice = order.AverageFillPrice;
						orderRecordMasterBuy.OrderSupplementals.SimulatedStop.isExitReady = true; /// we can now create an exit
						lastActionBar = CurrentBars[0];
					
					 	// Add the signal to the open positions set
						orderRecordMasterBuy.OrderSupplementals.SimulatedStop.isExitReady = true;
					
						///register
						orderRecordMasterBuy.OrderSupplementals.isEntryRegisteredDTW = curvesService.RegisterPosition(orderRecordMasterBuy.EntryOrderUUID,orderRecordMasterBuy.OrderSupplementals.patternId, orderRecordMasterBuy.EntryOrder.Instrument.FullName.Split(' ')[0],Time[0]);
							
						
						
					}
					if(order.OrderState == OrderState.Working)
					{
							
					}
					if(order.OrderState == OrderState.Cancelled)
					{
					
						
					}
					if(order.OrderState == OrderState.ChangePending)
					{
						
					}
					
				}
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch Section: "+tryCatchSection+" - Exception error; "+ex);
			CloseAndStopStrategy("Error in HandleEntryOrder: " + ex.Message);

	
		}
			
	}

	
	///  NEW EXIT ORDERS
	/// HANDLE EOSC ASSIGNMENT
	void HandleExitOrder(Order order,string uuid, string source) 
	{
		string tryCatchSection = "BeginHandleExitOrder ";
		
		try
		{

		
			if (!OrderRecordMasterLiteExitSignals.ContainsKey(uuid))
		    {
		        if(debugOrderPrints) Print($"HandleExitOrder: UUID {uuid} does not exist. Skipping.");
		        return;
		    }
		
		    OrderRecordMasterLite exitRecord = OrderRecordMasterLiteExitSignals[uuid];
			
		    if (exitRecord.ExitOrder != null)
		    {
		        if(debugOrderPrints) Print($"HandleExitOrder: UUID {uuid} already processed. Skipping.");
		        return;
		    }
			/// non UUID exit signals created by system
			if(order.Name == "Exit on session close" || order.Name == "Close position"|| order.Name == "Buy to cover" || order.Name == "Sell" )
			{
			
				if (order.OrderState == OrderState.Filled)
			    {
					if(debugOrderPrints) Print($"v2 EOSC {order.Name}");
			        
					/// list of currently open orders
					foreach (var openOrder in openOrders)
			        {
			            /// Associate the EOSC order to the existing open orders
			            openOrder.ExitOrder = order;
			            openOrder.ExitOrderUUID = openOrder.ExitOrderUUID+"Other";
						openOrder.OrderSupplementals.SimulatedStop.ExitOrderUUID = openOrder.ExitOrderUUID+"Other";
						curvesService.DeregisterPosition(openOrder.EntryOrderUUID);
				
			            if(debugOrderPrints) Print($"Processing exit for EntryOrderUUID: {openOrder.EntryOrderUUID}, ExitOrder: {openOrder.ExitOrderUUID+"Other"} type EOSC/Other");
			        }
	
			        /// Clear the list after processing
			        openOrders.Clear();
				}
			            
			       
			    

			}
	
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch Section: "+tryCatchSection+" - Exception error; "+ex);
	
		}
		
	}

	protected OrderRecordMasterLite HandleEOSC(Order order, string uuid, string source)
	{
	    string tryCatchSection = "BeginHandleExitOrder";
		OrderRecordMasterLite ORML = new OrderRecordMasterLite(); 
		ORML.EntryOrderUUID = null;
		ORML.ExitOrderUUID = null;
		if(curvesService.divergenceScores.ContainsKey(order.Name))
		{
			curvesService.DeregisterPosition(order.Name);
		}
		
	    try
	    {
	        if(debugOrderPrints) Print($"HandleExitOrder! from {source} for UUID: {uuid}");
	
	        if (!OrderRecordMasterLiteExitSignals.ContainsKey(uuid))
	        {
	            if(debugOrderPrints) Print($"Error: UUID {uuid} not found in OrderRecordMasterLiteEntrySignals for order {order.Name}. Source: {source}");
	            return ORML; // Skip processing
	        }
	
	        OrderRecordMasterLite orderRecordMasterSell = OrderRecordMasterLiteEntrySignals[uuid];
	
	        if (orderRecordMasterSell.ExitOrder == null)
	        {
			
	            if(debugOrderPrints) Print($"Assigning exit order for UUID: {orderRecordMasterSell.EntryOrderUUID}");
	            orderRecordMasterSell.ExitOrder = order;
	            orderRecordMasterSell.ExitOrderUUID = uuid;
				return orderRecordMasterSell;
	            // Update other values as needed
	        }
	       
			return ORML;
	    }
	    catch (Exception ex)
	    {
	        Print($"tryCatch Section: {tryCatchSection} - Exception error; {ex}");
			return ORML;
	    }
	}
		
	
	protected void ExitOrderFunction(int QuantityVal,OrderRecordMasterLite orderRecordMasterParam, string overWriteUUID, string fromCode,bool isForcedEntryVal,ExitOrderType exitOrderType,OrderType orderType,double stopPrice,double limitPrice,bool reEnter)
		{
			lastFunction = "ExitOrderFunction";
				/// CREATE NEW ORDER RECORD TO ACOMPANY EXIT ORDER
					
						
						string ThisUUID = orderRecordMasterParam.ExitOrderUUID; /// passthrough
						
						if(debugOrderPrints) Print($"EXIT ORDER FUNCTION {orderRecordMasterParam.ExitOrderUUID}");
						if(isBarOpenForExit == false)
						{
							/// if false, an order was already submitted this bar.  Will be reset in OnBarUpdate on first tick.
							return;
						}
				
						if(orderRecordMasterParam != null)
						{
							/// bring in the master record
							OrderRecordMasterLite thisOrderRecordMaster = orderRecordMasterParam;
						
							
								
							///ASSIGN UUID AND ORDER IN OnOrderUpdate()
							thisOrderRecordMaster.ExitOrderUUID = ThisUUID;
						
						
							int seriesIndex = thisOrderRecordMaster.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex;
							///update the new reason
							thisOrderRecordMaster.OrderSupplementals.ExitReason = ""+exitOrderType;
							
						
							
							string uuid	= orderRecordMasterParam.ExitOrderUUID;
										
							//BackBrush = Brushes.AliceBlue;
							/// create a simulated entry
							simulatedStop simulatedExitAction = new simulatedStop
							{
								 EntryOrderUUID = thisOrderRecordMaster.EntryOrderUUID,
								 ExitOrderUUID = uuid,
								 EntryOrderAction = thisOrderRecordMaster.EntryOrder.OrderAction == OrderAction.Buy ? OrderAction.Sell : OrderAction.BuyToCover,
								 quantity = QuantityVal,								
								 isExitReady = true
							};
						
							/// submit the phantom order 
							/// add the record simulates opening a stoploss
							
							MasterSimulatedStops.Add(simulatedExitAction);
							/// set the sim stop in the OrderRecordMaster
							thisOrderRecordMaster.OrderSupplementals.SimulatedStop = simulatedExitAction;
							
							//BackBrush = Brushes.Olive;
							isBarOpenForExit = false;/// after a position is initialized as an order, close the bar											
												
							
						}
					
				
						
	}
		
	protected void CancelExpiredOrders()
	{
		lastFunction = "CancelExpiredOrders";
		string tryCatchSelection = "Begin CancelOrder";	
		try{
	    int count_unfilled = 0;
	    // For each ORDER
		
			foreach (OrderRecordMasterLite orderRecordMaster in LiteMasterRecords)
			{		
				
					if(orderRecordMaster != null && orderRecordMaster.OrderSupplementals.SimulatedEntry.isEnterReady == true)
					{
						if(orderRecordMaster.EntryOrder != null)
						{
							
							///Filter by action - don't cancel covers if you're canceling entries
							if(orderRecordMaster.EntryOrder.OrderState == OrderState.Working || orderRecordMaster.EntryOrder.OrderState == OrderState.Accepted || orderRecordMaster.EntryOrder.OrderState == OrderState.Submitted || orderRecordMaster.EntryOrder.OrderState == OrderState.Initialized)												
							{
								DebugPrint(debugSection.OnBarUpdate,"CancelExpiredOrders 4");
								DateTime OrderEntryTime = orderRecordMaster.EntryOrder.Time;
							    DateTime xMinutesAfterFilled = OrderEntryTime.AddMinutes(1); // Adds 10 minutes to the filled time
							    
							    // To check if it has been 10 minutes since the order was filled
							    if (Times[0][0] >= xMinutesAfterFilled) // Assuming Time[0] is the current time in the strategy context
								{
								    CancelOrder(orderRecordMaster.EntryOrder);							
									//forceDrawDebug("EXP",-1,Low[0],Brushes.Purple,true);
									DebugPrint(debugSection.OnBarUpdate,"CancelExpiredOrders 5");
							    }
							
								DebugPrint(debugSection.OnBarUpdate,"CancelExpiredOrders 6");

					        }
						}
					}
					else
					{
							DebugPrint(debugSection.OnBarUpdate,("orderRecordMaster is null?"+(orderRecordMaster == null)+" isEnterReady = "+orderRecordMaster.OrderSupplementals.SimulatedEntry.isEnterReady));
							if(orderRecordMaster.OrderSupplementals.SimulatedEntry.isEnterReady == false)
							{
								
							}
					
					}
					
				}
			////delte existing entries
				foreach(simulatedEntry simEntry in MasterSimulatedEntries)
				{
					if(simEntry.isEnterReady == true)
					{
						int quantity =  simEntry.quantity;
						int offset = CurrentBar-simEntry.EntryBar;
						DateTime OrderEntryTime = Bars.GetTime(simEntry.EntryBar);
						DateTime xMinutesAfterFilled = OrderEntryTime.AddMinutes(1); // Adds 10 minutes to the filled time
						
						if (Times[0][0] >= xMinutesAfterFilled) // Assuming Time[0] is the current time in the strategy context
						{  	
							tryCatchSelection = "CancelOrder";	
					
							simEntry.isEnterReady = false;
							MastersimulatedEntryToDelete.Enqueue(simEntry);
							
					    }
					}
				}
		}
		catch (Exception ex)
		{
		    // Handle the exception
		    Print("tryCatch Section: "+tryCatchSelection+" - Exception error; "+ex);
			
		}
		

	}
	

	

	public int CountEntryOrdersByWorkingStates(bool cancelThem)
	{
		
	    int c = 0;
		
	    // For each ORDER
		lastFunction = "CountEntryOrdersByWorkingStates";
		DebugPrint(debugSection.OnBarUpdate,"CountOrdersByStates 0");
			foreach (OrderRecordMasterLite orderRecordMaster in LiteMasterRecords)
			{		
				DebugPrint(debugSection.OnBarUpdate,"CountOrdersByStates 1");
				if(orderRecordMaster != null)
				{
					DebugPrint(debugSection.OnBarUpdate,"CountOrdersByStates 1.5");
					if(orderRecordMaster.OrderSupplementals.SimulatedEntry.isEnterReady == true)
					{
							DebugPrint(debugSection.OnBarUpdate,"CountOrdersByStates 2");
							if(orderRecordMaster.EntryOrder != null)
							{
						
								
								DebugPrint(debugSection.OnBarUpdate,"CountOrdersByStates 3");
								///Filter by action - don't cancel covers if you're canceling entries
								if(orderRecordMaster.EntryOrder.OrderState == OrderState.Working || orderRecordMaster.EntryOrder.OrderState == OrderState.Accepted || orderRecordMaster.EntryOrder.OrderState == OrderState.Submitted || orderRecordMaster.EntryOrder.OrderState == OrderState.Initialized)
								{
									int age = CurrentBars[0] - orderRecordMaster.EntryBar;
									c++; /// count the order
									if(cancelThem && age > 5)/// keep open at least 5 bars to ensure we dont cancel something trying to be filled
									{
										/// if the entryPrice > GetCurrentAsk(0) then price is moving away and we can cancel and resubmit
										if(orderRecordMaster.EntryOrder.AverageFillPrice > GetCurrentAsk(0))
										{
										CancelOrder(orderRecordMaster.EntryOrder);
										c--; ///uncount if we cancel
									
										}
									
									}
									
								}
							}
					}
				}
			}
		
		
			return c;
	

	}
	
	

	
	///  nonstandard exits: EOSC, OOM, etc
	protected void ExitActiveOrders(ExitOrderType exitOrderType, signalExitAction exitSignalAction, bool reEnter)
	{
	    lastFunction = "ExitActiveOrders";
	    int canceledOrderCount = 0;
	
		double unrealizedDailyProfit = dailyProfit+CalculateTotalOpenPositionProfit();
		if(unrealizedDailyProfit < -dailyProfitMaxLoss)
		{
		//	Print($"{Time[0]} > EXIT ACTIVE ORDERS , DailyProfit {unrealizedDailyProfit}");
	    }
		foreach (OrderRecordMasterLite orderRecordMaster in LiteMasterRecords)
		    {
		        if (orderRecordMaster.EntryOrder != null && orderRecordMaster.ExitOrder == null)
		        {
		            int seriesIndex = orderRecordMaster.OrderSupplementals.sourceSignalPackage.instrumentSeriesIndex;
		
					

		            if (orderRecordMaster.EntryOrder.OrderAction == OrderAction.Buy && orderRecordMaster.OrderSupplementals.SimulatedStop.isExitReady == true)
		            {
		                orderRecordMaster.OrderSupplementals.thisSignalExitAction = exitSignalAction;
					    //if(debugOrderPrints) Print($"{CurrentBars[0]} > EXIT ACTIVE ORDERS (SELL) : Series Index: {seriesIndex}, EntryUUID: {orderRecordMaster.EntryOrderUUID}, ExitUUID: {orderRecordMaster.ExitOrderUUID} DailyProfit {unrealizedDailyProfit}");
		
						OrderAction determinedExitAction = (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Long) ? OrderAction.Sell : OrderAction.BuyToCover;
						string exitSignalName = orderRecordMaster.ExitOrderUUID + "_Exit";
						
						Print($"DEBUG ExitActiveOrders: Exiting {GetMarketPositionByIndex(1)}/{GetMarketPositionByIndex(0)}  for EntryUUID {orderRecordMaster.EntryOrderUUID}. Submitting Action: {determinedExitAction}, Name: {exitSignalName}");
						ExitLong(1,orderRecordMaster.EntryOrder.Quantity,orderRecordMaster.ExitOrderUUID,orderRecordMaster.EntryOrderUUID);
		                /*SubmitOrderUnmanaged(
		                    1,
		                    OrderAction.Sell,
		                    OrderType.Market,
		                    orderRecordMaster.EntryOrder.Quantity,
		                    0,
		                    0,
		                    //orderRecordMaster.EntryOrderUUID+" exit ",
		                    orderRecordMaster.ExitOrderUUID
		                );
						*/
						orderRecordMaster.OrderSupplementals.SimulatedStop.isExitReady = false;
		            }
		            // Handle Short Positions
		            else if (orderRecordMaster.EntryOrder.OrderAction == OrderAction.SellShort && orderRecordMaster.OrderSupplementals.SimulatedStop.isExitReady == true)
		            {
		                orderRecordMaster.OrderSupplementals.thisSignalExitAction = exitSignalAction;
		
		                  if(debugOrderPrints) Print($"{CurrentBars[0]} > EXIT ACTIVE ORDERS (BUYTOCOVER) : Series Index: {seriesIndex}, EntryUUID: {orderRecordMaster.EntryOrderUUID}, ExitUUID: {orderRecordMaster.ExitOrderUUID}");
		                
						 ExitShort(1,orderRecordMaster.EntryOrder.Quantity,orderRecordMaster.ExitOrderUUID,orderRecordMaster.EntryOrderUUID);
/*
						  SubmitOrderUnmanaged(
		                    1,
		                    OrderAction.BuyToCover,
		                    OrderType.Market,
		                    orderRecordMaster.EntryOrder.Quantity,
		                    0,
		                    0,
		                    //orderRecordMaster.EntryOrderUUID+" exit ",
		                    orderRecordMaster.ExitOrderUUID
		                );
						  */
						orderRecordMaster.OrderSupplementals.SimulatedStop.isExitReady = false;
		            }
		
		            // Cancel any remaining working orders associated with this entry
		            if (orderRecordMaster.EntryOrder.OrderState == OrderState.Working)
		            {
		                if(debugOrderPrints) Print($"Canceling working order: {orderRecordMaster.EntryOrder.Name}");
		                CancelOrder(orderRecordMaster.EntryOrder);
		            }
		        }
		    }
		
	}

	
		

		
		private double CalculatePenaltyRatio(double longProfit, double shortProfit)
		{
		    if (longProfit == 0 && shortProfit == 0)
		        return 0.5; // Neutral penalty if no trades have been made
		
		    // Calculate the ratio of short profit to long profit
		    double ratio = longProfit != 0 ? shortProfit / longProfit : (shortProfit != 0 ? 1.0 : 0.5);
		    
		    // Ensure the ratio is between 0.0 and 1.0
		    ratio = Math.Max(0.0, Math.Min(ratio, 1.0));
		    return ratio;
		}
		
		private (double longPenalty, double shortPenalty) DeterminePenalties()
		{
			
		    double longProfit = performanceTracker.longProfit;
		    double shortProfit = performanceTracker.shortProfit;
		
		    double penaltyRatio = CalculatePenaltyRatio(longProfit, shortProfit);
		
		    // Dynamic penalties
		    double longPenalty = 1.0 - penaltyRatio;
		    double shortPenalty = penaltyRatio;
		
		    // Ensure penalties are within a reasonable range
		    longPenalty = longPenalty < 0.1 ? 0.1 : longPenalty;
		    shortPenalty = shortPenalty < 0.1 ? 0.1 : shortPenalty;
		
		    return (longPenalty, shortPenalty);
		}
		
		
	
		

	
	}
    
		
}