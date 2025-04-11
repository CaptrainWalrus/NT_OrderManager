//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.ComponentModel.DataAnnotations;
	using System.Linq;
	using System.Windows.Media;
	using NinjaTrader.Cbi;
	using NinjaTrader.Gui;
	using NinjaTrader.Gui.Tools;
	using NinjaTrader.Data;
	using NinjaTrader.NinjaScript;
	using NinjaTrader.Core.FloatingPoint;
	using NinjaTrader.NinjaScript.Indicators;
	using System.Threading.Tasks;
	using Newtonsoft.Json;
	using System.Collections.Concurrent;
	using System.Xml.Serialization;

public partial class CurvesStrategy : MainStrategy
{
	// Mark non-serializable fields with XmlIgnore
	
	
	[XmlIgnore]
	private bool somethingWasConnected;
	
	// Make properties fully public or XmlIgnore if they shouldn't be serialized

	
	[XmlIgnore]
	private bool terminatedStatePrinted = false;
	
	// Comment out most custom fields
	// private readonly TimeSpan updateInterval = TimeSpan.FromMilliseconds(100);
	// private DateTime lastUpdate = DateTime.MinValue;
	// private DateTime lastProcessedTime = DateTime.MinValue;
	// private int lastProcessedBar = -1;
	// private bool isProcessingQueue;
	// private Dictionary<string, double> signalStrengths = new Dictionary<string, double>();
	// private Dictionary<string, DateTime> lastSignalTimes = new Dictionary<string, DateTime>();
	// public static double CurrentResistance { get; private set; } 
	// public static double CurrentSupport { get; private set; }
	// public static double ConfluencePair { get; private set; }
	// public double LastBullStrength = double.MinValue;
	// public double LastBearStrength = double.MinValue;
	// public string timestamp_bar;
	// public double high_bar;
	// public double low_bar;
	// public double open_bar;
	// public double close_bar;
	// public int volume_bar;
	// public bool historicalSync = false;
	// private DateTime backtestStartTime; 
	// private DateTime? firstBarTime; 

	// Restore NinjaScript Properties
	[NinjaScriptProperty]    
	[Display(Name="Use Remote Service", Order=0, GroupName="Class Parameters")]
	public bool UseRemoteService { get; set; }
	
	[NinjaScriptProperty]    
	[Display(Name="Stream Historical Data", Order=2, GroupName="Class Parameters")]
	public bool HistoricalData { get; set; }


	private int stopTest = 0;

	// Make the Signal class serializable
	[Serializable]
	public class Signal
	{
		public string type { get; set; }
		public double confidence { get; set; }
		public string timestamp { get; set; }
		public string pattern_id { get; set; }
		public string pattern_name { get; set; }
		public string pattern_type { get; set; }
		public string direction { get; set; }
		public float entry { get; set; }
		public float target { get; set; }
		public float stop { get; set; }
	}

	protected override void OnStateChange()
	{
		// *** Call base FIRST to let MainStrategy do its setup ***
		base.OnStateChange(); 
		
		if (State == State.SetDefaults)
		{   
			// Defaults are set as per previous step
			Description = "Curves strategy"; 
			Name = "Curves"; 
			Calculate = Calculate.OnBarClose;
			EntriesPerDirection = 1;
			EntryHandling = EntryHandling.UniqueEntries;
			IsExitOnSessionCloseStrategy = false;
			IsFillLimitOnTouch = false;
			MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
			OrderFillResolution = OrderFillResolution.Standard;
			Slippage = 0;
			StartBehavior = StartBehavior.WaitUntilFlatSynchronizeAccount;
			TimeInForce = TimeInForce.Gtc;
			TraceOrders = false;
			RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
			StopTargetHandling = StopTargetHandling.PerEntryExecution;
			BarsRequiredToTrade = 20;
			IsUnmanaged = true;
			
			// Restore original strategy-specific defaults 
			// (Assuming these were the correct ones from MicroStrategy)
			microContractStoploss = 50;
			microContractTakeProfit = 150;
			softTakeProfitMult = 20;
			entriesPerDirectionSpacingTime = 1; // Default might need checking
			PullBackExitEnabled = true;
			TakeBigProfitEnabled = true;
			signalsOnly = true;
			somethingWasConnected = false;
			// Set default for new property
			UseDirectSync = false; // Enable synchronous mode by default
		}
		// *** Restore Configure block ***
		else if (State == State.Configure)
		{
			NinjaTrader.Code.Output.Process("CurvesStrategy.OnStateChange: State.Configure", PrintTo.OutputTab1);
		}
		else if (State == State.DataLoaded) 
		{   
			Print("CurvesStrategy.OnStateChange: Handling State.DataLoaded");
			// Initialize the service asynchronously
			
		

		}
		// *** Restore Historical block structure ***
		else if (State == State.Historical)
		{
			Print("CurvesStrategy.OnStateChange: State.Historical (No custom logic)");
			// Custom logic (bar iteration, etc.) remains commented out
		}
		// *** Restore Terminated block structure ***
		else if (State == State.Terminated)
		{
			if(terminatedStatePrinted==false)
			{
				
				
				// 3. (Removed WebSocket handling since it's in the service dispose method)
				
				// 4. Clear local collections
				Print("4. Clearing local collections");
				CurrentBullStrength = 0;
				CurrentBearStrength = 0;
				
				// 5. (Removed ProcessQueue setting since we're now using fire-and-forget)
				
				// 6. Force garbage collection to clean up lingering resources
				Print("6. Running aggressive garbage collection");
				GC.Collect(2, GCCollectionMode.Forced);
				GC.WaitForPendingFinalizers();
				
				Print("COMPLETE SHUTDOWN SEQUENCE FINISHED");
				terminatedStatePrinted = true;
			}
		}
	}
	////////////////////////////
	//////////////////////////// ON BAR UPDATE
	////////////////////////////
	protected override void OnBarUpdate()
	{
		base.OnBarUpdate(); 
		
		try
		{
			// Skip processing if service isn't available
			if (curvesService == null)
			{
				if (CurrentBars[0] % 10 == 0) // Only log occasionally
				NinjaTrader.Code.Output.Process("OnBarUpdate: CurvesV2Service not available", PrintTo.OutputTab1);
				return;
			}
			
			
			bool isConnected = curvesService.IsConnected;
				
			
			
			
			if (BarsInProgress != 0) return;
			if (CurrentBars[0] < BarsRequiredToTrade) return;
			// Log connection status periodically
			CurvesV2Service.CurrentContextId = null;
			// SIMPLIFIED APPROACH: Direct SendBar and UpdateSignals
			if (isConnected && BarsInProgress == 0)
			{
				// Extract instrument code
				string instrumentCode = GetInstrumentCode();
			
				// 1. Simple, direct send of bar data - fire and forget
				bool barSent = curvesService.SendBarFireAndForget(
					instrumentCode,
					Time[0],
					Open[0],
					High[0],
					Low[0],
					Close[0],
					Volume[0],
					IsInStrategyAnalyzer ? "backtest" : "1m"
				);
				//Print($"{Time[0]} : barSent {barSent}");
				// 2. Simple, direct signal check - fire and forget  
				if (barSent)
				{
					//Print($"{Time[0]} : Check Signal");
					curvesService.CheckSignalsFireAndForget(Time[0].ToString(),instrumentCode);
				}
				
				// 3. Read current signals from static properties
				UpdateLocalSignalData();
			
			}
			
		}
		catch (Exception ex)
		{
			NinjaTrader.Code.Output.Process($"Error in OnBarUpdate: {ex.Message}", PrintTo.OutputTab1); 
		}
	}
	// Fix BuildNewSignal to actually return entry signals
	protected override FunctionResponses BuildNewSignal()
	{
		//Print($"FunctionResponses Begin: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}");
	    FunctionResponses FRS = FunctionResponses.NoAction;
	    
	    if(CurrentBars[0] < BarsRequiredToTrade)
	    {
	        return FRS;
	    }
	    if(BarsInProgress != 0)
	    {
	        return FRS;
	    }
	    
	    // Calculate total positions and working orders
	    int totalPositions = getAllcustomPositionsCombined();
	    
	    // Debug output for diagnostic purposes
	    if (CurrentBars[0] % 20 == 0)
	    {
	        Print($"Position check: totalPositions={totalPositions}, accountMaxQuantity={accountMaxQuantity}");
	    }
	    
	    // Check if we have room for more positions
	    TimeSpan timeSinceLastThrottle = Times[BarsInProgress][0] - ThrottleAll;
	    
	    // Critical safety limit - don't allow more than the max quantity
	    if (totalPositions >= accountMaxQuantity )
	    {
	        Print($"*** SAFETY HALT - Position limit reached: positions={totalPositions},  max={accountMaxQuantity}");
	        return FunctionResponses.NoAction;
	    }
	    
	    // Regular position check with throttle timing
	    if (totalPositions < accountMaxQuantity && 
	        timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))
	    {
			
		        if( VWAP1[0] < EMA3[0] && CurrentBullStrength > CurrentBearStrength * 2 && CurrentBullStrength > .8 && GetMarketPositionByIndex(1) != MarketPosition.Short)
		        {
		            Print($"LONG SIGNAL: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}");
		            //forceDrawDebug($"+{CurrentBullStrength}",1,0,High[0]+(TickSize*20),Brushes.Lime,true);
		            //forceDrawDebug($"{totalPositions}",1,0,High[0]+(TickSize*20),Brushes.Lime,true);
	
		            ThrottleAll = Times[BarsInProgress][0];
		            return FunctionResponses.EnterLong; // Actually return signal instead of NoAction
		        }
		        
		        if( VWAP1[0] > EMA3[0] &&CurrentBearStrength > CurrentBullStrength * 2 && CurrentBearStrength > .8 && GetMarketPositionByIndex(1) != MarketPosition.Long)
		        {
		            Print($"SHORT SIGNAL: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}");
		            //forceDrawDebug($"-{CurrentBearStrength}",1,0,Low[0]-(TickSize*20),Brushes.Red,true);
		            //forceDrawDebug($"{totalPositions}",1,0,Low[0]-(TickSize*20),Brushes.Red,true);
		            ThrottleAll = Times[BarsInProgress][0];
		            return FunctionResponses.EnterShort; // Actually return signal instead of NoAction
		        }
			
	    }
	    
	    return FunctionResponses.NoAction;
	}
	// Keep ProcessSignal commented out
	// private void ProcessSignal(dynamic signal) { ... }

	

	// Keep the original methods but don't use them
	private void ProcessBarData() 
	{
		// Original implementation preserved but not used
	}

	private void ProcessBarDataSync()
	{
		// Original implementation preserved but not used
	}

	// Restore GetInstrumentCode method
	private string GetInstrumentCode()
	{
		string instrumentCode = Instrument?.FullName?.Split(' ')?.FirstOrDefault() ?? "";
		if (string.IsNullOrEmpty(instrumentCode))
		{
			Print("Warning: Unable to determine instrument code - using fallback");
			instrumentCode = "UNKNOWN"; // Or handle appropriately
		}
		return instrumentCode;
	}

	// Restore SendBarData method definition
	protected bool SendBarData(bool isHistorical = false)
	{
		// Don't send if we're terminated
		if (State == State.Terminated)
		{
			Print("SendBarData: State == State.Terminated - skipping data send");
			return false;
		}

		// Skip sending bar if we're below the minimum bars required
		if (CurrentBars[0] < BarsRequiredToTrade)
		{
			return false;
		}
		
		// Skip sending bar to server if we don't have CurvesService initialized
		if (curvesService == null)
		{
			 Print("SendBarData: curvesService is null, skipping send."); // Added log
			return false;
		}
		
		// Extract instrument code safely
		string instrumentCode = GetInstrumentCode();
		
		// Use QueueBar instead of SendBar
		// Print($"SendBarData: Queueing bar {CurrentBar} for {instrumentCode}"); // Optional detailed log
		bool sent = curvesService.QueueBar(
			instrumentCode,
			Time[0],
			Open[0],
			High[0],
			Low[0],
			Close[0],
			Volume[0],
			isHistorical ? "backtest" : "1m" // Note: Original code used "1m" fixed, changed to match isHistorical
		);
		
		return sent;
	}

	
	// Restore UpdateLocalSignalData method definition
	private void UpdateLocalSignalData()
	{
		try
		{
			// Access the static properties updated by the service
			CurrentBullStrength = CurvesV2Service.CurrentBullStrength;
			CurrentBearStrength = CurvesV2Service.CurrentBearStrength;
			
			// Check if signals are fresh
			bool signalsFresh = CurvesV2Service.SignalsAreFresh;
			
			// Update patterns - safely
			if (CurvesV2Service.CurrentMatches != null)
			{
				
				
		
				
				// Only log if we have actual signal strength and not in backtest mode
				if (!IsInStrategyAnalyzer && (CurrentBullStrength > 0 || CurrentBearStrength > 0) && CurrentBars[0] % 20 == 0)
				{
					Print($"Signal state: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}, Fresh={signalsFresh}");
				}
			}
			else if (!IsInStrategyAnalyzer && CurrentBars[0] % 100 == 0) 
			{
				Print("No patterns in signal response");
				
			}
		}
		catch (Exception ex)
		{
			Print($"Error in UpdateLocalSignalData: {ex.Message}");
			
		}
	}

	// Restore ConvertPatternsToSignals method definition
	private List<Signal> ConvertPatternsToSignals(List<PatternMatch> patterns)
	{
		try
		{
			if (patterns == null || !patterns.Any()) 
				return new List<Signal>();
				
			var result = new List<Signal>();
			
			foreach (var p in patterns)
			{
				// Skip null entries
				if (p == null) continue;
				
				try
				{
					// Create signal with careful null handling
					var signal = new Signal 
					{
						type = p.patternType ?? "unknown",
						confidence = p.confidence,
						timestamp = DateTime.UtcNow.ToString("O"), // Use current time as approximation
						pattern_id = p.id ?? Guid.NewGuid().ToString(),
						pattern_name = p.patternName ?? "Unknown Pattern",
						pattern_type = p.patternType ?? "unknown",
						direction = (p.patternType != null && p.patternType.ToLower().Contains("bull")) ? "long" : "short", // Simple direction guess
						entry = p.entry.HasValue ? (float)p.entry.Value : 0f,
						target = p.target.HasValue ? (float)p.target.Value : 0f,
						stop = p.stop.HasValue ? (float)p.stop.Value : 0f
					};
					
					result.Add(signal);
				}
				catch (Exception ex)
				{
					Print($"Error converting individual pattern to signal: {ex.Message}");
					// Continue with next pattern
				}
			}
			
			return result;
		}
		catch (Exception ex)
		{
			Print($"Error in ConvertPatternsToSignals: {ex.Message}");
			return new List<Signal>();
		}
	}
}
}