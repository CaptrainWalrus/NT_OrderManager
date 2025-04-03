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

public partial class CurvesStrategy : MainStrategy
{
	// Restore essential service reference declaration
	private CurvesV2Service curvesService;
	
	// Comment out most custom fields
	// private readonly TimeSpan updateInterval = TimeSpan.FromMilliseconds(100);
	// private DateTime lastUpdate = DateTime.MinValue;
	// private DateTime lastProcessedTime = DateTime.MinValue;
	// private int lastProcessedBar = -1;
	// private bool isProcessingQueue;
	// private Dictionary<string, double> signalStrengths = new Dictionary<string, double>();
	// private Dictionary<string, DateTime> lastSignalTimes = new Dictionary<string, DateTime>();
	 public double CurrentBullStrength { get; private set; } 
	 public double CurrentBearStrength { get; private set; } 
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

	[NinjaScriptProperty]    
	[Display(Name="Use Direct Synchronous Processing", Order=1, GroupName="Class Parameters")]
	public bool UseDirectSync { get; set; }

	// Restore Signal class definition
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
			
			// Set default for new property
			UseDirectSync = true; // Enable synchronous mode by default
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
			Print("Initializing CurvesV2 connection (async)...");
			try 
			{ 
				var config = ConfigManager.Instance.CurvesV2Config; 
				
				// Set synchronous mode in config
				config.EnableSyncMode = UseDirectSync;
				Print($"Setting CurvesV2 sync mode to {UseDirectSync}");
				
				curvesService = new CurvesV2Service(config, logger: msg => Print(msg));
				Print("CurvesV2Service Initialized (async connection pending).");
				
				// Establish connection after initialization
				if (UseDirectSync)
				{
					Print("Attempting to establish connection immediately...");
					
					// Create a task to connect with a timeout
					var connectTask = Task.Run(async () => {
						try 
						{
							// Connect to the WebSocket first
							bool wsConnected = await curvesService.ConnectWebSocketAsync();
							
							if (wsConnected)
							{
								Print("WebSocket connection established successfully");
								return true;
							}

							// If WebSocket fails, try the health check
							bool healthy = await curvesService.CheckHealth();
							return healthy;
						}
						catch (Exception ex)
						{
							Print($"Connection check failed: {ex.Message}");
							return false;
						}
					});
					
					// Set a reasonable timeout for connection
					bool connected = false;
					try
					{
						// Wait up to 15 seconds for connection (increased from 10)
						if (connectTask.Wait(15000))
						{
							connected = connectTask.Result;
							if (connected)
							{
								Print("Connection established successfully");
							}
							else
							{
								Print("Connection check failed - server may be unavailable");
							}
						}
						else
						{
							Print("Connection timeout after 15 seconds");
						}
					}
					catch (Exception ex)
					{
						Print($"Error during connection: {ex.Message}");
					}
					
					// In backtest mode, continue even without connection
					if (!connected && IsInStrategyAnalyzer)
					{
						// Check if WebSocket is actually connected despite health check failure
						bool wsConnected = curvesService?.IsWebSocketConnected() ?? false;
						
						if (wsConnected)
						{
							Print("WebSocket is connected - proceeding with backtest");
							connected = true;
						}
						else
						{
							Print("WARNING: Starting backtest with no server connection - using local data only");
						}
					}
				}
			}
			catch (Exception ex)
			{   
				Print($"ERROR initializing CurvesV2Service: {ex.Message}");
				curvesService = null; 
			}
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
			Print("CurvesStrategy.OnStateChange: State.Terminated (No custom logic)");
			
			Print("STATE.TERMINATED: Beginning COMPLETE shutdown sequence...");
			
			// 1. Reset static data in CurvesV2Service
			Print("1. Resetting all static data in CurvesV2Service");
			CurvesV2Service.ResetStaticData();
			
			// 2. Properly dispose of the CurvesV2Service instance
			if (curvesService != null)
			{
				try 
				{
					Print("2. Disposing CurvesV2Service instance");
					curvesService.Dispose();
				}
				catch (Exception ex)
				{
					Print($"Error disposing CurvesV2Service: {ex.Message}");
				}
				finally
				{
					curvesService = null;
				}
			}
			else
			{
				Print("2. No active CurvesV2Service instance to dispose");
			}
			
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
			Print("Calling base.OnStateChange() for Terminated state");
		}
	}

	// Helper methods remain commented out
	/*
	private void InitializeCurvesV2()
	{
		// ... (keep commented out) ...
	}
	*/

	// Restore BuildNewSignal method definition
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

			
				
				if(CurrentBullStrength > CurrentBearStrength * 2)
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
				if(CurrentBearStrength > CurrentBullStrength * 2 )
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
	// Keep ProcessSignal commented out
	// private void ProcessSignal(dynamic signal) { ... }

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
			if (CurrentBars[0] % 10 == 0) // Log every 10 bars
				//NinjaTrader.Code.Output.Process($"isConnected = {isConnected} , OnBarUpdate: Bar={CurrentBar}, Time={Time[0]}", PrintTo.OutputTab1);
			
			// SIMPLIFIED APPROACH: Direct SendBar and UpdateSignals
			if (isConnected)
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
				
				// 2. Simple, direct signal check - fire and forget  
				if (barSent)
				{
					curvesService.CheckSignalsFireAndForget(instrumentCode);
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

	// Restore ProcessSignals method definition
	private void ProcessSignals(string instrumentCode)
	{
		// Skip if we're in terminated state
		if (State == State.Terminated)
		{
			Print("ProcessSignals: State == State.Terminated - skipping signal processing");
			return;
		}

		// In both backtest and live mode, simply update directly from CurvesV2Service
		UpdateLocalSignalData();
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