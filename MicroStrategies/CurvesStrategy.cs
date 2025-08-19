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
	private bool configOnce = false;
	[XmlIgnore]
	private bool configOnce2 = false;
	
	// Loss Vector Filters
	[XmlIgnore]
	private LossVectorFilters lossVectorFilters;
	[XmlIgnore]
	private DateTime cooldownStartTime = DateTime.MinValue;
	[XmlIgnore]
	private double cooldownExitPrice = 0.0;
	private const int COOLDOWN_MINUTES = 15;
	private const double COOLDOWN_PRICE_THRESHOLD = 0.5; // Points from exit price
	// Restore NinjaScript Properties

	
	
	public bool UseRemoteService = false;
	
 	public bool backfillSuccess = false;
	private bool backfillCompleted = false; // NEW: Prevent multiple backfill attempts
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
				
				// Note: DropletService is now initialized in MainStrategy.cs
				// SendSessionConfigToRelay() is also called from MainStrategy.cs after config is loaded
				
				// Use session ID from MainStrategy
				curvesService.sessionID = SessionID;
				Print($"[SESSION-DEBUG] Using MainStrategy session ID: {curvesService.sessionID} for instrument: {Instrument?.MasterInstrument?.Name ?? "UNKNOWN"}");
				
				// Set strategy state for conditional sync/async behavior
				curvesService.SetStrategyState(State == State.Historical);
				
				// Load matching engine configuration
				
				
				// Pass MasterSimulatedStops reference to service
				curvesService.SetMasterSimulatedStops(MasterSimulatedStops);
				
				Print("CurvesV2Service initialized successfully.");
				
				// Initialize Loss Vector Filters
				lossVectorFilters = new LossVectorFilters(this, msg => Print(msg));
				Print("LossVectorFilters initialized.");
				
				// Load loss vector filter configuration once at startup
				if (currentConfig != null && lossVectorFilters != null)
				{
					lossVectorFilters.LoadFromConfig(currentConfig);
					Print($"[LOSS-VECTOR-CONFIG] Loaded {lossVectorFilters.CountActiveFilters()} active filters from config");
				}
				
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
				Print($"[INIT-ERROR] Error initializing CurvesV2Service for {Instrument?.MasterInstrument?.Name ?? "UNKNOWN"}: {ex.Message}");
				Print($"[INIT-ERROR] Stack trace: {ex.StackTrace}");
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
			
			// Backfill remote service with historical data (ONLY ONCE)
			if(UseRemoteService == true && historicalBars.Count > 0 && !backfillCompleted)
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
						
						// Mark as completed regardless of success/failure to prevent loops
						backfillCompleted = true;
					}
					catch (Exception ex)
					{
						Print($"[BACKFILL] Error during backfill: {ex.Message}");
						backfillSuccess = false; // Ensure failure is recorded
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
				
				// 1. Send performance summary before disposing
				if (curvesService != null)
				{
					Print("1. Sending performance summary before disposal...");
					Task.Run(async () => 
					{
						try
						{
							await curvesService.SendPerformanceSummary(this);
						}
						catch (Exception ex)
						{
							Print($"[PERFORMANCE-SUMMARY] Error in termination: {ex.Message}");
						}
					}).Wait(TimeSpan.FromSeconds(5)); // Wait up to 5 seconds for completion
					
					Print("2. Disposing CurvesV2Service...");
					curvesService.Dispose();
					curvesService = null;
				}
				
				// 3. Reset static data
				Print("3. Resetting all static data in CurvesV2Service");
				CurvesV2Service.ResetStaticData();
				
				// 4. (Removed WebSocket handling since it's in the service dispose method)
				
				// 5. Clear local collections
				Print("5. Clearing local collections");
				CurrentBullStrength = 0;
				CurrentBearStrength = 0;
				
				// 6. (Removed ProcessQueue setting since we're now using fire-and-forget)
				
				Print("COMPLETE SHUTDOWN SEQUENCE FINISHED");
				
			}
		}
	}
	////////////////////////////
	//////////////////////////// ON BAR UPDATE
	////////////////////////////
	protected override void OnBarUpdate()
	{
		if(BarsInProgress == 0 && CurrentBars[0] % 120 == 0)
		{
			Print($"{GetInstrumentCode()} >> {Time[0]}");
		}
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
			
			DebugFreezePrint("Heartbeat completed");
			
			
			
			
			
		
			bool isConnected = curvesService.IsConnected;
			
			if (CurrentBars[0] < BarsRequiredToTrade) return;

			
			
			
		}
		catch (Exception ex)
		{
			DebugFreezePrint($"ERROR in CurvesStrategy OnBarUpdate: {ex.Message}");
			NinjaTrader.Code.Output.Process($"Error in OnBarUpdate: {ex.Message}", PrintTo.OutputTab1); 
		}
		
		DebugFreezePrint("CurvesStrategy OnBarUpdate END");
	}
	
	// NEW: Simplified BuildNewSignal using Signal Approval Service
	protected override patternFunctionResponse BuildNewSignal()
	{
		patternFunctionResponse thisSignal = new patternFunctionResponse();
		thisSignal.newSignal = FunctionResponses.NoAction;
		thisSignal.patternSubType = "none";
		thisSignal.patternId = "";
		
		try
		{
			// Early safety checks
			if (curvesService == null || CurrentBars[0] < BarsRequiredToTrade || BarsInProgress != 0)
				return thisSignal;
			
			// Check cooldown status first - regime protection
			CheckCooldownExit();
			if (isInCooldown)
				return thisSignal;
			
			// Check Loss Vector Filters (already loaded in State.DataLoaded)
			if (lossVectorFilters != null && lossVectorFilters.EnableLossVectorFilters)
			{
				string filterReason;
				if (lossVectorFilters.ShouldFilterTrade(out filterReason))
				{
					Print($"[LOSS-VECTOR-FILTER] Trade blocked: {filterReason}");
					return thisSignal; // Return NoAction
				}
			}
			
			// Position and throttle checks
			int totalPositions = getAllcustomPositionsCombined();
			TimeSpan timeSinceLastThrottle = Times[BarsInProgress][0] - ThrottleAll;
			
			
			if (Math.Max(totalPositions,Position.Quantity) < EntriesPerDirection && Math.Max(totalPositions,Position.Quantity) < accountMaxQuantity && timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))
			{

			   
				// Get traditional strategy signal using voting system with config
				StrategyConfig currentConfig = new StrategyConfig(SpecificStrategyName);
				
				//var traditionalSignal = improvedTraditionalStrategies.ExecuteVotingSystem(this, currentConfig);
				
				
				patternFunctionResponse traditionalSignal = null;
				
				// Config is now loaded ONCE in State.DataLoaded, not here
				// Using the class-level currentConfig that was validated at startup
				
				// Choose execution mode:
				/// Option 1: NEW Decay-based signal strength system with momentum
				if(strategyExecutionMode == executionMode.decaySystem)
				{	
					traditionalSignal = improvedTraditionalStrategies.ExecuteDecaySystem(this, currentConfig);
				}		
				/// Option 2: Branching system - regime detection then confirmation
				if(strategyExecutionMode == executionMode.Branching)
				{
						traditionalSignal = improvedTraditionalStrategies.ExecuteBranchingSystem(this, currentConfig);
				}
				/// Option 3: Original voting system with Math/Lag segments
				if(strategyExecutionMode == executionMode.VotingSystem)
				{
				 	traditionalSignal = improvedTraditionalStrategies.ExecuteVotingSystem(this, currentConfig);
				}
						
				/// Option 4: Execute all strategies with consensus (30% agreement threshold)
				if(strategyExecutionMode == executionMode.Consensus)
				{
					traditionalSignal = improvedTraditionalStrategies.ExecuteAll(this, 0.3, currentConfig);
				}
				
				if (traditionalSignal == null || traditionalSignal.newSignal == FunctionResponses.NoAction)
					return thisSignal;
				// Traditional strategies already got Risk Agent approval and populated signalScore, recStop, recTarget
				// Check if the signal meets our confidence threshold
				Print($"[DEBUG-CURVES] Signal: {traditionalSignal.signalType}, Score: {traditionalSignal.signalScore:F3}, Confidence: {traditionalSignal.confidence:F3}, Threshold: {RiskAgentConfidenceThreshold:F3}");
				
				if (traditionalSignal.signalScore >= RiskAgentConfidenceThreshold)
				{
					ThrottleAll = Times[0][0];
					
					string direction = traditionalSignal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
					Print($"[SIGNAL-APPROVED] {traditionalSignal.signalScore:P1} - {traditionalSignal.signalType} {direction} SL {traditionalSignal.recStop:F2} TP {traditionalSignal.recTarget:F2}");
					return traditionalSignal;
				}
				else
				{
					string direction = traditionalSignal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
					Print($"[SIGNAL-REJECTED] {traditionalSignal.signalScore:P1} - {traditionalSignal.signalType} {direction} @ {Close[0]:F2}");
					return thisSignal;
				}
			}
			else
			{
				return thisSignal;
			}
			
		}
		catch (Exception ex)
		{
			Print($"[CRITICAL] BuildNewSignal Exception: {ex.Message}");
			if (curvesService != null)
				curvesService.ErrorCounter++;
			return thisSignal;
		}
	}
	
	// Calculate features for signal approval
	private FeatureSet CalculateSignalFeatures()
	{
		try
		{
			if (CurrentBar < 21) return null;
			
			return new FeatureSet
			{
				Momentum5 = (Close[0] / Close[5]) - 1.0,
				PriceChangePct5 = (Close[0] - Close[5]) / Close[5],
				BbPosition = CalculateBollingerPosition(),
				BbWidth = CalculateBollingerWidth(),
				VolumeSpike3Bar = CalculateVolumeSpike(),
				EmaSpreadPct = CalculateEmaSpread(),
				Rsi = RSI(14, 3)[0],
				AtrPct = ATR(14)[0] / Close[0],
				RangeExpansion = (High[0] - Low[0]) / Math.Max(ATR(14)[0], 0.1)
			};
		}
		catch (Exception ex)
		{
			Print($"Error calculating features: {ex.Message}");
			return null;
		}
	}
	
	// Helper methods for feature calculation
	private double CalculateBollingerPosition()
	{
		var bb = Bollinger(Close, 2, 20);
		return (Close[0] - bb.Lower[0]) / (bb.Upper[0] - bb.Lower[0]);
	}
	
	private double CalculateBollingerWidth()
	{
		var bb = Bollinger(Close, 2, 20);
		return (bb.Upper[0] - bb.Lower[0]) / bb.Middle[0];
	}
	
	private double CalculateVolumeSpike()
	{
		if (CurrentBar < 3) return 1.0;
		double vol3Avg = (Volume[1] + Volume[2] + Volume[3]) / 3.0;
		return Volume[0] / Math.Max(vol3Avg, 1);
	}
	
	private double CalculateEmaSpread()
	{
		if (EMA3?.Count > 0 && VWAP1?.Count > 0)
			return (EMA3[0] - VWAP1[0]) / Close[0];
		return 0.0;
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