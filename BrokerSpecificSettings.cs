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
using System.Xml.Serialization;
using System.IO;



#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{




	public partial class MainStrategy : Strategy
	{
			
	 	// Step 1: Create the OnRithmicExecutionUpdate Method
		// Step 1: Create OnRithmicExecutionUpdate Method
		protected void OnRithmicExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
		
			if(!IsInStrategyAnalyzer && State == State.Realtime) 
			Print("OnRithmicExecutionUpdate "+execution.Order.Name+" "+execution.Order.OrderState);
		    // Execute the customOnOrderUpdate logic
			customOnOrderUpdate(
		        execution.Order, 
		        execution.Order.LimitPrice, 
		        execution.Order.StopPrice, 
		        quantity, 
		        execution.Order.Filled, 
		        execution.Order.AverageFillPrice, 
		        execution.Order.OrderState, 
		        time, 
		        ErrorCode.NoError, 
		        ""
		    );
		    // Execute the customOnExecutionUpdate logic
		    customOnExecutionUpdate(
		        execution.Order, 
		        executionId, 
		        price, 
		        quantity, 
		        marketPosition, 
		        orderId, 
		        time, 
		        execution.Order.FromEntrySignal
		    );
		}
		
		
		
		// No changes needed in ProcessQueuedEvents or the custom methods


	}
}



