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
	using NinjaTrader.NinjaScript.DrawingTools;
	using NinjaTrader.Gui.Chart;
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
			Calculate = Calculate.OnPriceChange;
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
			IsUnmanaged = false;
			
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
				Print($"CurvesStrategy.OnStateChange: State.Terminated entered for instance {this.GetHashCode()}. Running cleanup if needed.");
				terminatedStatePrinted = true;
				
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
			if(curvesService != null)
			{
				if(curvesService.ErrorCounter > 10)
				{
					Print($"[ERROR COUNTER VIOLATION] {curvesService.ErrorCounter}");
					return;
				}
			}
			
			
			bool isConnected = curvesService.IsConnected;
				
			
			
			
			if (BarsInProgress != 0)
			{
				//Print($"{Time[0]} BarsInProgress {BarsInProgress} BAR # {CurrentBars[0]}");
				return;
			}
			///small status update of progress
			if(BarsInProgress == 0 && CurrentBars[0] % 10 == 0)
			{
				Print($"{Time[0]} BarsInProgress {BarsInProgress} BAR # {CurrentBars[0]}");
			}
			
			if (CurrentBars[0] < BarsRequiredToTrade) return;
			
			
			// Log connection status periodically
			CurvesV2Service.CurrentContextId = null;
			// SIMPLIFIED APPROACH: Direct SendBar and UpdateSignals
			if (isConnected && BarsInProgress == 0)
			{
				//Print($"{Time[0]} isConnected");

				// Extract instrument code
				string instrumentCode = GetInstrumentCode();
			
				// 1. Simple, direct send of bar data - fire and forget
				bool barSent = curvesService.SendBarFireAndForget(
					UseRemoteService,
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
					

					Task.Delay(5).Wait(); 
					//Print($"{Time[0]} : CheckSignalsFireAndForget!");
					curvesService.CheckSignalsFireAndForget(UseRemoteService,Time[0],instrumentCode,null,OutlierScoreRequirement,effectiveScoreRequirement, null);
					
				}
			
			}
			
		}
		catch (Exception ex)
		{
			NinjaTrader.Code.Output.Process($"Error in OnBarUpdate: {ex.Message}", PrintTo.OutputTab1); 
		}
	}
	
	// Fix BuildNewSignal to actually return entry signals
	protected override patternFunctionResponse BuildNewSignal()
	{
		//Print($"[DEBUG] BuildNewSignal Begin: Bull={CurvesV2Service.CurrentBullStrength:F2}%, Bear={CurvesV2Service.CurrentBearStrength:F2}%, RawScore={CurvesV2Service.CurrentRawScore:F2}, PatternType={CurvesV2Service.CurrentPatternType}");
	    patternFunctionResponse thisSignal = new patternFunctionResponse();
		thisSignal.newSignal = FunctionResponses.NoAction;
	    thisSignal.patternSubType = "none";
		thisSignal.patternId = "";
	    if(CurrentBars[0] < BarsRequiredToTrade)
	    {
	        return thisSignal;
	    }
	    if(BarsInProgress != 0)
	    {
	        return thisSignal;
	    }
	    
	    // Calculate total positions and working orders
	    int totalPositions = getAllcustomPositionsCombined();
	    // Check if we have room for more positions
	    TimeSpan timeSinceLastThrottle = Times[BarsInProgress][0] - ThrottleAll;
	
	    
	    
	    // Critical safety limit - don't allow more than the max quantity
	    if (totalPositions >= accountMaxQuantity )
	    {
	        Print($"*** SAFETY HALT - Position limit reached: positions={totalPositions},  max={accountMaxQuantity}");
	        return thisSignal;
	    }
	   
	    // Regular position check with throttle timing
	    //if (totalPositions < accountMaxQuantity && timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))
		if (Math.Max(totalPositions,Position.Quantity) < EntriesPerDirection && Math.Max(totalPositions,Position.Quantity) < accountMaxQuantity && timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))

	    {      // Get current signal values
	            double currentBullStrength = CurvesV2Service.CurrentBullStrength;
	            double currentBearStrength = CurvesV2Service.CurrentBearStrength;
             
				
			        if (CurvesV2Service.SignalsAreFresh && (CurvesV2Service.CurrentSubtype == patternSubtypesPicker.ToString() || patternSubtypesPicker == patternSubtypes.All))
			        {
			      
		
			            // Enhanced logging to debug signal values
			           // Print($"[DEBUG] Signal details: Bull={currentBullStrength:F2}%, Bear={currentBearStrength:F2}%, PatternType={CurvesV2Service.CurrentPatternType}, PatternId={CurvesV2Service.CurrentPatternId}");
				            if(currentBullStrength > currentBearStrength)
							{
								Print($"[{Time[0]}] LONG SIGNAL FROM CURVESV2: Bull={currentBullStrength}  [subtype] {CurvesV2Service.CurrentSubtype} ");
	
								
				            		// Check for Long signal (strength and ratio conditions)
						            if (CurvesV2Service.CurrentRawScore > OutlierScoreRequirement)// && CurvesV2Service.CurrentEffectiveScore > effectiveScoreRequirement)
						            {
										/*if(
											(IsRising(EMA3) && EMA3[0] > VWAP1[0] && CurvesV2Service.CurrentSubtype == patternSubtypes.Trending.ToString()) || 
											(CrossAbove(EMA3,VWAP1,5) && CurvesV2Service.CurrentSubtype == patternSubtypes.Reversion.ToString()) || 
											(ATR(5)[0] < 1 && CurvesV2Service.CurrentSubtype == patternSubtypes.Breakout.ToString()) ||
											(CurvesV2Service.CurrentSubtype == patternSubtypes.Consolidation.ToString())
											)
										{*/
							                ThrottleAll = Times[BarsInProgress][0];
							                thisSignal.newSignal = FunctionResponses.EnterLong;
											thisSignal.patternSubType = CurvesV2Service.CurrentSubtype;
											thisSignal.patternId = CurvesV2Service.CurrentPatternId.ToString();
											Print($"[DEBUG] Returning LONG signal with patternId={thisSignal.patternId}, subtype={thisSignal.patternSubType}");
											return thisSignal;
											
						            	//}
									}
								
				            }
							if(currentBearStrength > currentBullStrength)
							{
								Print($"[{Time[0]}] SHORT SIGNAL FROM CURVESV2: Bear={currentBearStrength} [subtype] {CurvesV2Service.CurrentSubtype}");
	
							
						            if (CurvesV2Service.CurrentRawScore > OutlierScoreRequirement)//&& CurvesV2Service.CurrentEffectiveScore > effectiveScoreRequirement)
						            {
										/*if(
											(IsFalling(EMA3) && VWAP1[0] > EMA3[0]  && CurvesV2Service.CurrentSubtype == patternSubtypes.Trending.ToString()) || 
											(CrossAbove(VWAP1,EMA3,5) && CurvesV2Service.CurrentSubtype == patternSubtypes.Reversion.ToString()) || 
											(ATR(5)[0] < 1 && CurvesV2Service.CurrentSubtype == patternSubtypes.Breakout.ToString()) ||
											(CurvesV2Service.CurrentSubtype == patternSubtypes.Consolidation.ToString())
											)
										{*/	
										
											ThrottleAll = Times[BarsInProgress][0];
							               	thisSignal.newSignal = FunctionResponses.EnterShort;
											thisSignal.patternSubType = CurvesV2Service.CurrentSubtype;
											thisSignal.patternId = CurvesV2Service.CurrentPatternId.ToString();
											Print($"[DEBUG] Returning SHORT signal with patternId={thisSignal.patternId}, subtype={thisSignal.patternSubType}");
											return thisSignal;
						            	//}
									}
							
							}
							else
							{
								Print($"[DEBUG] SKIP: Bull={currentBullStrength:F2}% , Bear={currentBearStrength:F2}% |||| [confluenceScore] {CurvesV2Service.CurrentConfluenceScore} , [oppositionStrength] {CurvesV2Service.CurrentOppositionStrength} [effectiveScore] {CurvesV2Service.CurrentEffectiveScore:F2}, [rawScore] {CurvesV2Service.CurrentRawScore:F2}");
		
							}
			        	
					}
				
		        
		        
	    }
		//Print("[DEBUG] BuildNewSignal: No signals generated");
	    return thisSignal;
	    
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