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
	
	private bool historicalBarsSent = false;
	// Restore NinjaScript Properties
	[NinjaScriptProperty]    
	[Display(Name="Use Remote Service", Order=0, GroupName="Class Parameters")]
	public bool UseRemoteServiceParameter { get; set; }
	
	public bool UseRemoteService = false;
	

	
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
			if(IsInStrategyAnalyzer)
			{
				UseRemoteService = false;
			}
			else if(!IsInStrategyAnalyzer)
			{
				UseRemoteService = true;
			}
			Print($"CurvesStrategy.OnStateChange: State.Historical UseRemoteService {UseRemoteService}");
			// Custom logic (bar iteration, etc.) remains commented out
		}
		else if (State == State.Realtime)
		{
			if(UseRemoteServiceParameter == true)
			{
				UseRemoteService = true;
			}
			Print($"CurvesStrategy.OnStateChange: State.Realtime UseRemoteService {UseRemoteService}");
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

		DebugFreezePrint("CurvesStrategy OnBarUpdate START");
		base.OnBarUpdate(); 
		DebugFreezePrint("Base OnBarUpdate completed");
		
		try
		{
			DebugFreezePrint("Service availability check");
			// Skip processing if service isn't available
			if (curvesService == null)
			{
				if (CurrentBars[0] % 10 == 0) // Only log occasionally
				Print("OnBarUpdate: CurvesV2Service not available");
				return;
			}
			if(curvesService != null)
			{
				if(curvesService.ErrorCounter > 10)
				{
					Print($"[ERROR COUNTER VIOLATION] {curvesService.ErrorCounter}, {Time[0]}");
					return;
				}
			}
			if(IsInStrategyAnalyzer && CurrentBars[0] % 12 == 0)
			{
			 Print($"{Time[0]}");
			}
			DebugFreezePrint("Heartbeat check");
			// In OnBarUpdate or a timer
			if (UseRemoteService == true && BarsInProgress == 1) // Send heartbeats on 5-second series (BarsInProgress 1)
			{
			    //Print("Heartbeat, now truly fire-and-forget");
			    curvesService?.CheckAndSendHeartbeat(UseRemoteService);
				
				Print($" Last Heartbeat : {curvesService.lastHeartbeat}, Current Time == {{Time[0]}}");
			    //Print("Heartbeat call returned - strategy continues");
			}
			DebugFreezePrint("Heartbeat completed");
						
			bool isConnected = curvesService.IsConnected;
			if (CurrentBars[0] < BarsRequiredToTrade) return;
			// Only send historical bars once, when we have enough data
		    if (UseRemoteService == false && BarsInProgress == 0 && sendHistoricalBars && !historicalBarsSent && CurrentBar >= 1) // Wait for 50000 bars
		    {
		    	DebugFreezePrint("Historical bars processing START");
		        // Prepare historical bars
		        var historicalBars = new List<object>();
		        
		        // Collect the last 1500 bars (reverse order for chronological sequence)
		       // for (int i = 50000; i >= 0; i--)
		        //{
		            historicalBars.Add(new
		            {
		                timestamp = Time[0],
		                open = Open[0],
		                high = High[0],
		                low = Low[0],
		                close = Close[0],
		                volume = Volume[0]
		            });
		        //}
		        
		        Print($"Starting synchronous send of {historicalBars.Count} historical bars to IBI Analysis Tool...");
		        
		        // Use synchronous wrapper instead of Task.Run to prevent concurrent processing
		        try
		        {
		        	DebugFreezePrint("About to call SendHistoricalBarsSync");
		            bool success = SendHistoricalBarsSync(historicalBars, Instrument.MasterInstrument.Name);
		            DebugFreezePrint("SendHistoricalBarsSync completed");
		            
		            if (success)
		            {
		                Print("Historical bars sent successfully to IBI Analysis Tool!");
		                historicalBarsSent = true;
		            }
		            else
		            {
		                Print("Failed to send historical bars to IBI Analysis Tool");
		            }
		        }
		        catch (Exception ex)
		        {
		            Print($"Error sending historical bars: {ex.Message}");
		        }
		        DebugFreezePrint("Historical bars processing COMPLETE");
		        
		        return; // Skip normal processing this bar
		    }
		
			
			
			// Log connection status periodically
			CurvesV2Service.CurrentContextId = null;
			// SIMPLIFIED APPROACH: Direct SendBar and UpdateSignals
			if (isConnected && BarsInProgress == 0 && IsFirstTickOfBar)
			{
				DebugFreezePrint("Connected bar processing START");
				//Print($"{Time[0]} isConnected, SEND");
				
				// Extract instrument code
				string instrumentCode = GetInstrumentCode();
			
				// 1. Simple, direct send of bar data - fire and forget
				DebugFreezePrint("About to call SendBarFireAndForget");
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
				DebugFreezePrint("SendBarFireAndForget completed");
				//Print($"{Time[0]} : barSent {barSent}");
				// 2. PARALLEL signal check - no delay, no dependency on barSent
				DebugFreezePrint("About to call CheckSignalsFireAndForget");
				curvesService.CheckSignalsFireAndForget(UseRemoteService,Time[0],instrumentCode,null,OutlierScoreRequirement,effectiveScoreRequirement, null);
				DebugFreezePrint("CheckSignalsFireAndForget completed");
				double currentPrice = Close[0];  // Use the current bar's close price
			    curvesService.UpdateAllDivergenceScoresAsync(currentPrice);
				
			}
			
			
		}
		catch (Exception ex)
		{
			DebugFreezePrint($"ERROR in CurvesStrategy OnBarUpdate: {ex.Message}");
			NinjaTrader.Code.Output.Process($"Error in OnBarUpdate: {ex.Message}", PrintTo.OutputTab1); 
		}
		
		DebugFreezePrint("CurvesStrategy OnBarUpdate END");
	}
	
	// Fix BuildNewSignal to actually return entry signals
	protected override patternFunctionResponse BuildNewSignal()
	{
		DebugFreezePrint("CurvesStrategy BuildNewSignal START");
		//NinjaTrader.Code.Output.Process($"[CURVES] BuildNewSignal CALLED - BarsInProgress: {BarsInProgress}, Time: {Time[0]}", PrintTo.OutputTab1);
		//Print("[BuildNewSignal] CurvesStrategy");
		string msg = "A";
		patternFunctionResponse thisSignal = new patternFunctionResponse();
		thisSignal.newSignal = FunctionResponses.NoAction;
	    thisSignal.patternSubType = "none";
		thisSignal.patternId = "";
		try{
		DebugFreezePrint("BuildNewSignal validation checks");
		//Print($"[DEBUG] BuildNewSignal Begin: Bull={CurvesV2Service.CurrentBullStrength:F2}%, Bear={CurvesV2Service.CurrentBearStrength:F2}%, RawScore={CurvesV2Service.CurrentRawScore:F2}, PatternType={CurvesV2Service.CurrentPatternType}");
	 
		msg = "A2 ";
	    if(CurrentBars[0] < BarsRequiredToTrade)
	    {
	        return thisSignal;
	    }
	    if(BarsInProgress != 0)
	    {
	        return thisSignal;
	    }
	    msg = "B";
	    DebugFreezePrint("BuildNewSignal position calculations");
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
	    	    DebugFreezePrint("BuildNewSignal signal evaluation START");
	            double currentBullStrength = CurvesV2Service.CurrentBullStrength;
	            double currentBearStrength = CurvesV2Service.CurrentBearStrength;
             msg =  "C ";
				
			        if (CurvesV2Service.SignalsAreFresh && (CurvesV2Service.CurrentSubtype == patternSubtypesPicker.ToString() || patternSubtypesPicker == patternSubtypes.All))
			        {
			        	DebugFreezePrint("Fresh signals found - evaluating");
			      
						msg = "D";
			            // Enhanced logging to debug signal values
			           // Print($"[DEBUG] Signal details: Bull={currentBullStrength:F2}%, Bear={currentBearStrength:F2}%, PatternType={CurvesV2Service.CurrentPatternType}, PatternId={CurvesV2Service.CurrentPatternId}");
				            if(currentBullStrength > currentBearStrength)
							{
								//Print($"[{Time[0]}] LONG SIGNAL FROM CURVESV2: Bull={currentBullStrength}  [subtype] {CurvesV2Service.CurrentSubtype} ");

								
							
				            		// Check for Long signal (strength and ratio conditions) - NOW USING THOMPSON-ADJUSTED THRESHOLD
						            if (CurvesV2Service.CurrentRawScore > OutlierScoreRequirement && VWAP1[0] < EMA3[0])//&& IsRising(EMA3) && IsRising(VWAP1))
						            {
										DebugFreezePrint("LONG signal generated");
										Print($"[LONG] RawScore {CurvesV2Service.CurrentRawScore:F2}");
										
										
							                ThrottleAll = Times[0][0];
							                thisSignal.newSignal = FunctionResponses.EnterLong;
											thisSignal.patternSubType = CurvesV2Service.CurrentSubtype;
											thisSignal.patternId = CurvesV2Service.CurrentPatternId.ToString();
											thisSignal.stopModifier = CurvesV2Service.CurrentStopModifier;
											thisSignal.pullbackModifier = CurvesV2Service.CurrentPullbackModifier;
											return thisSignal;
									}
									
								
				            }
							if(currentBearStrength > currentBullStrength)
							{
								
								//Print($"[{Time[0]}] SHORT SIGNAL FROM CURVESV2: Bear={currentBearStrength} [subtype] {CurvesV2Service.CurrentSubtype}");

								
			
						            if (CurvesV2Service.CurrentRawScore > OutlierScoreRequirement && VWAP1[0] > EMA3[0] )//&& IsFalling(EMA3)  && IsFalling(VWAP1))
						            {
										DebugFreezePrint("SHORT signal generated");
										Print($"[SHORT] RawScore {CurvesV2Service.CurrentRawScore:F2}");
																		
										ThrottleAll = Times[0][0];
							            thisSignal.newSignal = FunctionResponses.EnterShort;
										thisSignal.patternSubType = CurvesV2Service.CurrentSubtype;
										thisSignal.patternId = CurvesV2Service.CurrentPatternId.ToString();
										thisSignal.stopModifier = CurvesV2Service.CurrentStopModifier;
										thisSignal.pullbackModifier = CurvesV2Service.CurrentPullbackModifier;
										return thisSignal;
									}
									
							
							}
							else
							{
								//Print($"[DEBUG] SKIP: Bull={currentBullStrength:F2}% , Bear={currentBearStrength:F2}% |||| [confluenceScore] {CurvesV2Service.CurrentConfluenceScore} , [oppositionStrength] {CurvesV2Service.CurrentOppositionStrength} [effectiveScore] {CurvesV2Service.CurrentEffectiveScore:F2}, [rawScore] {CurvesV2Service.CurrentRawScore:F2}");
		
							}
			        	
					}
				
		        
		        
	    }
		//Print("[DEBUG] BuildNewSignal: No signals generated");
		DebugFreezePrint("CurvesStrategy BuildNewSignal END - No signals");
	    return thisSignal;
		}
		catch (Exception ex)
		{
			DebugFreezePrint($"ERROR in CurvesStrategy BuildNewSignal: {ex.Message}");
			NinjaTrader.Code.Output.Process($"Error in BuildNewSignal: {ex.Message} + {msg}", PrintTo.OutputTab1); 
			 return thisSignal;
		}
	    
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

	// Synchronous wrapper for SendHistoricalBarsAsync to prevent concurrent writes
	private bool SendHistoricalBarsSync(List<object> bars, string instrument)
	{
	    try
	    {
	        Print($"SendHistoricalBarsSync: Blocking until {bars.Count} bars are sent...");
	        var task = curvesService.SendHistoricalBarsAsync(bars, instrument);
	        bool result = task.GetAwaiter().GetResult();
	        Print($"SendHistoricalBarsSync: Completed with result={result}");
	        return result;
	    }
	    catch (Exception ex)
	    {
	        Print($"Error in SendHistoricalBarsSync: {ex.Message}");
	        return false;
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
	
	// Override LoadMEConfig for backtesting-specific configuration
	protected override void LoadMEConfig()
	{
		try
		{
			if (curvesService == null)
			{
				Print("[CONFIG] CurvesV2Service not available - skipping config load");
				return;
			}
			
			// Create backtesting-optimized configuration
			var backtestConfig = new MatchingConfig
			{
				ZScoreThreshold = -0.5,             // Very relaxed for backtesting - allow patterns 0.5 std dev worse than average
				ReliabilityPenaltyEnabled = false,  // Disable penalties during backtesting for more signals
				MaxThresholdPenalty = 0.0,          // No threshold penalties
				AtmosphericThreshold = 0.7,         // Lower pre-filtering threshold for more matches
				CosineSimilarityThresholds = new CosineSimilarityThresholds
				{
					DefaultThreshold = 0.65,        // More relaxed for backtesting
					EmaRibbon = 0.70,               // Lower threshold for more EMA matches
					SensitiveEmaRibbon = 0.73       // Lower threshold for sensitive patterns
				},
				// NEW: Add risk management for dynamic stops (minimal config)
				RiskManagement = new RiskManagementConfig
				{
					MaxTolerance = 100.0            // Just flag that risk management is enabled
				}
			};
			
			// Get instrument code
			string instrumentCode = GetInstrumentCode();
			
			// Send configuration synchronously
			bool configSent = curvesService.SendMatchingConfig(instrumentCode, backtestConfig);
			
			if (configSent)
			{
				Print($"[CONFIG] Backtesting matching configuration sent successfully for {instrumentCode}");
				Print($"[CONFIG] Z-Score: {backtestConfig.ZScoreThreshold}, Atmospheric: {backtestConfig.AtmosphericThreshold}");
				Print($"[CONFIG] Reliability Penalties: {backtestConfig.ReliabilityPenaltyEnabled}");
			}
			else
			{
				Print($"[CONFIG] Failed to send backtesting configuration for {instrumentCode}");
			}
		}
		catch (Exception ex)
		{
			Print($"[CONFIG] Error in LoadMEConfig (Backtesting): {ex.Message}");
		}
	}
}
}