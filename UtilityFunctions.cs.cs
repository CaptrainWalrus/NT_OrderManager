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

#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{


	public partial class MainStrategy : Strategy
	{
		
	private DateTime GetLastEntryTime(Account account)
	{
	    DateTime lastEntryTime = DateTime.MinValue;  // Initialize to an early date as a fallback
	    
	    foreach (Order order in account.Orders)
	    {
	        // Check if the order is filled and is an entry order (buy or sell)
	        if (order.OrderState == OrderState.Filled && 
	            (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.Sell))
	        {
	            // If this order's time is more recent than the current latest, update the lastEntryTime
	            if (order.Time > lastEntryTime)
	            {
	                lastEntryTime = order.Time;
	            }
	        }
	    }
	
	    return lastEntryTime;
	}

	double CalculateRateOfChange(double currentValue, double previousValue)
	{
	    double result = ((currentValue - previousValue) / previousValue);
		if (Double.IsNaN(result) || Double.IsInfinity(result))
		{
		    // Handle the situation
		    return 0; // or handle appropriately
		}
		else
		return result;
		
	}

	public enum CrossType
	{
	    Crossover,  // EMA3 crossing above VWAP
	    Crossunder  // EMA3 crossing below VWAP
	}
	
	private bool IsLikelyToCross(int lookaheadBars, ISeries<double> vwapSeries, ISeries<double> emaSeries, CrossType crossType)
	{
	    if (vwapSeries == null || emaSeries == null || CurrentBar < 1)
	    {
	        return false;
	    }
	
	    // Calculate the current slopes (rate of change) for both VWAP1 and EMA3
	    double vwapSlope = vwapSeries[0] - vwapSeries[1];
	    double emaSlope = emaSeries[0] - emaSeries[1];
	
	    // Project future values
	    for (int i = 1; i <= lookaheadBars; i++)
	    {
	        double vwapProjected = vwapSeries[0] + (vwapSlope * i);
	        double emaProjected = emaSeries[0] + (emaSlope * i);
	
	        // Check for a crossover or crossunder based on the specified crossType
	        if (crossType == CrossType.Crossover)
	        {
	            // Check if EMA3 is likely to cross above VWAP
	            if (emaSeries[0] < vwapSeries[0] && emaProjected >= vwapProjected)
	            {
	                return true; // Crossover is likely within the next 'lookaheadBars'
	            }
	        }
	        else if (crossType == CrossType.Crossunder)
	        {
	            // Check if EMA3 is likely to cross below VWAP
	            if (emaSeries[0] > vwapSeries[0] && emaProjected <= vwapProjected)
	            {
	                return true; // Crossunder is likely within the next 'lookaheadBars'
	            }
	        }
	    }
	
	    return false; // No cross detected within the lookahead period
	}


	
		
	public int? IdentifyReversalPoint()
	{
	    // Ensure we have at least 7 bars to work with
	    if (CurrentBar < 6)
	        return null;
	
	    // Check the last 7 bars for a reversal pattern
	    bool firstThreeUp = Close[6] < Close[5] && Close[5] < Close[4] && Close[4] < Close[3];
	    bool firstThreeDown = Close[6] > Close[5] && Close[5] > Close[4] && Close[4] > Close[3];
	
	    bool lastThreeDown = Close[2] > Close[1] && Close[1] > Close[0];
	    bool lastThreeUp = Close[2] < Close[1] && Close[1] < Close[0];
	
	    // Check for a peak (3 bars up, 1 bar down, 3 bars down)
	    if (firstThreeUp && lastThreeDown && Close[3] > Close[2])
	    {
	        return 3; // Peak at bar offset 3
	    }
	
	    // Check for a valley (3 bars down, 1 bar up, 3 bars up)
	    if (firstThreeDown && lastThreeUp && Close[3] < Close[2])
	    {
	        return 3; // Valley at bar offset 3
	    }
	
	    // No reversal point found
	    return null;
	}

	
	public int LowestBarOffset(ISeries<double> series, int lookbackPeriod, int offset)
	{
	    if (offset < 0 || lookbackPeriod < 0)
	        throw new ArgumentException("Offset and lookbackPeriod must be non-negative.");
	
	    int currentBarCount = series.Count;
	    int startIndex = currentBarCount-Math.Max(0, currentBarCount - offset - lookbackPeriod);
	    int endIndex = currentBarCount-Math.Max(0, currentBarCount - offset);
	
	    // Ensure the end index does not exceed the series count
	    endIndex = Math.Min(endIndex, currentBarCount);
	
	    double lowestValue = double.MaxValue;
	    int lowestBarIndex = -1;
		//Print("LB: currentBarCount "+currentBarCount+" startIndex "+startIndex+" endIndex "+endIndex);
	    for (int i = endIndex; i < startIndex; i++)
	    {
	        if (series[i] < lowestValue)
	        {
	            lowestValue = series[i];
	            lowestBarIndex = i;
	        }
	    }
	
	    // Return the offset from the current bar position
	    return lowestBarIndex;
	}

	
	public int HighestBarOffset(ISeries<double> series, int lookbackPeriod, int offset)
	{
	
	    if (offset < 0 || lookbackPeriod < 0)
	        throw new ArgumentException("Offset and lookbackPeriod must be non-negative.");
	
	    int currentBarCount = series.Count;
	    int startIndex = currentBarCount-Math.Max(0, currentBarCount - offset - lookbackPeriod);
	    int endIndex = currentBarCount-Math.Max(0, currentBarCount - offset);
	
	    // Ensure the end index does not exceed the series count
	    endIndex = Math.Min(endIndex, currentBarCount);
	
	    double highestValue = double.MinValue;
	    int highestBarIndex = -1;
		//Print("HB: currentBarCount "+currentBarCount+" startIndex "+startIndex+" endIndex "+endIndex);
		
	    for (int i = endIndex; i < startIndex ; i++)
	    {
		
	        if (series[i] > highestValue)
	        {
	            highestValue = series[i];
	            highestBarIndex = i;
	        }
	    }
	
	    // Return the offset from the current bar position
	    return highestBarIndex;
	}
	

	
	public void Logger(string message)
	{
	    Print(message); // This uses NinjaTrader's Print method
	}
	
	private double? CalculateATH(Order order)
    {
        // Implement your ATH calculation logic
        return null;
    }

    private double GetProfitTarget(Order order)
    {
        // Implement your logic to get profit target
        return 0;
    }

    private double GetPullBackThreshold(Order order)
    {
        // Implement your logic to get pull back threshold
        return 0;
    }

	// Function to find the last crossover point (VWAP1 crosses above EMA1)
	int CrossOver(ISeries<double> VWAP1, ISeries<double> EMA1, int maxLookback = 100)
	{
	    int lookback = Math.Min(maxLookback, VWAP1.Count - 1);
		if(EMA1.Count > maxLookback || VWAP1.Count > maxLookback)
		{
		    for (int barsAgo = 1; barsAgo <= lookback; barsAgo++)
		    {
		        if (VWAP1[barsAgo] > EMA1[barsAgo] && VWAP1[barsAgo - 1] < EMA1[barsAgo - 1])
		        {
		            return barsAgo;
		        }
		    }
		}
	    return -1; // Return -1 if no crossover found
	}

	// Function to find the last crossunder point (VWAP1 crosses below EMA1)
	int CrossUnder(ISeries<double> VWAP1, ISeries<double> EMA1, int maxLookback = 100)
	{
	    int lookback = Math.Min(maxLookback, VWAP1.Count - 1);
		if(EMA1.Count > maxLookback || VWAP1.Count > maxLookback)
		{
		    for (int barsAgo = 1; barsAgo <= lookback; barsAgo++)
		    {
		        if (VWAP1[barsAgo] < EMA1[barsAgo] && VWAP1[barsAgo - 1] > EMA1[barsAgo - 1])
		        {
		            return barsAgo;
		        }
		    }
		}
	    return -1; // Return -1 if no crossunder found
	}

		
	
	
	
	





	
		protected Brush GetColorBrush(double val, double min, double max)
		{
		    if ((val < -0.9 * max && val >= -1 * max) || (val > 0.9 * max && val <= 1 * max))
		    {
		        return Brushes.Blue;
		    }
		    else if ((val < -0.8 * max && val >= -0.9 * max) || (val > 0.8 * max && val <= 0.9 * max))
		    {
		        return Brushes.CadetBlue;
		    }
		    else if ((val < -0.7 * max && val >= -0.8 * max) || (val > 0.7 * max && val <= 0.8 * max))
		    {
		        return Brushes.Cyan;
		    }
		    else if ((val < -0.6 * max && val >= -0.7 * max) || (val > 0.6 * max && val <= 0.7 * max))
		    {
		        return Brushes.Lime;
		    }
		    else if ((val < -0.5 * max && val >= -0.6 * max) || (val > 0.5 * max && val <= 0.6 * max))
		    {
		        return Brushes.Green;
		    }
		    else if ((val < -0.4 * max && val >= -0.5 * max) || (val > 0.4 * max && val <= 0.5 * max))
		    {
		        return Brushes.YellowGreen;
		    }
		    else if ((val < -0.3 * max && val >= -0.4 * max) || (val > 0.3 * max && val <= 0.4 * max))
		    {
		        return Brushes.Yellow;
		    }
		    else if ((val < -0.2 * max && val >= -0.3 * max) || (val > 0.2 * max && val <= 0.3 * max))
		    {
		        return Brushes.PaleGoldenrod;
		    }
		    else if ((val < -0.1 * max && val >= -0.2 * max) || (val > 0.1 * max && val <= 0.2 * max))
		    {
		        return Brushes.Orange;
		    }
		    else if ((val < 0 && val >= -0.1 * max) || (val > 0 && val <= 0.1 * max))
		    {
		        return Brushes.OrangeRed;
		    }
		    else if (val == 0)
		    {
		        return Brushes.Red;
		    }
		    else
		    {
		        return Brushes.Purple; // For values outside the -1 to 1 range
		    }
		}
		
		protected Brush GetColorBrushBullBear(double val, double min, double max)
		{
		    // Normalize the value to a range of 0 to 1
		    double normalizedValue = (val - min) / (max - min);
		
		    // Determine the color based on the normalized value
		    if (normalizedValue <= -0.9)
		    {
		        return Brushes.Red; // Minimum value or very close to it
		    }
		    else if (normalizedValue <= -0.5)
		    {
		        return Brushes.OrangeRed;
		    }
		    else if (normalizedValue <= -0.3)
		    {
		        return Brushes.Orange;
		    }
		    else if (normalizedValue <= -0.1)
		    {
		        return Brushes.PaleGoldenrod;
		    }
			else if (normalizedValue > -0.1 && normalizedValue < 0.1)
		    {
		        return Brushes.CadetBlue;
		    }
		    
		    else if (normalizedValue >= 0.1)
		    {
		        return Brushes.YellowGreen;
		    }
		    else if (normalizedValue >= 0.3)
		    {
		        return Brushes.DarkGreen;
		    }
		    else if (normalizedValue >= 0.5)
		    {
		        return Brushes.Green;
		    }
		    else if (normalizedValue >= 0.9)
		    {
		        return Brushes.Lime;
		    }
		    
		    else
		    {
		        return Brushes.Purple; // For values outside the expected range
		    }
		}


		
		
		

		
		protected bool exitTouchStop(OrderAction OA,double stop, int vol)
		{
			if(Volume[0] < vol || vol == 0)
			{
				if(OA == OrderAction.SellShort && Open[0] < stop && Close[0] > stop)
				{
					return true;
				}
				else if(OA == OrderAction.Buy && Open[0] > stop && Close[0] < stop)
				{
					return true;
				}
				return false;
			}
			return false;
		}
		
		protected bool exitPastStop(OrderAction OA,double stop, int vol)
		{
			if(Volume[0] < vol || vol == 0)
			{
				if(OA == OrderAction.SellShort && Volume[0] < vol && Close[0] > stop)
				{
					return true;
				}
				else if(OA == OrderAction.Buy && Close[0] < stop)
				{
					return true;
				}
				return false;
			}
			return false;
		}

		protected void drawDebug(string context,int ydir,Brush color,bool showAll)
		{
			if(debugModeDraw)
			{
				int x = 100;
				if(showAll == true)
				{
					x = 29;
				}
				if(random.Next(1,x) < 30)
				{
				double y1 = ydir < 0 ? Low[0] : High[0];
				double y2 = y1+((TickSize*random.Next(5, 100))*ydir);
					
				
				
				NinjaTrader.Gui.Tools.SimpleFont myFont = new NinjaTrader.Gui.Tools.SimpleFont("Arial", 12) { Size = 12, Bold = false };
				Draw.Text(this,"1423"+CurrentBar,false,context+"",0,y2,0,Brushes.White ,myFont, TextAlignment.Center, color, null, 1);
				Draw.Line(this,"384845"+CurrentBar,0,y1,0,y2,Brushes.Yellow);
				}
			}
			
		}
		
		protected void forceDrawDebug(string context,int ydir,int Offset,double ystart,Brush color,bool showAll)
		{
			
				int x = 100;
				if(showAll == true)
				{
					x = 29;
				}
				if(random.Next(1,x) < 30)
				{
				double y1 = ystart;//ydir < 0 ? Low[0] : High[0];
				double y2 = y1+((TickSize*random.Next(5, 10))*ydir);
					
				
				
				NinjaTrader.Gui.Tools.SimpleFont myFont = new NinjaTrader.Gui.Tools.SimpleFont("Arial", 12) { Size = 12, Bold = false };
				Draw.Text(this,"1423"+CurrentBar,false,context+"",Offset,y2,Offset,Brushes.White ,myFont, TextAlignment.Center, color, null, 1);
				Draw.Line(this,"384845"+CurrentBar,true,Offset,y1,Offset,y2,Brushes.Yellow,DashStyleHelper.Solid,2);
				}
			
		}
	
		
	
		public int CountPatternChanges(string hashPattern)
		{
		    if (string.IsNullOrWhiteSpace(hashPattern))
		        return 0;
		
		    // Split the hash pattern into individual values
		    string[] values = hashPattern.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		
		    // Initialize the count of changes
		    int changeCount = 0;
		
		    // Track the previous value
		    string previousValue = values[0];
		
		    // Iterate through the values and count the changes
		    for (int i = 1; i < values.Length; i++)
		    {
		        if (values[i] != previousValue)
		        {
		            changeCount++;
		            previousValue = values[i];
		        }
		    }
		
		    return changeCount;
		}
	
		/*
		protected int CalculateMarginToleranceQuantity()
		{
			
			
			double AccountCashvalue = IsInStrategyAnalyzer == true ? virtualCashAccount : Account.Get(AccountItem.CashValue, Currency.UsDollar);
			double accountInitialMargin = IsInStrategyAnalyzer == true? instrumentInitialMargin : Account.Get(AccountItem.InitialMargin, Currency.UsDollar);
			double accountMaintenanceMargin = IsInStrategyAnalyzer == true? instrumentDayMargin : Account.Get(AccountItem.MaintenanceMargin, Currency.UsDollar);
			double MaxMarginReq = IsInStrategyAnalyzer == true ? (AccountCashvalue*.5) : Math.Max(accountInitialMargin,accountMaintenanceMargin); // if backtest ensure 50% equity
			double excessMargin = IsInStrategyAnalyzer == true ? (AccountCashvalue - accountMaintenanceMargin) : Account.Get(AccountItem.ExcessInitialMargin, Currency.UsDollar);
			
			cashValue = virtualCashAccount;/// projection of account growth minus commissions
			//cashValue = AccountCashvalue;/// projection from 1k
			
			if(dynamicCapitalRisk == true)
			{
				perOrderCapitalStopLoss = virtualCashAccount*(perOrderMaxAccountRisk*strategyDefaultQuantity); /// Risk proportionate to account, and perOrderMaxAccountRisk now scales with quantity
			}
			else if(dynamicCapitalRisk == false)
			{

				perOrderCapitalStopLoss = HardMaxLoss; /// fixed dollar risk
				
			}
			//Print("Risk: "+perOrderCapitalStopLoss);
			double aPNL = PositionAccount.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
		    double pPNL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
			double unrealizedPNL = aPNL != 0 ? aPNL : pPNL;
			
	
	
		    actualPositionQuantity = Math.Max(PositionAccount.Quantity, PositionAccount.Quantity);
			//Print("actualPositionQuantity "+actualPositionQuantity);
		    // Calculate maximum total trade size based on equity and margin requirement
		
		    double maximumTotalTradeSize = cashValue / MaxMarginReq;
		
			double marginUsedPct = excessMargin/MaxMarginReq;
		
			double marginRemainingPct = 100 - marginUsedPct;
		    // Calculate maximum quantity of securities based on price per security
		    double pricePerSecurity = GetCurrentAsk(BarsInProgress); // Assuming price per security is $5
	
			int maxQuantityHeld = strategyMaxQuantity; // Max quantity that can be held
		
			int minQuantityBuy = strategyDefaultQuantity; // Min quantity for buying

			int maxQuantityBasedOnMargin = (int)(maximumTotalTradeSize / pricePerSecurity);
	
		
	
			 // Conditions for different scenarios...
			// if 1000 > 187*3 (ample room)
		    if (AccountCashvalue >= (MaxMarginReq * 2) || marginUsedPct <= 0.5)// we mave 2x margin
		    {
				
				
			
				
				
					return strategyDefaultQuantity;/// always open with minimum
				
		       // int newQuantity = Math.Min(minQuantityBuy, Math.Min(strategyDefaultQuantity, maxQuantityHeld - actualPositionQuantity));
		       // return newQuantity > 0 ? newQuantity : 0;
		    }
			// start making room if we have margin between 1.2 and 2
		    else if ((AccountCashvalue > (MaxMarginReq * 1.2) && AccountCashvalue < (MaxMarginReq * 2))  || ( marginUsedPct > 0.5 && marginUsedPct <= 0.75)) // if cash is less than margin, sell stuff
		    {
				//Print("Exit Some >> AccountCashvalue $"+AccountCashvalue+"     MaxMarginReq: "+MaxMarginReq+"        marginUsedPct "+marginUsedPct);
				
	
		        MarginRiskBar = CurrentBar;
		        return 0;
		    }
		    else if (AccountCashvalue <= (MaxMarginReq * 1.2) || marginUsedPct > 0.75) // if cash < margin, scramble
		    {
				//Print("EXIT ALL >> AccountCashvalue $"+AccountCashvalue+"     MaxMarginReq: "+MaxMarginReq+"        marginUsedPct "+marginUsedPct);
		        //BackBrush = Brushes.Tomato;
		     
		        return 0;
		    }
		    else
		    {
				//Print("Other margin  >> AccountCashvalue $"+AccountCashvalue+"     MaxMarginReq: "+MaxMarginReq+"        marginUsedPct "+marginUsedPct);
		        return 0;
		    }
			
			return 0;
		}
*/
	bool CheckForTrendWithFilter(ISeries<double> high, ISeries<double> low, int range, int trendSize, string trendDirection, double minPriceDifference)
	{
	    int numBarsPerPeriod = range / trendSize;

	    // Create an array to store the highest/lowest values for each period
	    double[] values = new double[5];

	    // Loop through the 5 periods and find the highest/lowest value in each
	    for (int i = 0; i < trendSize; i++)
	    {
	        // Calculate the start and end bars for the current period
	        int periodStartBar = i * numBarsPerPeriod;
	        int periodEndBar = (i + 1) * numBarsPerPeriod;

	        // Find the highest/lowest value in the current period
	        double value = (trendDirection == "up") ? double.MinValue : double.MaxValue;

	        for (int bar = periodStartBar; bar < periodEndBar; bar++)
	        {
	            double barValue = (trendDirection == "up") ? high[bar] : low[bar];

	            if ((trendDirection == "up" && barValue > value) || (trendDirection == "down" && barValue < value))
	            {
	                value = barValue;
	            }
	        }

	        values[i] = value;
	    }

	    // Now that you have the highest/lowest values for each period, compare them
	    for (int i = 1; i < trendSize; i++)
	    {
	        if ((trendDirection == "up" && values[i] <= values[i - 1]) ||
	            (trendDirection == "down" && values[i] >= values[i - 1]) ||
	            Math.Abs(values[i] - values[i - 1]) > minPriceDifference)
	        {
	            return false; // If the values are not in the specified trend direction or the price difference is too small, return false
	        }
	    }

	    return true; // If all values are in the specified trend direction with the required price difference, return true
	}


	// Define a custom function to find bars ago for CrossAbove


	
		protected string GenerateSignalId()
    	{
			// DateTime now = DateTime.Now;
	       // string currentTimeString = now.ToString("yyyyMMddHHmmssfffff")+CurrentBar;
			
	        string guidString = Guid.NewGuid().ToString("N").Substring(0, 5); // Get the first 6 characters of a new GUID
	        return guidString;
			
   		}
		
		
		
	    double GetOrderPNLDollars(Order order,int entryBar, double entryPrice)
		{
			if(entryBar == CurrentBar)
			{
				return 0;
			}
		    else
			{
		    		double priceDiff = 0;
					
					if(PositionAccount.MarketPosition == MarketPosition.Long)
					{
						priceDiff = (GetCurrentAsk(BarsInProgress) - entryPrice) * order.Quantity;
					}
					if(PositionAccount.MarketPosition == MarketPosition.Short)
					{
						priceDiff = (entryPrice - GetCurrentAsk(BarsInProgress)) * order.Quantity;
					}
		    		// Convert the PNL from ticks to dollars
					return Math.Round((priceDiff * Bars.Instrument.MasterInstrument.PointValue ),2);	
			}
		}
	
		public double avgBar(int period,double maxBarSize)
		{
		
			    double total = 0;
			    int count = 0;
			
			    for (int i = 0; i < period; i++)
			    {
			        double barSize = Math.Abs(Open[i] - Close[i]);
			        
			        if (maxBarSize > 0)
			        {
			            if (barSize < maxBarSize)
			            {
			                total += barSize;
			                count++;
			            }
			        }
			        else
			        {
			            total += barSize;
			            count++;
			        }
			    }
			
			    if (count > 0)
			    {
			        return total / count;
			    }
			    return 0;
		}
		public double avgVol(int period)
		{
		
			double avg = 0;
				
			for(int i = period; i > 0; i--)	
			{
				
				avg += Volume[i];
				
			}
			if(avg > 0)
			{
				avg = avg/period;
				return avg;
			}
			return 0;
		}
		public bool stdDevChannel(double deviation)
		{
		
			double avg = 0;
			double thisBarRange = Math.Abs(Open[1]-Close[1]);
			for(int i = 12; i > 0; i--)	
			{
				if(thisBarRange > Math.Abs(Open[i]-Close[i])*deviation)
				{
					return true;
				}
			
			}
			return false;
		}
				
		private bool IsValueInArray(int[] array, int targetValue)
		{
		    for (int i = 0; i < array.Length; i++)
		    {
		        if (array[i] >= (CurrentBar - targetValue))
		        {
		            return true; // Value found in the array
		        }
		    }

		    return false; // Value not found in the array
		}	
		
		private int CountValuesInArray(int[] array, int targetValue)
		{
			int ct = 0;
		    for (int i = 0; i < array.Length; i++)
		    {
		        if (array[i] >= 0)
		        {
					ct++;
		            
		        }
				
		    }

		    return ct; // Value not found in the array
		}
		


		protected void RTPrint(string message)
		{
		
			if(State == State.Realtime)
			{
				Print(Instrument.FullName + " STRATEGY "+strategyName+" ( "+CurrentBars[0]+") "+message);
		    }		
		}
		
		protected void DebugPrint(debugSection section,string message)
		{
			lastDebugPoint = (Instrument.FullName+" BAR ( "+CurrentBars[0]+")"+section+" "+message);
		    if (DebugMode)
		    {
				
					if(DebugFilter == section)
					{
						
					Print(Instrument.FullName+" BAR ( "+CurrentBars[0]+")"+section+" "+message);
					}
					
					else if(DebugFilter == debugSection.none)
					{  
					Print(Instrument.FullName+" BAR ( "+CurrentBars[0]+") "+message);
					}
		       
		    }
			
		
		}
		
		protected void DebugPrint(string filter,int message)
		{
			
		    if (DebugMode)
		    {
				
		        Print(message);
				
		    }
			
		}
		
		protected void DebugPrint(string filter,double message)
		{
		   if (DebugMode)
		    {
				
		        Print(message);
				
		    }
			
		}
		
		protected void DebugPrint(string filter,Order message)
		{
			
		   if (DebugMode)
		    {
				
		        Print(message);
				
		    }
			
		}
		public double MinOfTwo(double a, double b) 
		{
   		 return (a < b) ? a : b;
		}
		
		public double MaxOfTwo(double a, double b) 
		{
   		 return (a > b) ? a : b;
		}
		
		
	
		
		
		

		
		
		
	
		
	
		



		
	}
}
