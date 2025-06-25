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
	
	// NEW: Collection to store historical bars for remote service backfill
	[XmlIgnore]
	private List<object> historicalBars = new List<object>();
	private const int MAX_HISTORICAL_BARS = 200; // Keep last 200 bars for backfill
	
	// Strategy-wide cooldown for regime protection
	[XmlIgnore]
	private bool isInCooldown = false;
	[XmlIgnore]
	private DateTime cooldownStartTime = DateTime.MinValue;
	[XmlIgnore]
	private double cooldownExitPrice = 0.0;
	private const int COOLDOWN_MINUTES = 15;
	private const double COOLDOWN_PRICE_THRESHOLD = 0.5; // Points from exit price
	// Restore NinjaScript Properties

	
	
	public bool UseRemoteService = false;
	
 	public bool backfillSuccess = false;
	// Note: Price history no longer needed - ME service maintains its own 200-bar buffer
	// private List<double> priceHistory = new List<double>(); // REMOVED - not needed
	// private const int MAX_PRICE_HISTORY = 200; // REMOVED - ME service handles this

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
			// Super simple - just one parameter!
			//if (IsInStrategyAnalyzer) DenoiseIndicator = DenoiseReversalIndicator("http://localhost:5000/api/orange-line/analyze",5,200,.6);
		}
		else if (State == State.DataLoaded) 
		{   
			Print("CurvesStrategy.OnStateChange: Handling State.DataLoaded");
			
			// Initialize CurvesV2Service
			try
			{
				Print("Initializing CurvesV2Service...");
				
				// Create configuration for CurvesV2Service
				var config = new NinjaTrader.NinjaScript.Strategies.OrganizedStrategy.CurvesV2Config();
				// Force local services for backtesting to avoid remote timeouts
				config.UseRemoteServer = UseRemoteServiceParameter && State != State.Historical;
				config.EnableSyncMode = UseDirectSync;
				
				// Initialize the service
				curvesService = new CurvesV2Service(config, msg => Print(msg));
				
				// Generate a unique session ID for this strategy instance
				curvesService.sessionID = Guid.NewGuid().ToString();
				
				// Set strategy state for conditional sync/async behavior
				curvesService.SetStrategyState(State == State.Historical);
				
				// Load matching engine configuration
				LoadMEConfig();
				
				// Pass MasterSimulatedStops reference to service
				curvesService.SetMasterSimulatedStops(MasterSimulatedStops);
				
				Print("CurvesV2Service initialized successfully.");
				
				// Optional: Connect WebSocket if not using HTTP-only mode
				if (!UseDirectSync && State != State.Historical)
				{
					Task.Run(async () => {
						try {
							bool connected = await curvesService.ConnectWebSocketAsync();
							Print($"CurvesV2Service WebSocket connected: {connected}");
						}
						catch (Exception ex) {
							Print($"WebSocket connection optional - continuing with HTTP: {ex.Message}");
						}
					});
				}
			}
			catch (Exception ex)
			{
				Print($"Error initializing CurvesV2Service: {ex.Message}");
				curvesService = null;
			}
			
			//AddChartIndicator(DenoiseIndicator);
		}
		// *** Restore Historical block structure ***
		else if (State == State.Historical)
		{
			// Force local services for backtesting to avoid remote timeouts
			UseRemoteService = false;
			
			
			Print($"CurvesStrategy.OnStateChange: State.Historical UseRemoteService {UseRemoteService} (forced local for backtesting)");
			// Custom logic (bar iteration, etc.) remains commented out
		}
		else if (State == State.Realtime)
		{
			
			UseRemoteService = UseRemoteServiceParameter;
			
			Print($"CurvesStrategy.OnStateChange: State.Realtime UseRemoteService {UseRemoteService}");
			
			// Backfill remote service with historical data
			if(UseRemoteService == true && historicalBars.Count > 0)
			{
				Print($"[BACKFILL] Starting backfill with {historicalBars.Count} historical bars...");
				
				// Use fire-and-forget for backfill to avoid blocking OnStateChange
				Task.Run(async () => {
					try
					{
						backfillSuccess = await curvesService.BackfillRemoteServiceAsync(historicalBars, Instrument.MasterInstrument.Name);
						
						if (backfillSuccess)
						{
							Print("✅ Remote service backfilled successfully - ready for real-time trading");
						}
						else
						{
							Print("❌ Remote service backfill failed - may need to wait for real-time buffer");
						}
					}
					catch (Exception ex)
					{
						Print($"[BACKFILL] Error during backfill: {ex.Message}");
					}
				});
			}
			else if (UseRemoteService == true)
			{
				Print("[BACKFILL] ⚠️ No historical bars available for backfill - remote service will need to wait for real-time data");
			}
			// Custom logic (bar iteration, etc.) remains commented out
		}
		// *** Restore Terminated block structure ***
		else if (State == State.Terminated)
		{
			if(terminatedStatePrinted==false)
			{
				// Only log for non-test instances
				bool isTestInstance = IsInStrategyAnalyzer && Account == null;
				if (!isTestInstance)
				{
					Print($"CurvesStrategy.OnStateChange: State.Terminated entered for instance {this.GetHashCode()}. Running cleanup if needed.");
					Print($"[TERMINATION-DEBUG] Error count: {curvesService?.ErrorCounter ?? -1}");
					Print($"[TERMINATION-DEBUG] Time: {Time[0]}");
					Print($"[TERMINATION-DEBUG] Strategy state: {State}");
				}
				terminatedStatePrinted = true;
				
				// 1. Dispose CurvesV2Service
				if (curvesService != null)
				{
					Print("1. Disposing CurvesV2Service...");
					curvesService.Dispose();
					curvesService = null;
				}
				
				// 2. Reset static data
				Print("2. Resetting all static data in CurvesV2Service");
				CurvesV2Service.ResetStaticData();
				
				// 3. (Removed WebSocket handling since it's in the service dispose method)
				
				// 4. Clear local collections
				Print("4. Clearing local collections");
				CurrentBullStrength = 0;
				CurrentBearStrength = 0;
				
				// 5. (Removed ProcessQueue setting since we're now using fire-and-forget)
				
				Print("COMPLETE SHUTDOWN SEQUENCE FINISHED");
				
			}
		}
	}
	////////////////////////////
	//////////////////////////// ON BAR UPDATE
	////////////////////////////
	protected override void OnBarUpdate()
	{
		
		// KILL SWITCH: Check for abort file
		if (System.IO.File.Exists(@"C:\temp\kill_backtest.txt"))
		{
			Print("KILL SWITCH ACTIVATED - Aborting backtest");
			return;
		}
		
		DebugFreezePrint("CurvesStrategy OnBarUpdate START");
		base.OnBarUpdate(); 
		DebugFreezePrint("Base OnBarUpdate completed");
		//Print($"base.OnBarUpdate() done {Time[0]}");
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
			
			DebugFreezePrint("Heartbeat check");
			// In OnBarUpdate or a timer
			if (UseRemoteService == true && BarsInProgress == 1) // Send heartbeats on 5-second series (BarsInProgress 1)
			{
			    //Print("Heartbeat, now truly fire-and-forget");
			    curvesService?.CheckAndSendHeartbeat(UseRemoteService);
			   // Print("Heartbeat call returned - strategy continues");
			    //if(State == State.Realtime) Print($" Last Heartbeat : {curvesService.lastHeartbeat}, Current Time == {Time[0]}");
			}
			DebugFreezePrint("Heartbeat completed");
			
			/// NEW: Collect historical bars for potential backfill (do this for all bars)
			if (BarsInProgress == 0 && IsFirstTickOfBar && State == State.Historical)
			{
				
				var barData = new
				{
					timestamp = Time[0],
					open = Open[0],
					high = High[0],
					low = Low[0],
					close = Close[0],
					volume = Volume[0]
				};
				
				historicalBars.Add(barData);
				
				// Keep only the last MAX_HISTORICAL_BARS bars
				if (historicalBars.Count > MAX_HISTORICAL_BARS)
				{
					historicalBars.RemoveAt(0); // Remove oldest bar
				}
			}
			
			bool enoughData = UseRemoteService == true ? backfillSuccess : true;
			bool isConnected = curvesService.IsConnected && enoughData;
			
			if (CurrentBars[0] < BarsRequiredToTrade) return;
			
			
			CheckAndActivateCooldown();
			
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
				DebugFreezePrint("About to send bar (sync/async based on mode)");
				if (State == State.Realtime)
				{
				    // Real-time: async/fire-and-forget
				    bool barSent = curvesService.SendBarFireAndForget(
				        UseRemoteService,
				        instrumentCode,
				        Time[0],
				        Open[0],
				        High[0],
				        Low[0],
				        Close[0],
				        Volume[0],
				        State == State.Historical ? "backtest" : "1m"
				    );
				    Print($"{Time[0]} SendBarFireAndForget completed");
				}
				else if (State == State.Historical || IsInStrategyAnalyzer)
				{
				    // Backtest (Strategy Analyzer) or Historical (pre-realtime): sync/blocking
				    bool barSent = curvesService.SendBarSync(
				        instrumentCode,
				        Time[0],
				        Open[0],
				        High[0],
				        Low[0],
				        Close[0],
				        Volume[0],
				        State == State.Historical ? "backtest" : "1m"
				    );
				    DebugFreezePrint("SendBarSync completed");
				    
				   
				}
				// 2. PARALLEL signal check - no delay, no dependency on barSent
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
		//Print("CurvesStrategy BuildNewSignal START");
		
		patternFunctionResponse thisSignal = new patternFunctionResponse();
		thisSignal.newSignal = FunctionResponses.NoAction;
	    thisSignal.patternSubType = "none";
		thisSignal.patternId = "";
		
		try{
			// Early safety checks
			if (curvesService == null)
			{
				//Print("BuildNewSignal: curvesService null - returning NoAction");
				return thisSignal;
			}
			
			if (curvesService.ErrorCounter > 10)
			{
				//Print($"BuildNewSignal: Error counter exceeded {curvesService.ErrorCounter} - returning NoAction");
				return thisSignal;
			}
			
			// Check cooldown status first - regime protection
			CheckCooldownExit(); // Update cooldown status
			if (isInCooldown)
			{
				//Print("BuildNewSignal: In cooldown - blocking all entries");
				return thisSignal; // Block all trading during cooldown
			}
			
		//Print($"BuildNewSignal validation checks BarsInProgress={BarsInProgress}");
		//Print($"[DEBUG] BuildNewSignal Begin: Bull={CurvesV2Service.CurrentBullStrength:F2}%, Bear={CurvesV2Service.CurrentBearStrength:F2}%, RawScore={CurvesV2Service.CurrentRawScore:F2}, PatternType={CurvesV2Service.CurrentPatternType}");
	    if(CurrentBars[0] < BarsRequiredToTrade)
	    {
			//Print($"{CurrentBars[0]} < {BarsRequiredToTrade}");
	        return thisSignal;
	    }
	    if(BarsInProgress != 0)
	    {
			//Print($"BarsInProgress !+ {BarsInProgress}");
	        return thisSignal;
	    }
	    //Print("BuildNewSignal position calculations");
	    // Calculate total positions and working orders
	    int totalPositions = getAllcustomPositionsCombined();
	    // Check if we have room for more positions
	    TimeSpan timeSinceLastThrottle = Times[BarsInProgress][0] - ThrottleAll;

	    
	    
	    // Critical safety limit - don't allow more than the max quantity
	    if (totalPositions >= accountMaxQuantity )
	    {
	       // Print($"*** SAFETY HALT - Position limit reached: positions={totalPositions},  max={accountMaxQuantity}");
	        
	    }
	   
		if(!curvesService.IsConnected)
		{
			//Print($"curvesService.IsConnected {curvesService.IsConnected}");
			return thisSignal;
		}
		
	    // Regular position check with throttle timing
	    //if (totalPositions < accountMaxQuantity && timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))
		if (Math.Max(totalPositions,Position.Quantity) < EntriesPerDirection && Math.Max(totalPositions,Position.Quantity) < accountMaxQuantity && timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))

	    {      // Get current signal values
	    	  //Print("BuildNewSignal signal evaluation START");
	            double currentBullStrength = CurvesV2Service.CurrentBullStrength;
	            double currentBearStrength = CurvesV2Service.CurrentBearStrength;
             
				string instrumentCode = GetInstrumentCode();
				
				DebugFreezePrint("About to call CheckSignalsSync (NEW SYNCHRONOUS VERSION)");
				// NEW: Use synchronous version that returns enhanced signal data
				
				var (score, posSize, risk, target, pullback) = curvesService.CheckSignalsSync(UseRemoteService, Time[0], instrumentCode, OutlierScoreRequirement, effectiveScoreRequirement);
				Print($"[BuildNewSignal] {Time[0]}, score={score}, posSize={posSize}, risk={risk}, target={target}, pullback{pullback} >>>> CurvesV2Service.SignalsAreFresh {CurvesV2Service.SignalsAreFresh}");
				DebugFreezePrint("CheckSignalsSync completed");
				
				// Store signal in history for persistence validation using CurrentBars[0]
				currentBarIndex = CurrentBars[0];
				signalHistory[currentBarIndex] = score;
				
				// Clean up old signals (keep only last 10 bars for safety)
				var keysToRemove = signalHistory.Keys.Where(k => k < currentBarIndex - 10).ToList();
				foreach (var key in keysToRemove)
				{
					signalHistory.Remove(key);
				}
				
			       
			        	
						
						if(score > 0)
						{
							
							
			            	// Check for Long signal (strength and ratio conditions) - NOW USING THOMPSON-ADJUSTED THRESHOLD
				            if (score > OutlierScoreRequirement)// && EMA3[0] > VWAP1[0] && IsRising(EMA3))
				            {
								DebugFreezePrint("LONG signal generated with persistence");
								Print($"[LONG] Score={score:F4} [PERSISTENT]");
								
					                ThrottleAll = Times[0][0];
					                thisSignal.newSignal = FunctionResponses.EnterLong;
									thisSignal.patternSubType = CurvesV2Service.CurrentSubtype + "_PERSISTENT";
									thisSignal.patternId = CurvesV2Service.CurrentPatternId.ToString();
									thisSignal.recStop = risk;       // Use RF risk value (dollars)
									thisSignal.recTarget = target;
									thisSignal.recPullback = (100-pullback)/100; // Use RF pullback percentage eg 15 .. .15 >> 0.85
									thisSignal.recQty = strategyDefaultQuantity;// posSize;     // Use RF position size multiplier
									return thisSignal;
							}
			            }
						else if(score < 0)
						{
							
				            if (Math.Abs(score) > OutlierScoreRequirement)// && EMA3[0] < VWAP1[0] && IsFalling(EMA3))
				            {
								DebugFreezePrint("SHORT signal generated with persistence");
								Print($"[SHORT] Score={score:F4} [PERSISTENT]");
														
								ThrottleAll = Times[0][0];
					            thisSignal.newSignal = FunctionResponses.EnterShort;
								thisSignal.patternSubType = CurvesV2Service.CurrentSubtype + "_PERSISTENT";
								thisSignal.patternId = CurvesV2Service.CurrentPatternId.ToString();
								thisSignal.recStop = risk;       // Use RF risk value (dollars)
								thisSignal.recPullback = pullback; // Use RF pullback percentage
								thisSignal.recTarget = target;
								thisSignal.recQty = strategyDefaultQuantity;// posSize;     // Use RF position size multiplier
								return thisSignal;
							}
						}
						else
						{
							//Print($"[PERSISTENCE] Signal direction mismatch: score={score:F2}, persistence={persistenceDirection}");
						}
			        	
					
				
		        
		        
	    }
		//Print("[DEBUG] BuildNewSignal: No signals generated");
		DebugFreezePrint("CurvesStrategy BuildNewSignal END - No signals");
	    return thisSignal;
		}
		catch (Exception ex)
		{
			DebugFreezePrint($"ERROR in CurvesStrategy BuildNewSignal: {ex.Message}");
			Print($"[CRITICAL] BuildNewSignal Exception: {ex.Message}");
			Print($"[CRITICAL] Stack trace: {ex.StackTrace}");
			
			// Increment error counter to prevent cascading failures
			if (curvesService != null)
			{
				curvesService.ErrorCounter++;
				Print($"[ERROR-COUNTER] Incremented to {curvesService.ErrorCounter}");
			}
			
			// Return safe signal to prevent strategy termination
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

	

			// Add simple debug logging for backtesting
		private void BacktestLog(string message)
		{
			if (State == State.Historical || IsInStrategyAnalyzer)
			{
				Print($"[BACKTEST] {message}");
			}
		}
		
		// Strategy-wide cooldown management for regime protection
		private void CheckAndActivateCooldown()
		{
			// Skip cooldown logic during backtesting to avoid false triggers
			if (State == State.Historical || IsInStrategyAnalyzer) return;
			
			if (isInCooldown) return; // Already in cooldown
		
		// Method 1: Single position max loss
		double currentDrawdown = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
		double positionLoss = Math.Abs(currentDrawdown);
		
		if (positionLoss > microContractStoploss * 3) // 3x normal stop
		{
			ActivateCooldown($"Single position max loss: ${positionLoss:F2}");
			return;
		}
		
		// Method 2: Daily cumulative loss
		double dailyLoss = Math.Abs(dailyProfit); // Use your existing daily P&L tracking
		if (dailyLoss > dailyProfitMaxLoss * 0.8) // 80% of daily max loss
		{
			ActivateCooldown($"Daily loss approaching limit: ${dailyLoss:F2}");
			return;
		}
		
		// Method 3: Consecutive losing trades (if you track this)
		// Example: if (consecutiveLosses >= 3) { ActivateCooldown("3 consecutive losses"); }
	}
	
	private void ActivateCooldown(string reason)
	{
		isInCooldown = true;
		cooldownStartTime = Time[0];
		cooldownExitPrice = Close[0];
		
		Print($"[COOLDOWN] 🚨 ACTIVATED: {reason}");
		Print($"[COOLDOWN] Start time: {cooldownStartTime}");
		Print($"[COOLDOWN] Exit price: {cooldownExitPrice:F2}");
		Print($"[COOLDOWN] Will exit when: 15+ minutes OR price moves 0.5+ points");
	}
	
	private bool CheckCooldownExit()
	{
		if (!isInCooldown) return false;
		
		// Check time-based exit (15 minutes)
		TimeSpan elapsed = Time[0] - cooldownStartTime;
		bool timeExit = elapsed.TotalMinutes >= COOLDOWN_MINUTES;
		
		// Check price-based exit (0.5+ point move)
		double priceMove = Math.Abs(Close[0] - cooldownExitPrice);
		bool priceExit = priceMove >= COOLDOWN_PRICE_THRESHOLD;
		
		if (timeExit || priceExit)
		{
			string exitReason = timeExit ? $"Time elapsed ({elapsed.TotalMinutes:F1} min)" : $"Price moved ({priceMove:F2} points)";
			Print($"[COOLDOWN] ✅ EXITED: {exitReason}");
			Print($"[COOLDOWN] Resume trading - regime may have shifted");
			
			isInCooldown = false;
			return true;
		}
		
		// Still in cooldown
		if (CurrentBars[0] % 10 == 0) // Log every 10 bars
		{
			Print($"[COOLDOWN] ⏳ Still active: {elapsed.TotalMinutes:F1} min, price move {priceMove:F2}");
		}
		
		return false;
	}

	// Hybrid synchronous wrapper for SendHistoricalBarsAsync
	private bool SendHistoricalBarsSync(List<object> bars, string instrument)
	{
	    try
	    {
	        // CONDITIONAL SYNC/ASYNC: Only block during backtesting (Historical Mode) 
	        if (State == State.Historical)
	        {
	            // Historical Mode: Block for sequential processing 
	            Print($"SendHistoricalBarsSync [HISTORICAL]: Blocking until {bars.Count} bars are sent...");
	            var task = curvesService.SendHistoricalBarsAsync(bars, instrument);
	            bool result = task.GetAwaiter().GetResult();
	            Print($"SendHistoricalBarsSync [HISTORICAL]: Completed with result={result}");
	            return result;
	        }
	        else
	        {
	            // Real-time Mode: Fire-and-forget to prevent freezes
	            Print($"SendHistoricalBarsSync [REAL-TIME]: Fire-and-forget send of {bars.Count} bars...");
	            Task.Run(async () =>
	            {
	                try
	                {
	                    await curvesService.SendHistoricalBarsAsync(bars, instrument).ConfigureAwait(false);
	                    Print($"SendHistoricalBarsSync [REAL-TIME]: Background send completed successfully");
	                }
	                catch (Exception ex)
	                {
	                    Print($"SendHistoricalBarsSync [REAL-TIME]: Background send failed: {ex.Message}");
	                }
	            });
	            return true; // Return immediately in real-time
	        }
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
		Print("LoadMEConfig BEGIN");
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
			Print($"LoadMEConfig sending configSent");
			// Send configuration synchronously
			bool configSent = curvesService.SendMatchingConfig(instrumentCode, backtestConfig);
			Print($"LoadMEConfig configSent {configSent}");
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