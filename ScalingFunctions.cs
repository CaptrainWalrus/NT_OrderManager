#region
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
	public globalThompsonValue overallModifier = new globalThompsonValue();
	protected int globalStopStart;	

	private double avgDistance;
	private double scoreSL_Long;
	private double scoreTP_Long;
	private double scoreSTP_Long;
	private double scoreSL_Short;
	private double scoreTP_Short;
	private double scoreSTP_Short;	
	private double previousMaxScore = double.MinValue;
	private int lastSelectedBarsInProgress;
	private double switchThreshold = 0.4; // Example threshold value
	private double smoothedAdaptiveSL_Long;
	private double smoothedAdaptiveTP_Long;
	private double smoothedAdaptiveSTP_Long;
	private double smoothedAdaptiveSL_Short;
	private double smoothedAdaptiveTP_Short;
	private double smoothedAdaptiveSTP_Short;
	
	private double adjustedMaxLossLong;
	private double adjustedTakeProfitLong;
	private double adjustedSoftTPLong;
	
	private double adjustedMaxLossShort;
	private double adjustedTakeProfitShort;
	private double adjustedSoftTPShort;

		
	public enum ContractType
	{
	    Standard,
	    Mini,
	    Micro
	}
	double GetTrendStrength()
	{
	    double adxValue = ADX(14)[3];
	    return Math.Min(Math.Max(adxValue / 20.0, 0.0), 1.0);  // Scale ADX between 0 and 1
	}
	// Declare smoothing factor (between 0 and 1). Lower values = more smoothing.
	double smoothingFactor = 0.15;  // Adjust this value for more or less smoothing
	
	// Smooth function to apply exponential smoothing
	private double SmoothValue(double previousValue, double newValue, double factor)
	{
	    return (factor * newValue) + ((1 - factor) * previousValue);
	}
	 ///original
public void updateSLTP(int stopPeriod, int profitPeriod, double slmult, double tpmult, double sftpmult)
{
    // Baseline values
    HardMaxLoss_Long = slmult;
    HardTakeProfit_Long =  tpmult;
    SoftTakeProfit_Long =  sftpmult;

    HardMaxLoss_Short =  slmult;
    HardTakeProfit_Short =  tpmult;
    SoftTakeProfit_Short =  sftpmult;
	
    // Small increment for gradual adjustment
	double emaroc = Math.Abs(EMA3[0]-EMA3[1]);
    double gradualIncrement = TickSize * 1;  // Adjust this factor as needed for smoother scaling

   // UNRPNL.Update();

    // Initialize adjusted values if they haven't been set yet
    adjustedMaxLossLong = adjustedMaxLossLong > 0 ? adjustedMaxLossLong : slmult;
    adjustedTakeProfitLong = adjustedTakeProfitLong > 0 ? adjustedTakeProfitLong : tpmult;
    adjustedSoftTPLong = adjustedSoftTPLong > 0 ? adjustedSoftTPLong : sftpmult;

    adjustedMaxLossShort = adjustedMaxLossShort > 0 ? adjustedMaxLossShort : slmult;
    adjustedTakeProfitShort = adjustedTakeProfitShort > 0 ? adjustedTakeProfitShort : tpmult;
    adjustedSoftTPShort = adjustedSoftTPShort > 0 ? adjustedSoftTPShort : sftpmult;

	
	

	 
    // Adjust for a long position with positive unrealized PNL and upward trend
    if (IsRising(EMA3))
    {
        // Incrementally expand the stop-loss and take-profit levels
        adjustedMaxLossLong += gradualIncrement;
        adjustedTakeProfitLong += gradualIncrement;
        adjustedSoftTPLong += gradualIncrement;

        // Apply limits to ensure the values do not exceed daily max limits
        adjustedMaxLossLong = Math.Min(adjustedMaxLossLong, dailyProfitMaxLoss);
        adjustedTakeProfitLong = Math.Min(adjustedTakeProfitLong, dailyProfitGoal);
        adjustedSoftTPLong = Math.Min(adjustedSoftTPLong, dailyProfitGoal);

        // Apply the adjusted values
		
		
        HardMaxLoss_Long = adjustedMaxLossLong ;
        HardTakeProfit_Long = adjustedTakeProfitLong ;
        SoftTakeProfit_Long = adjustedSoftTPLong ;

        
    }
    // Adjust for a short position with positive unrealized PNL and downward trend
    else if (IsFalling(EMA3))
    {
        // Incrementally expand the stop-loss and take-profit levels
        adjustedMaxLossShort += gradualIncrement;
        adjustedTakeProfitShort += gradualIncrement;
        adjustedSoftTPShort += gradualIncrement;

        // Apply limits to ensure the values do not exceed daily max limits
        adjustedMaxLossShort = Math.Min(adjustedMaxLossShort, dailyProfitMaxLoss);
        adjustedTakeProfitShort = Math.Min(adjustedTakeProfitShort, dailyProfitGoal);
        adjustedSoftTPShort = Math.Min(adjustedSoftTPShort, dailyProfitGoal);

        // Apply the adjusted values
        HardMaxLoss_Short = adjustedMaxLossShort ;
        HardTakeProfit_Short = adjustedTakeProfitShort ;
        SoftTakeProfit_Short = adjustedSoftTPShort ;

       
    }
	
    // Update tracker with the final values
    sLTPTracker.UpdateValues(
        -HardMaxLoss_Long, HardTakeProfit_Long, SoftTakeProfit_Long,
        -HardMaxLoss_Short, HardTakeProfit_Short, SoftTakeProfit_Short
    );
}

		public void AdjustLong(double expandFactor,double contractFactor)
		{
			double scale = Body(1)* Bars.Instrument.MasterInstrument.PointValue;
		    HardMaxLoss_Long += scale;
		    HardTakeProfit_Long += scale;
		    SoftTakeProfit_Long += scale;
			
			
		}
		
		public void AdjustShort(double expandFactor,double contractFactor)
		{
			double scale = Body(1)* Bars.Instrument.MasterInstrument.PointValue;
			HardMaxLoss_Short += scale;
		    HardTakeProfit_Short += scale;
		    SoftTakeProfit_Short += scale;
			
		}
	
	
	
		// Declare smoothing factor (between 0 and 1). Lower values = more smoothing.
   		// Generate Thompson Sampling scores (function to avoid duplication)
	   // double GetThompsonScore(globalThompsonValue thompson, bool thompsonLock)
	   // {
	      //  return !thompsonLock ? BetaSample(thompson.Alpha, thompson.Beta, rand, DisableThompsonSampling) : 1;
	   // }
		// Adjust SoftTakeProfit based on trade performance
	    void AdjustSoftTakeProfit(ref double softTakeProfit, double hardTakeProfit)
	    {
	        double efficiency = SystemPerformance.AllTrades.TradesPerformance.AverageExitEfficiency;
	        softTakeProfit *= (efficiency > 0.25 ? 1 + efficiency : efficiency);
	        if (softTakeProfit > hardTakeProfit)
	        {
	            softTakeProfit = hardTakeProfit * 0.9; // Set buffer below HardTakeProfit
	        }
	    }
	
 		double CalculateAdaptiveValue(double score, double atrValue, double multiplier, ref double smoothedValue)
	    {
	        double adaptiveValue = atrValue * score * multiplier;
	        double smoothingFactor = 0.1;
	        smoothedValue = smoothingFactor * adaptiveValue + (1 - smoothingFactor) * smoothedValue;
	        return smoothedValue;
	    }

		
		public double ContractExpand(double value, int expandOrContract, double expandFactor, double contractFactor)
		{
		    if (expandOrContract > 0)
		    {
		        // Expand the value by a certain percentage
		        value *= (1 + expandFactor);
		    }
		    else if(expandOrContract < 0)
		    {
		        // Contract the value by a certain percentage
		        value *= (1 - contractFactor);
		    }
		
		    return value;
		}

	
		
	




	

	
	
	}
  
}
