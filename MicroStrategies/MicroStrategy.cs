//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
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
	using System.Security.Cryptography;
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
	using System.IO;
	using Newtonsoft.Json;
	using System.Collections.Concurrent;
	using D2D = SharpDX.Direct2D1;
	using Media = System.Windows.Media;

	// Signal class that combines all required properties
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

	public partial class MicroStrategy : MainStrategy
	{
		// Add these properties to MicroStrategy class
		public CurvesV2Service curvesService;
		private bool curvesConnected;
		private readonly TimeSpan updateInterval = TimeSpan.FromMilliseconds(100); // Match Python service interval
		private DateTime lastUpdate = DateTime.MinValue;
		private DateTime lastProcessedTime = DateTime.MinValue;
		private int lastProcessedBar = -1;
		private bool isProcessingQueue;
		private Dictionary<string, double> signalStrengths = new Dictionary<string, double>();
		private Dictionary<string, DateTime> lastSignalTimes = new Dictionary<string, DateTime>();
		public double CurrentBullStrength { get; private set; }
		public double CurrentBearStrength { get; private set; }
		public static double CurrentResistance { get; private set; }
		public static double CurrentSupport { get; private set; }
		public static double ConfluencePair { get; private set; }
		public double LastBullStrength = double.MinValue;
		public double LastBearStrength = double.MinValue;
		public string timestamp_bar;
		public double high_bar;
		public double low_bar;
		public double open_bar;
		public double close_bar;
		public int volume_bar;
		public bool historicalSync = false;
		private DateTime backtestStartTime; // Time when the strategy entered DataLoaded state
		private DateTime? firstBarTime; // Time of the first bar in the backtest
			
		[NinjaScriptProperty]    
		[Display(Name="Use Remote Service", Order=0, GroupName="Class Parameters")]
		public bool UseRemoteService
		{ get; set; }
		
		
		private List<PatternMatch> ActivePatterns = new List<PatternMatch>();
		private List<Signal> ActiveSignals = new List<Signal>();

		
		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Description = "L2-based signal generation strategy";
				Name = "Micro Strategy";
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
				IsUnmanaged = true;
				
				// Test SignalManager accessibility
				
				
				// Core strategy defaults
				microContractStoploss = 50;
				microContractTakeProfit = 150;
				softTakeProfitMult = 20;
				
				entriesPerDirectionSpacingTime = 1;
				PullBackExitEnabled = true;
				TakeBigProfitEnabled = true;
				signalsOnly = true;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				base.OnStateChange();
				
				// Record when the backtest starts
				backtestStartTime = DateTime.Now;
				firstBarTime = null;
				
				
				
				Print("Initializing CurvesV2 connection...");
				
				// Get configuration from ConfigManager
				var config = ConfigManager.Instance.CurvesV2Config;
				
				// Create CurvesV2Service with configuration
				curvesService = new CurvesV2Service(config, logger: msg => Print(msg));
				
				// Wait for connection synchronously
				Task.Run(async () => {
					for (int attempt = 1; attempt <= 3; attempt++)
					{
						Print($"Attempting CurvesV2 connection (attempt {attempt}/3)...");
						curvesConnected = await curvesService.CheckHealth();
						if (curvesConnected)
						{
							Print($"CurvesV2 connection successful on attempt {attempt}");
							break;
						}
						if (attempt < 3)
						{
							await Task.Delay(1000);
						}
					}
					Print($"Final CurvesV2 connection status: {(curvesConnected ? "Connected" : "Failed to connect")}");
				}).Wait(); // Wait for connection to complete
			}
			else if (State == State.Historical)
			{
				Print("State == State.Historical");
				backtestStartTime = DateTime.Now; // Start real-world timer for backtest timeout

				// Initialize CurvesV2 connection if not already done
				//InitializeCurvesV2();

				// Check if we should process historical data based on the strategy property
				if (curvesConnected && HistoricalData) // Correctly use the strategy's HistoricalData property
				{
					Print("Sending historical bars (Collection temporarily disabled for memory debugging)...");
					// The collection loop remains commented out for debugging
					// var historicalBars = new List<object>(); 
					// int i = CurrentBar;
					// while (i >= 0 && ... )
					// {
					//     historicalBars.Add(new { ... });
					//     i--;
					// }
					// Print($"Collected {historicalBars.Count} historical bars.");
					
					// The sending task also remains commented out
					// Task.Run(async () => { ... });
				}
				else
				{
					// Updated message to reflect the actual properties being checked
					Print($"Skipping historical data send. Connected: {curvesConnected}, HistoricalData Property: {HistoricalData}");
				}
			}
			else if (State == State.Terminated)
			{
				Print("STATE.TERMINATED: Beginning COMPLETE shutdown sequence...");
				
				// Ensure we're marked as disconnected to prevent any new operations
				curvesConnected = false;
				
				// Enhanced disposal with multiple verification steps
				try {
					Print("1. Resetting all static data in CurvesV2Service");
					CurvesV2Service.ResetStaticData();
					
					// If we have an active service, dispose it
					if (curvesService != null)
					{
						Print("2. Disposing CurvesV2Service instance");
						try {
							// Force WebSocket connections to close
							curvesService.Dispose(); // Instance Dispose now handles background task termination
							Print("   CurvesV2Service.Dispose() completed");
						}
						catch (Exception ex) {
							Print($"   Error during CurvesV2Service disposal: {ex.Message}");
						}
						finally {
							// Always null out the reference
							Print("3. Nulling out CurvesV2Service reference");
							curvesService = null;
						}
					}
					else {
						Print("2. No active CurvesV2Service instance to dispose");
					}
					
					// Clear local collections
					Print("4. Clearing local collections"); // Renumbered step
					if (ActivePatterns != null) ActivePatterns.Clear();
					if (ActiveSignals != null) ActiveSignals.Clear();
					if (signalStrengths != null) signalStrengths.Clear();
					if (lastSignalTimes != null) lastSignalTimes.Clear();
					
					// Nullify all collections
					ActivePatterns = null;
					ActiveSignals = null;
					signalStrengths = null;
					lastSignalTimes = null;
					
					// Perform aggressive garbage collection
					Print("6. Running aggressive garbage collection");
					GC.Collect(2, GCCollectionMode.Forced);
					GC.WaitForPendingFinalizers();
					GC.Collect(2, GCCollectionMode.Forced);
					
					Print("COMPLETE SHUTDOWN SEQUENCE FINISHED");
				} catch (Exception ex) {
					Print($"CRITICAL ERROR in shutdown sequence: {ex.Message}");
				}
				
				// Let the base strategy finish its termination
				Print("Calling base.OnStateChange() for Terminated state");
				base.OnStateChange();
			}
		}
		
		

		
		protected override FunctionResponses BuildNewSignal()
		{
		
			
			FunctionResponses FRS = FunctionResponses.NoAction;
			
			if(CurrentBars[0] < BarsRequiredToTrade)
			{
				Print($"{CurrentBars[0]} < {BarsRequiredToTrade}");
				FRS = FunctionResponses.NoAction;
				return FRS;
			}
			if(BarsInProgress != 0)
			{
				FRS = FunctionResponses.NoAction;
				return FRS;
			}
			// Check if we're enabled to accept signals
			//if (!SignalExchangeManager.IsAcceptingEnabled(Name, Instrument.FullName))
			//{
			//	Print("Is NOT AcceptingEnabled");
			//	return FunctionResponses.NoAction;
			//}

			// If confluence is -1, there's a conflict between ES and NQ, don't trade
			// If confluence is 0, the other instrument doesn't exist, allow trading
			// If confluence is 1, both instruments agree, allow trading
			

			TimeSpan timeSinceLastThrottle = Times[BarsInProgress][0] - ThrottleAll;
			/// dont run more than 1 time every 30 sec
		
			if (getAllcustomPositionsCombined() < accountMaxQuantity && timeSinceLastThrottle > TimeSpan.FromMinutes(entriesPerDirectionSpacingTime))
			{
				//if(CurrentSupport != 0) Draw.Dot(this,"CurrentSupport"+CurrentBars[0],true,0,CurrentSupport,Brushes.Lime);
				//if(CurrentResistance != 0) Draw.Dot(this,"CurrentResistance"+CurrentBars[0],true,0,CurrentResistance,Brushes.Red);
				//if(CurrentSupport != 0) Draw.HorizontalLine(this,"CurrentSupport",CurrentSupport,Brushes.Lime);
				//if(CurrentResistance != 0) Draw.HorizontalLine(this,"CurrentResistance",CurrentResistance,Brushes.Red);
				
				bool canScaleInAgg = CalculateTotalOpenPositionProfit() > (scaleInRisk);
				double microInstrumentOpenProfit = CalculateTotalOpenPositionProfitForInstrument(1);

				if ((GetMarketPositionByIndex(0) != MarketPosition.Flat && canScaleInAgg) || (GetMarketPositionByIndex(0) == MarketPosition.Flat))
				{

					if(CurrentBullStrength > CurrentBearStrength * 2 || CurrentBullStrength - CurrentBearStrength > 25 )
					{
						Print($"MICROSTRATEGY LONG Bull: {CurrentBullStrength}, Bear: {CurrentBearStrength}");
						forceDrawDebug($"+{CurrentBullStrength}",1,0,High[0]+(TickSize*20),Brushes.Lime,true);

							//if(LastBullStrength != CurrentBullStrength)
							//{
								
							//	LastBullStrength = CurrentBullStrength;
								
								ThrottleAll = Times[BarsInProgress][0];
								FRS = FunctionResponses.EnterLong;
								
								return FRS;
							
							//}
						
					}
					if(CurrentBearStrength > CurrentBullStrength * 2 || CurrentBearStrength - CurrentBullStrength > 25)
					{
						Print($"MICROSTRATEGY SHORT Bull: {CurrentBullStrength}, Bear: {CurrentBearStrength}");
						forceDrawDebug($"-{CurrentBearStrength}",1,0,Low[0]-(TickSize*20),Brushes.Red,true);
							//if(LastBearStrength != CurrentBearStrength)
							//{
								

							//	LastBearStrength = CurrentBearStrength;
								 
								//
								ThrottleAll = Times[BarsInProgress][0];
								FRS = FunctionResponses.EnterShort;
								
								return FRS;
							//}
						
					}
				}
			}
							
			FRS = FunctionResponses.NoAction;
			return FRS;
		}
		
		private void ProcessSignal(dynamic signal)
		{
			try
			{
				Print($"Processing signal - Raw data: {JsonConvert.SerializeObject(signal)}");
				
				// Handle WebSocket confidence messages
				if (signal.data != null)
				{
					CurrentBullStrength = signal.data.bull?.ToObject<double>() ?? 0.0;
					CurrentBearStrength = signal.data.bear?.ToObject<double>() ?? 0.0;
					
					Print($"Processed WebSocket confidence values - Bull: {CurrentBullStrength}, Bear: {CurrentBearStrength}");
					forceDrawDebug($"WS Bull: {CurrentBullStrength}, Bear: {CurrentBearStrength}", 1, 0, High[0] + (TickSize * 20), Brushes.White, true);
					
					
					return;
				}
			}
			catch (Exception ex)
			{
				Print($"Error processing signal: {ex.Message}");
			}
		}

	

		protected override void OnBarUpdate()
		{
			base.OnBarUpdate();
			return;
			try
			{
				// Skip if not on the primary BarsInProgress
				if (BarsInProgress != 0) return;
				
				// Check if we're in backtest mode and we should apply the time limit
				if (IsInStrategyAnalyzer && CurrentBars[0] > 0)
				{
					// Check if we've exceeded our allotted real-world execution time
					TimeSpan elapsedRealTime = DateTime.Now - backtestStartTime;
					
					if (elapsedRealTime.TotalMinutes > 3)
					{
						Print($"EMERGENCY SHUTDOWN: Backtest exceeded 3 minute real-time limit. Forcefully terminating all connections.");
						
						// Don't let anything continue processing
						curvesConnected = false;
						
						try 
						{
							// More aggressive termination sequence
							if (curvesService != null)
							{
								// Clear all static data first
								CurvesV2Service.ResetStaticData();
								
								// Force the service to dispose completely
								try 
								{ 
									Print("Forcefully disposing CurvesV2Service in OnBarUpdate timeout"); // Added context
									curvesService.Dispose(); 
								}
								catch (Exception ex) 
								{ 
									Print($"Error during forced CurvesV2Service disposal: {ex.Message}"); 
								}
								
								// Null out the reference immediately
								curvesService = null;
							}
							
							// Force three rounds of garbage collection
							Print("Forcing aggressive garbage collection");
							GC.Collect(2, GCCollectionMode.Forced);
							GC.WaitForPendingFinalizers();
							GC.Collect(2, GCCollectionMode.Forced);
							GC.WaitForPendingFinalizers();
							GC.Collect(2, GCCollectionMode.Forced);
							
							// Set strategy to terminated state
							Print("Setting strategy state to terminated");
							State = State.Terminated;
						}
						catch (Exception ex)
						{
							Print($"Error during emergency shutdown: {ex.Message}");
						}
						
						// Exit immediately
						Print("Exiting OnBarUpdate due to emergency shutdown");
						return;
					}
				}
				
				// **** ADDED: Log entry into OnBarUpdate ****
				if (CurrentBars[0] % 10 == 0) // Log every 10 bars to reduce noise
					Print($"OnBarUpdate: Bar={CurrentBars[0]}, Time={Time[0]}, BarsInProgress={BarsInProgress}");

				// Check BarsRequiredToTrade
				if (CurrentBars[0] < BarsRequiredToTrade) {
					// **** ADDED: Log if skipping due to BarsRequired ****
					if (CurrentBars[0] % 10 == 0)
						 Print($"OnBarUpdate: Skipping, Bar={CurrentBars[0]} < BarsRequiredToTrade={BarsRequiredToTrade}");
					return;
				}
				
				// Log periodically in backtest mode
				if (IsInStrategyAnalyzer && CurrentBars[0] % 100 == 0)
				{
				//	Print($"Backtest progress: {Time[0]}");
				}
				
				// Process current bar data
				// **** ADDED: Log before calling ProcessBarData ****
				if (CurrentBars[0] % 10 == 0)
					Print($"OnBarUpdate {Time[0]}: Calling ProcessBarData for Bar={CurrentBars[0]}");
				ProcessBarData();
				
				// Clean up old signals
				CleanupOldSignals();
				
				// In backtest mode, regularly clear memory
				if (IsInStrategyAnalyzer && CurrentBars[0] % 100 == 0)
				{
					// Clear references periodically to prevent memory buildup
					CurvesV2Service.ResetStaticData();
				}
			}
			catch (Exception ex)
			{
				Print($"Error in OnBarUpdate: {ex.Message}");
			}
			finally
			{
				// Clean up memory right after each bar update
				if (IsInStrategyAnalyzer)
				{
					// Don't retain any references between bars
					if (ActiveSignals != null && ActiveSignals.Count > 0)
						ActiveSignals.Clear();
					
					if (ActivePatterns != null && ActivePatterns.Count > 0)
						ActivePatterns.Clear();
				}
			}
		}

		// Process the current bar's data
		private void ProcessBarData()
		{
			if (CurrentBars[0] < 0) return;
			
			// Don't process data if we're terminated
			if (State == State.Terminated)
			{
				Print("ProcessBarData: State == State.Terminated - skipping data processing");
				return;
			}
			
			// **** ADDED: Log entry into ProcessBarData ****
			if (CurrentBars[0] % 10 == 0)
				 Print($"ProcessBarData: Bar={CurrentBars[0]}, curvesConnected={curvesConnected}");

			// Extract instrument code safely
			string instrumentCode = GetInstrumentCode();
			// Only process if CurvesV2 service is connected
			if (curvesConnected)
			{
				// Send bar data to CurvesV2
				// **** ADDED: Log before calling SendBarData ****
				 if (CurrentBars[0] % 10 == 0)
					Print($"ProcessBarData: Calling SendBarData for Bar={CurrentBars[0]}");
				SendBarData(IsInStrategyAnalyzer);
				
				// Process signals (if appropriate)
				ProcessSignals(instrumentCode);
				
				// Clean up for memory management - don't retain data between bars
				instrumentCode = null;
				
				// Aggressively clear pattern matches if we're in a backtest
				if (IsInStrategyAnalyzer)
				{
					// Call garbage collection in backtest mode to minimize memory usage
					if (CurrentBars[0] % 100 == 0)
					{
						ActivePatterns.Clear();
						ActiveSignals.Clear();
						GC.Collect();
					}
				}
			}
			else
			{
				// **** ADDED: Log if skipping due to curvesConnected == false ****
				 if (CurrentBars[0] % 10 == 0)
					Print($"ProcessBarData: Skipping SendBarData/ProcessSignals for Bar={CurrentBars[0]} because curvesConnected is false.");
			}
		}

		// Extract instrument code safely
		private string GetInstrumentCode()
		{
			string instrumentCode = Instrument?.FullName?.Split(' ')?.FirstOrDefault() ?? "";
			if (string.IsNullOrEmpty(instrumentCode))
			{
				Print("Warning: Unable to determine instrument code - using fallback");
				instrumentCode = "UNKNOWN";
			}
			return instrumentCode;
		}

		// Send bar data to CurvesV2
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
				return false;
			}
			
			// Extract instrument code safely
			string instrumentCode = GetInstrumentCode();
			
			// Use QueueBar instead of SendBar
			bool sent = curvesService.QueueBar(
				instrumentCode,
				Time[0],
				Open[0],
				High[0],
				Low[0],
				Close[0],
				Volume[0],
				isHistorical ? "backtest" : "1m"
			);
			
			return sent;
		}

		// Process signals based on context (synchronous/async)
		private void ProcessSignals(string instrumentCode)
		{
			// Skip if we're in terminated state
			if (State == State.Terminated)
			{
				Print("ProcessSignals: State == State.Terminated - skipping signal processing");
				return;
			}

			// In both backtest and live mode, simply update directly from CurvesV2Service
			// No need for different paths - we just use the signals that were updated by automatic polling
			UpdateLocalSignalData();
		}

		// Update local signal data from CurvesV2Service static properties
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
					ActivePatterns = new List<PatternMatch>(CurvesV2Service.CurrentMatches);
					
					// Convert to Signals format for backward compatibility
					ActiveSignals = ConvertPatternsToSignals(ActivePatterns);
					
					// Only log if we have actual signal strength and not in backtest mode
					if (!IsInStrategyAnalyzer && (CurrentBullStrength > 0 || CurrentBearStrength > 0) && CurrentBars[0] % 20 == 0)
					{
						Print($"Signal state: Bull={CurrentBullStrength}, Bear={CurrentBearStrength}, Fresh={signalsFresh}, Patterns={ActivePatterns.Count}");
					}
				}
				else if (!IsInStrategyAnalyzer && CurrentBars[0] % 100 == 0) 
				{
					Print("No patterns in signal response");
					ActivePatterns = new List<PatternMatch>();
					ActiveSignals = new List<Signal>();
				}
			}
			catch (Exception ex)
			{
				Print($"Error in UpdateLocalSignalData: {ex.Message}");
				// Reset to safe defaults
				ActivePatterns = new List<PatternMatch>();
				ActiveSignals = new List<Signal>();
			}
		}

		// Helper method to convert PatternMatches to legacy Signal format
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
							timestamp = DateTime.UtcNow.ToString("O"),
							pattern_id = p.id ?? Guid.NewGuid().ToString(),
							pattern_name = p.patternName ?? "Unknown Pattern",
							pattern_type = p.patternType ?? "unknown",
							direction = (p.patternType != null && p.patternType.ToLower().Contains("bull")) ? "long" : "short",
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

		private void CleanupOldSignals()
		{
			var now = DateTime.UtcNow;
			var oldSignals = lastSignalTimes.Where(kvp => (now - kvp.Value).TotalMinutes > 5).ToList();
			
			foreach (var signal in oldSignals)
			{
				signalStrengths.Remove(signal.Key);
				lastSignalTimes.Remove(signal.Key);
			}
		}
		
		protected override void sendPositionsForReview(string instrument, double entryPrice, double exitPrice, 
		    DateTime entryTime, DateTime exitTime, List<Signal> patternId, string marketPosition, double profit)
		{
			if (curvesConnected)
			{
				Task.Run(async () => {
					try 
					{
						// Create trade result object
						var tradeResult = new TradeResult
						{
							pattern_id = patternId.FirstOrDefault()?.pattern_id ?? "unknown",
							entry_time = (long)(entryTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds,
							entry_price = entryPrice,
							exit_time = (long)(exitTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds,
							exit_price = exitPrice,
							pnl = profit,
							pnl_points = Math.Abs(exitPrice - entryPrice),
							direction = marketPosition.ToLower() == "long" ? "long" : "short",
							status = "completed"
						};
						
						// Send trade result to CurvesV2
						string instrumentCode = instrument.Split(' ')[0]; // Extract ES or NQ
						await curvesService.SendTradeResultAsync(instrumentCode, tradeResult);
						
						Print($"Sent trade result to CurvesV2: {instrumentCode}, Pattern: {tradeResult.pattern_id}, PnL: {profit}");
					}
					catch (Exception ex)
					{
						Print($"Error sending position for review: {ex.Message}");
					}
				});
			}
		}
	
	}
}
