#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Cbi;
using System.IO;
using System.Xml.Serialization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Concurrent;


#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{

	[Gui.CategoryOrder("Risk Agent Config", 1)]
	[Gui.CategoryOrder("Class Parameters", 2)]
	[Gui.CategoryOrder("Entry Parameters", 3)]
	[Gui.CategoryOrder("Entry Behaviors", 4)]
	[Gui.CategoryOrder("EMA", 5)]
	[Gui.CategoryOrder("Signal Parameters", 6)]
	[Gui.CategoryOrder("Strategy Level Params", 7)]
	[Gui.CategoryOrder("Debug", 8)]
	[Gui.CategoryOrder("Broker Settings", 9)]
	[Gui.CategoryOrder("Entry Types", 10)]
	public partial class MainStrategy : Strategy
	{
		// Instance tracking for debugging Strategy Analyzer issues
		private static readonly HashSet<int> ActiveInstances = new HashSet<int>();
		private static readonly object InstanceLock = new object();
		
		// Clear stale instances on static initialization
		static MainStrategy()
		{
			ActiveInstances.Clear();
		}
		
		[XmlIgnore]
		public CurvesV2Service curvesService;
		
		[XmlIgnore]
		public DropletService dropletService;
		
		[XmlIgnore]
		public DropletIntegration dropletIntegration;
		
		[XmlIgnore]
		private TrainingDataClient trainingDataClient;
		
		[XmlIgnore]
		private ProjectXBridge projectXBridge;
		
		[XmlIgnore]
		// Legacy: protected TraditionalStrategies traditionalStrategies;
		protected ImprovedTraditionalStrategies improvedTraditionalStrategies;
		
		[XmlIgnore]
		public string SessionID { get; private set; }
		
		public int openOrderTest = 0;
		[XmlIgnore]
		public double CurrentBullStrength { get; set; } 
		
		[XmlIgnore]
		public double CurrentBearStrength { get; set; } 
		
		public string contract;
		/// threads
	 	public int historicalBar = 0;
		public double globalPrice;
		public string ConnectionName;
		public string startTimeL3;
		public string endTimeL3;
		public int lastBar = -1;
		public string startTimeAPI;
        public string endTimeAPI;
		
		public double priceMarginL3;
	
		
	
		public Bollinger BB0;
		public double GetPriceRangeValue0;
		public double GetPriceRangeValue1;
		public double GetPriceRangeValue2;
		public double GetPriceRangeValue3;

		public double instrumentSeriesMicroTickValue;
		public double instrumentSeriesStandardTickValue;
		
		public double OpenPositionProfitRisk;
		public DateTime ThrottleAll = DateTime.MinValue;
		public double dailyLossATL;
		/*
		
		public double Index0avgRatio;
		public double Index0currentRatio;
		public double Index0lowerBullThreshold;
		public double Index0upperBullThreshold; 
		public double Index0upperBullThresholdAggressive;
		
		public double Index0upperBearThreshold;
		public double Index0lowerBearThreshold;
		public double Index0lowerBearThresholdAggressive;
		
*/
		public double avgRollingEntryPrice;/// used for EOSC
		private double accumulationReq;
		private double lastCumProfit;
		private double CumProfitDelta;
		private int stallCount = 0;
		protected int currentPrimaryBarBeingAnalyzed = 0;
		protected int realTimeCounter = 0;
		protected bool isRealTime = false;
		//int uninterruptedBullWins = 0;
		protected bool SkipDynamicPatternObject = false;
		protected int uninterruptedBearWins = 0;
		protected bool isGranularLockedOut = false;
		public bool thompsonLock;
		// Parameters to track the wins and losses
		protected int numberOfWins = 0;
		protected int numberOfLosses = 0;
		protected double totalProfit = 0;
		
		// Profit tracking dictionary: <Time[0] string, profit value>
		private Dictionary<string, double> profitByTimeStamp = new Dictionary<string, double>();
		protected double totalLoss = 0;
		/*  // Set default master value
		protected double MasterValue2 = 0.5;  // Default midpoint for control
		protected int debugMaxPossibleEntries = 0;
		protected int debugMaxPossiblePatterns = 0;
		protected int debugnewcounterr = 0;
		
		protected int debugEarlyExits = 0;
		protected int progBarDebug = 0;
		protected int debugShortEntry = 0;
		protected int debugLongEntry = 0;
		protected int debugNullEntry = 0;
		protected int debugNullEntry2 = 0;
		protected int debugNoActionEntry = 0;
*/
		protected bool useStaticDataSeries = false;
		
		// Loose starting values for each parameter
		protected double looseStartingObservations = 50;
		protected double looseStartingProfitMin = 5.0;
		protected double looseStartingPatternMaxAge = 10;
		protected double looseStartingSimilarityThreshold = 1;
		protected double looseStartingValueScoreThreshold = .5;
		protected double looseStartingCompositeScoreThreshold = 5.0;
	// Variables to track the time/distance since the last loss
		private int lastLossBar = -1;  // Initialize to -1 to represent no previous loss
		private DateTime lastLossTime;  // Or use timestamp if needed
		private DateTime lastSignalQueueTime;  // Or use timestamp if needed
		protected int lastEMACrossAboveVWAP = 0;
		protected int lastEMACrossBelowVWAP = 0;
		protected double steepestVWAP = double.MinValue;
		protected string strategyName = "";
		protected bool reEnterOnWin = false;
		protected bool noPatternsRequired = false;
		protected int orderEntryBIP = 0;
		protected NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType barsType;
		private int RTCounter = 5;
		private bool isWithinDoNotTradeWindow = false;
		private bool testWebhook = false;
		private bool saveTest = true;
		private bool configOnce = false;
		private bool pauseThompson = false;
		private double previousNetProfit = 0;
		private double currentNetProfit = 0;
		private int init_lookbackwindow;
		private int barCount = 0;
		protected CustomPosition customPosition; /// customPosition IRL, Position in backtest
		private string lastFunction = "";
		private bool endAll = false;
		private string lastDebugPoint;
		private SimpleProfiler profiler = new SimpleProfiler();
		private double rampUp = 0;
		private double riskAdjustedRampUp = 1;
		///  backtest timer
	 	private signalPackage PreppedSignalPackage;
		private DateTime startTime;

		protected executionMode strategyExecutionMode;
		
		public double pinPrice = 0;
		public DateTime pinTime;
		public string pinDirection = "";
			
		private bool DebugMode;
		
		private bool isTerminationLogicRun = false; // Flag to ensure termination logic runs only once per instance
	//	private double prevdata = double.MinValue;
	//	private double drawDownBasedRisk = 0;
	//	private double preOptimizeProfitAvg;
	//	private int preOptimizeCount;
	//	private macroTrend MacroTrend = macroTrend.Unknown;
	//	private int lastVolatilityStart;
	//	private int lastVolatilityEnd;
		private DateTime lastResetDate;
		private DateTime lastPrintDate;
		private DateTime lastDailyDate;
		private static int seed = 012345; // You can change this seed value to get different sequences
		
		// Trade correlation analysis state variables
		private int dailyTradeCounter = 0;
		private string lastTradeOutcome = "NONE";
		private DateTime lastTradeDate = DateTime.MinValue;
	//	private int usedSeed = 0;
		public SessionIterator sessionIterator;
// Get the actual session end time
		private DateTime sessionBeginTime;
		private DateTime sessionEndTime;
		private DateTime sessionExitTime;
		private bool sessionCloseRequested;
		private bool flattenWindowAnnounced;
		public double dailyProfitATH = double.MinValue;
		public double intervalNetProfit = 0;
		public bool btnEnterLong = false;
		public bool btnEnterShort = false;
	
		public double unrealizedPNL = 0;
		public double realizedPNL = 0;
		private bool emailShutdownNoticeSent = false;
		
		double directionCounter = 0;
	
      	public double perOrderFixedDollarAccountRisk_dynamic;
		public double HardTakeProfit;
		public double HardTakeProfit_Long;
		public double HardTakeProfit_Short;
		public double SoftTakeProfit;
		public double SoftTakeProfit_Long;
		public double SoftTakeProfit_Short;
		public double HardMaxLoss;
		public double HardMaxLoss_Short;
		public double HardMaxLoss_Long;
  		
		public double openRisk = 0;
		protected int savedBar;
		protected int lastActionBar;
		protected int barSpacing;
		protected int lastSignalBar = -999;  // Track when last signal was generated
		protected int signalCooldownBars = 5;  // Minimum bars between signals
		protected string lastSignalDirection = "";  // Track last signal direction
		

		
 		public Random random = new Random();
		protected int MarginRiskBar;
		protected double profitRate;
		protected int smallestBarsInProgress = -1;
		protected int smallestMinuteValue = int.MaxValue;
		protected int largestBarsInProgress = -1;
		protected int largestMinuteValue = int.MinValue;
		
	
		protected double globalReversalRate = 0;
		private DateTime t1;
	
		private DateTime t0;

		private regimeType measuredRegimeType = regimeType.TrendFollowing;
		private regimeType previousRegimeType = regimeType.TrendFollowing;
		public signalScoreDebug SSD;
		
		protected UnrealizedProfit UNRPNL;
		protected WickReversalStrength WickRevStr;
		

		protected AttractingMA attractingMA;
		public EMA EMA3; 
		public EMA EMA4; 
	
		//const EMA_PERIODS = [8, 13, 21, 34, 55, 89]; // Standard periods

		public EMA EMA8;
		public EMA EMA13;
		public EMA EMA21;
		public EMA EMA34;
		public EMA EMA55;
		public EMA EMA89;
		public SMA SMA200;
		public RSI RSI14;
	

		private double[] cumProfitArray = new double[3];
		private int valueCounter = 0;
		
        private int barsInLagPeriod;
        private int barsInTotalPeriod;
		
		public custom_VWAP VWAP1;
		private SLTPTracker sLTPTracker;

	
	
		public bool isBarOpenForExit = false;
		public bool isBarOpenForEntry = false;
		public int numBarEntries = 0;
		protected Account myAccount;

	
	    protected TimeZoneInfo centralTimeZone;
		protected TimeZoneInfo pacificTimeZone;
		protected DateTime currentDateTimeCT;
	
	
		protected double accountUnPNL;
		protected int actualPositionQuantity;
		protected double cashValue = 1000;// default

		protected double positionUnPNL;

        protected double riskAmount;
		
		public double dailyProfit;
		public double dailyProfitGoal;
		public double dailyProfit_Long;
		public double dailyProfit_Short;
		public double dailyProfitDynamic;
		public bool runningProfit;
		
		//protected double bt_cashValueStart;
		protected double lowest_PNL = 0;

	


		
		public double virtualCashAccount;/// used for tracking backtest outcomes
	
	
	
		protected List<OrderRecordMasterLite> LiteMasterRecords;
		
		private Dictionary<string, OrderRecordMasterLite> OrderRecordMasterLiteEntrySignals = new Dictionary<string, OrderRecordMasterLite>();
		private Dictionary<string, OrderRecordMasterLite> OrderRecordMasterLiteExitSignals = new Dictionary<string, OrderRecordMasterLite>();
		private Dictionary<vwapStop,OrderRecordMasterLite > vwapStopMapping = new Dictionary<vwapStop,OrderRecordMasterLite>();
		private Dictionary<string, double> patternIdProfits = new Dictionary<string, double>();

		
		protected List<simulatedEntry> MasterSimulatedEntries;
		protected List<simulatedStop> MasterSimulatedStops;
		protected List<OrderActionResult> OrderActionResultList;
		
		
		/// new version of OrderFlowPatternATH
			
		private Dictionary<string, OrderFlowPattern> OrderFlowPatternATH = new Dictionary<string, OrderFlowPattern>();
		//protected Dictionary<string, GridMetricRanges> metricRanges = new Dictionary<string, GridMetricRanges>();
		protected Dictionary<string, Func<double, double, double>> metrics = new Dictionary<string, Func<double, double, double>>();
		protected Dictionary<double,double> similarityProfits = new Dictionary<double,double>();
		protected Dictionary<double,int> similarityCounts = new Dictionary<double,int>();
		protected Dictionary<string,double> regimeTracker = new Dictionary<string,double>();


		// Signal persistence tracking for RF validation
		[XmlIgnore]
		public Dictionary<int, double> signalHistory = new Dictionary<int, double>();
		public const int SIGNAL_CONFIRMATION_BARS = 3; // Reduced from 5 to 3 for more trading opportunities
		public int currentBarIndex = 0;
		Queue<simulatedEntry> MastersimulatedEntryToDelete = new Queue<simulatedEntry>();
		Queue<simulatedStop> MastersimulatedStopToDelete = new Queue<simulatedStop>();
		//Queue<PatternObject> OrderFlowPatternsToDelete = new Queue<PatternObject>();
		protected Stack<signalPackage> signalQueue = new Stack<signalPackage>();


		private int minBarsNeeded;

		public int barToSkip;
		
		/// <summary>
		/// VOLUMETRICS

		bool isTerminatedPrintOut = false;
		public int firstBarOfSession = 0;

				/// </summary>
		protected double perOrderCapitalStopLoss = 0;
	
		public DateTime StrategyLastScaleInTime;
		public DateTime StrategyLastEntryTime;/// for entry spacing
		public DateTime AccountLastEntryTime;/// for entry spacing
		public DateTime tempAccountLastEntryTime;
		
		protected double instrumentInitialMargin;
		protected double instrumentDayMargin;
		
		public double openProfit = 0;
		public int chartPosition = 0;
		public double chartPNL = 0;
  // Declare PSAR_Signals at the class level
		[XmlIgnore]
		public DateTime StartDate { get; private set; }
		[XmlIgnore]
		public DateTime EndDate { get; private set; }

		[NinjaScriptProperty]    
		[Display(Name="Use Remote Service", Order=0, GroupName="Class Parameters")]
		public bool UseRemoteServiceParameter { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Droplet Endpoint URL", Order=1, GroupName="Droplet Configuration")]
		public string DropletEndpoint { get; set; } = "https://yourtradejournal-relay.onrender.com";
		
		[NinjaScriptProperty]
		[Display(Name="Droplet API Key", Order=2, GroupName="Droplet Configuration")]
		public string DropletApiKey { get; set; } = "";
		
		[NinjaScriptProperty]
		[Display(Name="Enable Droplet Service", Order=3, GroupName="Droplet Configuration")]
		public bool EnableDropletService { get; set; } = true;

	


		/// <summary>
		/// widget controls
		/// </summary>
		public Indicator_Y indicator_Y;
		public string OutputText { get; set; }

		public MyStrategyControlPane myStrategyControlPane;
	
		// DEBUG PRINT WRAPPER - SET TO TRUE TO ENABLE FREEZE DEBUGGING
		private static bool ENABLE_FREEZE_DEBUG = false;
		private static DateTime lastDebugPrint = DateTime.MinValue;
		
		protected void DebugFreezePrint(string message, [System.Runtime.CompilerServices.CallerFilePath] string fileName = "", [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
		{
			if (ENABLE_FREEZE_DEBUG)// && !IsInStrategyAnalyzer && State == State.Realtime)
			{
				string fileNameOnly = System.IO.Path.GetFileName(fileName);
				var now = DateTime.Now;
				var timeSinceLastPrint = now - lastDebugPrint;
				Print($"[FREEZE DEBUG] [{now:HH:mm:ss.fff}] (+{timeSinceLastPrint.TotalMilliseconds:F0}ms) [{fileNameOnly}:{lineNumber}] {message}");
				lastDebugPrint = now;
			}
		}
		
		/// <summary>
		/// Initialize ProjectX Bridge with authentication and reconciliation
		/// </summary>
		private async Task InitializeProjectXBridge()
		{
			try
			{
				if (string.IsNullOrEmpty(ProjectXApiKey) || string.IsNullOrEmpty(ProjectXUsername) || ProjectXAccountId == 0)
				{
					Print("❌ ProjectX configuration incomplete - API Key, Username, and Account ID required");
					Print("💡 Contact your firm to get API Key and Account ID for algorithmic trading");
					return;
				}
				
				bool initialized = await projectXBridge.InitializeAsync(ProjectXUsername, ProjectXApiKey, ProjectXAccountId);
				
				if (initialized)
				{
					Print("✅ ProjectX Bridge initialized successfully");
				}
				else
				{
					Print("❌ ProjectX Bridge initialization failed");
				}
			}
			catch (Exception ex)
			{
				Print($"❌ ProjectX Bridge initialization exception: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Get contract mapping for ProjectX based on current NinjaTrader instrument
		/// </summary>
		public string GetProjectXContractId()
		{
			try
			{
				// Get instrument details from NinjaTrader
				string masterInstrumentName = Instrument?.MasterInstrument?.Name ?? "";
				string fullName = Instrument?.FullName ?? "";
				DateTime expiry = Instrument?.Expiry ?? DateTime.MinValue;
				
				// Extract base symbol
				string baseSymbol = masterInstrumentName.ToUpper();
				
				// Determine month code from expiry
				string monthCode = GetMonthCode(expiry.Month);
				string yearCode = expiry.Year.ToString().Substring(2); // Last 2 digits
				
				// Build ProjectX contract ID format: CON.F.US.{SYMBOL}.{MONTH}{YEAR}
				string contractId = $"CON.F.US.{baseSymbol}.{monthCode}{yearCode}";
				
				//Print($"📊 ProjectX Contract Mapping: {fullName} → {contractId}");
				
				return contractId;
			}
			catch (Exception ex)
			{
				Print($"❌ Error mapping contract: {ex.Message}");
				
				// Fallback to basic mapping
				string instrumentName = Instrument?.FullName ?? "";
				if (instrumentName.Contains("ES")) return "CON.F.US.ES.Z24";
				else if (instrumentName.Contains("NQ")) return "CON.F.US.NQ.Z24";
				else if (instrumentName.Contains("RTY")) return "CON.F.US.RTY.Z24";
				else if (instrumentName.Contains("GC")) return "CON.F.US.GC.G25";
				else if (instrumentName.Contains("MGC")) return "CON.F.US.MGC.G25";
				else if (instrumentName.Contains("CL")) return "CON.F.US.CL.G25";
				else return "UNKNOWN";
			}
		}
		
		/// <summary>
		/// Convert month number to futures month code
		/// </summary>
		private string GetMonthCode(int month)
		{
			switch (month)
			{
				case 1: return "F";  // January
				case 2: return "G";  // February
				case 3: return "H";  // March
				case 4: return "J";  // April
				case 5: return "K";  // May
				case 6: return "M";  // June
				case 7: return "N";  // July
				case 8: return "Q";  // August
				case 9: return "U";  // September
				case 10: return "V"; // October
				case 11: return "X"; // November
				case 12: return "Z"; // December
				default: return "Z";
			}
		}
		
		/// <summary>
		/// Execute ProjectX long entry with bracket order
		/// </summary>
		private async Task ExecuteProjectXEntryLong(int quantity, string entryUUID)
		{
			try
			{
				if (projectXBridge == null)
				{
					Print($"❌ ProjectX Bridge not initialized for {entryUUID}");
					return;
				}
				
				// Get risk parameters (simplified - use your actual risk logic)
				double currentPrice = GetCurrentAsk(0);
				double stopLoss = currentPrice - (TickSize * 20); // 20 ticks stop
				double takeProfit = currentPrice + (TickSize * 40); // 40 ticks target
				string contractId = GetProjectXContractId();
				
				// 1. Submit ProjectX bracket order
				bool projectXSuccess = await projectXBridge.ProjectXEnterLong(quantity, entryUUID, stopLoss, takeProfit, contractId);
				
				if (projectXSuccess)
				{
					// 2. Submit NT simulation order for strategy continuity
					EnterLong(1, quantity, entryUUID);
					Print($"✅ ProjectX LONG order placed: {entryUUID}");
					
					// 3. Monitor fill in background
					_ = Task.Run(() => MonitorProjectXOrder(entryUUID, true));
				}
				else
				{
					Print($"❌ ProjectX LONG order failed: {entryUUID}");
				}
			}
			catch (Exception ex)
			{
				Print($"❌ ProjectX entry long exception: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Execute ProjectX short entry with bracket order
		/// </summary>
		private async Task ExecuteProjectXEntryShort(int quantity, string entryUUID)
		{
			try
			{
				if (projectXBridge == null)
				{
					Print($"❌ ProjectX Bridge not initialized for {entryUUID}");
					return;
				}
				
				// Get risk parameters (simplified - use your actual risk logic)
				double currentPrice = GetCurrentBid(0);
				double stopLoss = currentPrice + (TickSize * 20); // 20 ticks stop
				double takeProfit = currentPrice - (TickSize * 40); // 40 ticks target
				string contractId = GetProjectXContractId();
				
				// 1. Submit ProjectX bracket order
				bool projectXSuccess = await projectXBridge.ProjectXEnterShort(quantity, entryUUID, stopLoss, takeProfit, contractId);
				
				if (projectXSuccess)
				{
					// 2. Submit NT simulation order for strategy continuity
					EnterShort(1, quantity, entryUUID);
					Print($"✅ ProjectX SHORT order placed: {entryUUID}");
					
					// 3. Monitor fill in background
					_ = Task.Run(() => MonitorProjectXOrder(entryUUID, false));
				}
				else
				{
					Print($"❌ ProjectX SHORT order failed: {entryUUID}");
				}
			}
			catch (Exception ex)
			{
				Print($"❌ ProjectX entry short exception: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Monitor ProjectX order fill and handle failures
		/// </summary>
		private async Task MonitorProjectXOrder(string entryUUID, bool isLong)
		{
			try
			{
				// Wait up to 30 seconds for fill
				int maxWaitSeconds = 30;
				int checkIntervalSeconds = 2;
				
				for (int i = 0; i < maxWaitSeconds / checkIntervalSeconds; i++)
				{
					await Task.Delay(checkIntervalSeconds * 1000);
					
					// Check if ProjectX position exists
					var position = await projectXBridge.GetProjectXPositionByUUID(entryUUID);
					
					if (position != null && position.isActive)
					{
						Print($"✅ ProjectX order filled: {entryUUID}");
						return;
					}
				}
				
				// If we get here, ProjectX order didn't fill - emergency exit NT position
				Print($"⚠️ ProjectX order NOT filled within {maxWaitSeconds}s: {entryUUID}");
				await EmergencyExitNTPosition(entryUUID, isLong);
			}
			catch (Exception ex)
			{
				Print($"❌ ProjectX monitoring error: {ex.Message}");
				await EmergencyExitNTPosition(entryUUID, isLong);
			}
		}
		
		/// <summary>
		/// Emergency exit NT position if ProjectX fails
		/// </summary>
		private async Task EmergencyExitNTPosition(string entryUUID, bool wasLong)
		{
			try
			{
				Print($"🚨 EMERGENCY EXIT NT position: {entryUUID}");
				
				int orderSeriesIndex = GetOrderSeriesIndex();
				if (wasLong)
				{
					ExitLong(orderSeriesIndex, 1, $"EXIT_{entryUUID}", entryUUID);
				}
				else
				{
					ExitShort(orderSeriesIndex, 1, $"EXIT_{entryUUID}", entryUUID);
				}
				
				Print($"📢 ProjectX order failed - NT position closed: {entryUUID}");
			}
			catch (Exception ex)
			{
				Print($"❌ Emergency exit exception: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Convert existing stop to trailing stop when profit threshold is reached
		/// </summary>
		private async Task ConvertToTrailingStop(simulatedStop simStop, double currentProfit)
		{
			try
			{
				string entryUUID = simStop.EntryOrderUUID;
				
				// Get the ProjectX order set
				var orderSet = projectXBridge.GetOrderSet(entryUUID);
				if (orderSet == null)
				{
					Print($"❌ No ProjectX order set found for {entryUUID}");
					return;
				}
				
				// Calculate new trailing stop price
				double entryPrice = simStop.OrderRecordMasterLite.EntryOrder.AverageFillPrice;
				bool isLong = simStop.OrderRecordMasterLite.EntryOrder.IsLong;
				int quantity = simStop.OrderRecordMasterLite.EntryOrder.Quantity;
				
				// Set trailing stop at pullBackPct of current profit (e.g., 50% = $150 if profit is $300)
				double trailAmount = (currentProfit * pullBackPct) / quantity; // Per contract trail amount
				double trailingStopPrice = isLong ? 
					entryPrice + trailAmount : 
					entryPrice - trailAmount;
				
				Print($"[TRAILING-STOP] Converting stop for {entryUUID}: Entry ${entryPrice:F2}, Current profit ${currentProfit:F2}, Trail at ${trailingStopPrice:F2}");
				
				// Cancel existing stop order
				bool cancelSuccess = await projectXBridge.CancelStopOrder(entryUUID);
				if (!cancelSuccess)
				{
					Print($"⚠️ Failed to cancel stop for trailing conversion: {entryUUID}");
					return;
				}
				
				// Small delay to ensure cancellation is processed
				await Task.Delay(500);
				
				// Place new trailing stop order
				bool trailSuccess = await projectXBridge.PlaceTrailingStop(
					entryUUID, 
					trailingStopPrice,
					quantity
				);
				
				if (trailSuccess)
				{
					Print($"✅ Trailing stop activated for {entryUUID}: Stop at ${trailingStopPrice:F2} ({pullBackPct:P0} of ${currentProfit:F2} profit)");
				}
				else
				{
					Print($"❌ Failed to place trailing stop for {entryUUID} - position unprotected!");
				}
			}
			catch (Exception ex)
			{
				Print($"❌ Trailing stop conversion error for {simStop.EntryOrderUUID}: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Update ProjectX profit information
		/// </summary>
		private async Task UpdateProjectXProfit(simulatedStop simStop)
		{
			try
			{
				if (projectXBridge == null)
					return;
					
				var projectXPosition = await projectXBridge.GetProjectXPositionByUUID(simStop.EntryOrderUUID);
				if (projectXPosition != null)
				{
					double realProfit = CalculateProjectXProfit(projectXPosition, simStop);
					
					// Update the ProjectX info object
					var pxInfo = simStop.OrderRecordMasterLite.PriceStats.OrderStatsProjectXInfo;
					pxInfo.positionId = projectXPosition.positionId;
					pxInfo.contractId = projectXPosition.contractId;
					pxInfo.size = projectXPosition.size;
					pxInfo.entryPrice = projectXPosition.entryPrice;
					pxInfo.currentPrice = projectXPosition.currentPrice;
					pxInfo.unrealizedPnL = projectXPosition.unrealizedPnL;
					pxInfo.calculatedProfit = realProfit;
					pxInfo.lastUpdate = DateTime.Now;
					pxInfo.isActive = true;
				}
			}
			catch (Exception ex)
			{
				Print($"❌ Error updating ProjectX profit: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Calculate ProjectX profit based on position info
		/// </summary>
		private double CalculateProjectXProfit(ProjectXPositionInfo position, simulatedStop simStop)
		{
			try
			{
				bool isLong = simStop.OrderRecordMasterLite.EntryOrder.IsLong;
				int quantity = simStop.OrderRecordMasterLite.EntryOrder.Quantity;
				double pointValue = BarsArray[simStop.instrumentSeriesIndex].Instrument.MasterInstrument.PointValue;
				
				double profit;
				if (isLong)
				{
					profit = (position.currentPrice - position.entryPrice) * quantity * pointValue;
				}
				else
				{
					profit = (position.entryPrice - position.currentPrice) * quantity * pointValue;
				}
				
				// Track profit by timestamp if profit is not null
				if (profit != 0) // Using 0 check since double can't be null
				{
					string timeStamp = Time[0].ToString("yyyy-MM-dd HH:mm:ss");
					profitByTimeStamp[timeStamp] = profit;
					
					// Debug the calculation components for troubleshooting
					Print($"[PROFIT-DEBUG] {timeStamp}:");
					Print($"  IsLong: {isLong}");
					Print($"  EntryPrice: {position.entryPrice:F2}");
					Print($"  CurrentPrice: {position.currentPrice:F2}");
					Print($"  Quantity: {quantity}");
					Print($"  PointValue: {pointValue}");
					Print($"  Price Diff: {(isLong ? position.currentPrice - position.entryPrice : position.entryPrice - position.currentPrice):F2}");
					Print($"  Calculated Profit: ${profit:F2}");
					Print($"  Expected: Should be around ($9.00)");
				}
				
				return profit;
			}
			catch (Exception ex)
			{
				Print($"❌ Error calculating ProjectX profit: {ex.Message}");
				return 0.0;
			}
		}
		
		/// <summary>
		/// Check if NT and ProjectX profits are synchronized
		/// </summary>
		private bool CheckProfitSync(double ntProfit, double pxProfit, string entryUUID)
		{
			try
			{
				double profitDifference = Math.Abs(ntProfit - pxProfit);
				double toleranceThreshold = Math.Max(50.0, Math.Abs(ntProfit) * 0.05); // 5% or $50, whichever is larger
				
				bool isSynced = profitDifference <= toleranceThreshold;
				
				if (!isSynced)
				{
					Print($"[SYNC-CHECK] {entryUUID}: Diff=${profitDifference:F2} Tolerance=${toleranceThreshold:F2}");
				}
				
				return isSynced;
			}
			catch (Exception ex)
			{
				Print($"❌ Error checking profit sync: {ex.Message}");
				return true; // Assume synced on error to avoid false alarms
			}
		}
		
		/// <summary>
		/// Handle profit drift between NT and ProjectX
		/// </summary>
		private async Task HandleProfitDrift(simulatedStop simStop, double ntProfit, double pxProfit)
		{
			try
			{
				double driftAmount = Math.Abs(ntProfit - pxProfit);
				string entryUUID = simStop.EntryOrderUUID;
				
				// Critical drift threshold - emergency exit
				double criticalDriftThreshold = 200.0; // $200
				
				if (driftAmount > criticalDriftThreshold && !IsInStrategyAnalyzer)
				{
					Print($"🚨 CRITICAL DRIFT: {entryUUID} - Emergency exit triggered! Drift=${driftAmount:F2}");
					
					// Mark position for emergency exit
					simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
					simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.EMERGENCY;
					simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = $"Emergency profit drift exit: ${driftAmount:F2}";
					
					// Exit both NT and ProjectX positions
					bool isLong = simStop.OrderRecordMasterLite.EntryOrder.IsLong;
					int quantity = simStop.OrderRecordMasterLite.EntryOrder.Quantity;
					string exitUUID = simStop.OrderRecordMasterLite.ExitOrderUUID;
					
					// Exit NT position
					int orderSeriesIndex = GetOrderSeriesIndex();
					if (isLong)
					{
						ExitLong(orderSeriesIndex, quantity, exitUUID, entryUUID);
					}
					else
					{
						ExitShort(orderSeriesIndex, quantity, exitUUID, entryUUID);
					}
					
					// Exit ProjectX position
					string contractId = GetProjectXContractId();
					if (isLong)
					{
						await projectXBridge.ProjectXExitLong(quantity, exitUUID, entryUUID, contractId);
					}
					else
					{
						await projectXBridge.ProjectXExitShort(quantity, exitUUID, entryUUID, contractId);
					}
					
					// Mark for cleanup
					simStop.OrderRecordMasterLite.OrderSupplementals.SimulatedStop.isExitReady = false;
					simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = true;
					MastersimulatedStopToDelete.Enqueue(simStop);
					
					Print($"📢 EMERGENCY: Both NT and ProjectX positions closed due to critical drift");
				}
				else
				{
					// Log the drift for monitoring
					Print($"⚠️ PROFIT DRIFT: {entryUUID} NT=${ntProfit:F2} PX=${pxProfit:F2} Diff=${driftAmount:F2}");
				}
			}
			catch (Exception ex)
			{
				Print($"❌ Error handling profit drift: {ex.Message}");
			}
		}
	
		protected override void OnStateChange()
		{
		
			try{

		// Create a queue to hold orders to be added to the dictionary
		
			if (State == State.SetDefaults)
			{
				// Track instance creation for debugging Strategy Analyzer issues
				
				
				Description									= @"Management of order handling and responses to entry signals, logic for exits";
				Name										= "OrganizedStrategy_MainStrategy";
				Calculate									= Calculate.OnPriceChange;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= false; // Disable NT built-in, use our own logic
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.AdoptAccountPosition;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				
				// AUTO-SYNC CONFIG FILES - Ensure all coded strategies are in configs
				try
				{
					if (improvedTraditionalStrategies == null)
						improvedTraditionalStrategies = new ImprovedTraditionalStrategies();
					
					// Auto-correct all config files
					string[] configFiles = {"PUMPKIN.json", "PUMPKIN_NQ.json", "PUMPKIN_NQ_200TICK.json", "PUMPKIN_NQ_500tick.json"};
					foreach (string configFile in configFiles)
					{
						string configPath = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "strategies", "OrganizedStrategy", "Configs", configFile);
						bool wasUpdated = improvedTraditionalStrategies.AutoCorrectConfig(configPath);
						if (wasUpdated)
							Print($"✅ Auto-corrected config: {configFile}");
					}
					
					Print($"🔄 Config auto-sync completed for {configFiles.Length} files");
				}
				catch (Exception ex)
				{
					Print($"⚠️ Config auto-sync error: {ex.Message}");
				}
				minBarsNeeded								= 75;
				IsAdoptAccountPositionAware 				= true;
				IsOverlay 									= true;
	            RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors; // Ignore all errors to prevent strategy from terminating
				IsUnmanaged = false; // Switch to unmanaged mode
				
				lock(InstanceLock)
				{
					ActiveInstances.Add(this.GetHashCode());
					Print($"[INSTANCE-{Name}] Created: {this.GetHashCode()}, Total Active: {ActiveInstances.Count}");
				}
				
				DivergenceThreshold = 0.4;
				
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
			
				strategyName = Name;
				
				
				IsInstantiatedOnEachOptimizationIteration	= false; // FIXED: Prevent excessive instance creation
		
				
				//////////ORDER FLOW///////////
		
				softTakeProfitMult = .2;
				
				accumulationReq = 1;
				IsUnmanaged = false;
				
				accountMaxQuantity = 5;
			
				selectedBroker = brokerSelection.Topstep;
		
				ema3_val = 10;
				
				vwap1 = 20;
				commonPeriod_smooth = 5;
				VWAPScalingFactor = 1;
			
				
			
		      
				dailyProfitMaxLoss = 750;
			
				DebugMode = DebugModeParameter;
				
				        // Set default values for the strategy
		        sessionBeginTime = DateTime.MinValue;
		        sessionEndTime = DateTime.MinValue;
		        sessionExitTime = DateTime.MinValue;
		
			
			
				mainEntryOrderType = OrderType.Market;
				
				
				
				marketPosAllowed = marketPositionsAllowed.Any;
				signalsOnly = false;
			
				debugModeDraw = false;
				DebugFilter = debugSection.none;
			
				enableProfitColors = 0;
				enableLossColors = 0;
				//enableScalping = false;
				strategyDefaultQuantity = 1;
				strategyMaxQuantity = 4;
				accountMaxQuantity = 5;
				entriesPerDirectionSpacing = 2;
				entriesPerDirectionSpacingTime = 90;

				
				///TREND PRESETS
				//dynamicCapitalRisk = false;
				perOrderMaxAccountRisk = 0.05;
			
				pullBackPct = 0.8;
				
			
		
				dailyProfitGoalParameter = 600;
				dailyProfitGoal = dailyProfitGoalParameter;
				
	
				
				
			

			}
			else if (State == State.Configure)
			{
				ClearOutputWindow();
				
				// Core series initialization (no Python)        
				//restingSeries = new Series<double>(this);
				//imbalanceSeries = new Series<double>(this);
				customPosition = new CustomPosition(this, BarsArray, 5);
				OrderActionResultList = new List<OrderActionResult>();
				MasterSimulatedEntries = new List<simulatedEntry>();
				MasterSimulatedStops = new List<simulatedStop>();
				MastersimulatedEntryToDelete = new Queue<simulatedEntry>();
				MastersimulatedStopToDelete = new Queue<simulatedStop>();
				LiteMasterRecords = new List<OrderRecordMasterLite>();

				OrderRecordMasterLiteEntrySignals = new Dictionary<string, OrderRecordMasterLite>();
				OrderRecordMasterLiteExitSignals = new Dictionary<string, OrderRecordMasterLite>();
				
				myAccount = Account;	
				
				myStrategyControlPane = MyStrategyControlPane();
				AddChartIndicator(myStrategyControlPane);
				myStrategyControlPane.AssociatedStrategy = this;
				
				
	           /// for stops
	           AddDataSeries(Instrument.FullName,BarsPeriodType.Second, 1);
	          // AddDataSeries(Instrument.FullName,BarsPeriodType.Minute, 30);
	              
	         
			}
			else if (State == State.DataLoaded)
			{			
				// Note: Strategy Analyzer instances have Account == null
				// We still need to initialize critical components for backtesting
				
				// Generate unique session ID for this strategy instance
				SessionID = Guid.NewGuid().ToString();
				Print($"[SESSION-DEBUG] Generated session ID: {SessionID} for strategy: {Name} instrument: {Instrument?.MasterInstrument?.Name ?? "UNKNOWN"}");
				
				// Initialize training data client
				trainingDataClient = new TrainingDataClient();
				
				// Initialize traditional strategies
				// Legacy: traditionalStrategies = new TraditionalStrategies();
				improvedTraditionalStrategies = new ImprovedTraditionalStrategies();
				
				// Initialize DropletService if enabled
				if (EnableDropletService && !string.IsNullOrEmpty(DropletEndpoint))
				{
					dropletService = new DropletService(
						DropletEndpoint, 
						DropletApiKey, 
						msg => Print(msg)
					);
					
					// Initialize DropletIntegration with the DropletService
					dropletIntegration = new DropletIntegration(
						dropletService,
						msg => Print(msg)
					);
					
					Print($"[DROPLET] DropletService and DropletIntegration initialized - Endpoint: {DropletEndpoint}");
					
					// Send session configuration to relay for risk management tracking
					// This will be called after config is loaded below
				}
				else
				{
					Print($"[DROPLET] DropletService disabled or no endpoint configured");
				}
				
				// ========== CRITICAL: Load and Validate Strategy Config ==========
				// Load config ONCE during initialization, not on every bar update
				if (!string.IsNullOrEmpty(SpecificStrategyName) && !OverrideStrategyConfig)
				{
					Print($"[CONFIG-INIT] Loading config for: {SpecificStrategyName}");
					currentConfig = new StrategyConfig(SpecificStrategyName);
					
					// Validate that config loaded successfully
					if (currentConfig == null)
					{
						Print($"[CONFIG-ERROR] Failed to create config object for: {SpecificStrategyName}");
						endAll = true;
						return;
					}
					
					// Check if critical risk parameters were loaded
					if (!currentConfig.TakeProfit.HasValue || !currentConfig.StopLoss.HasValue)
					{
						Print($"[CONFIG-ERROR] Config file missing critical risk parameters for: {SpecificStrategyName}");
						Print($"[CONFIG-ERROR] TakeProfit: {(currentConfig.TakeProfit.HasValue ? currentConfig.TakeProfit.ToString() : "NULL")}");
						Print($"[CONFIG-ERROR] StopLoss: {(currentConfig.StopLoss.HasValue ? currentConfig.StopLoss.ToString() : "NULL")}");
						Print($"[CONFIG-ERROR] Strategy will terminate. Please check config file at:");
						Print($"[CONFIG-ERROR] C:\\Users\\aport\\Documents\\NinjaTrader 8\\bin\\Custom\\Strategies\\OrderManager\\Configs\\{SpecificStrategyName}.json");
						endAll = true;
						return;
					}
					
					strategyExecutionMode = currentConfig.ExecutionMode;
					microContractStoploss = (double)currentConfig.StopLoss;
					microContractTakeProfit = (double)currentConfig.TakeProfit;
					softTakeProfitMult = (double)currentConfig.SoftTakeProfitMult;
					PullBackExitEnabled = (bool)currentConfig.PullBackExitEnabled;
					TakeBigProfitEnabled = (bool)currentConfig.TakeBigProfitEnabled;
					// Config loaded successfully - display values
					Print($"[CONFIG-SUCCESS] Loaded {SpecificStrategyName}:");
					Print($"[CONFIG-SUCCESS]   TakeProfit: ${microContractTakeProfit}");
					Print($"[CONFIG-SUCCESS]   StopLoss: ${microContractStoploss}");
					Print($"[CONFIG-SUCCESS]   SoftTPMult: {softTakeProfitMult}");
					Print($"[CONFIG-SUCCESS]   PullBackExit: {PullBackExitEnabled}");
					Print($"[CONFIG-SUCCESS]   TakeBigProfit: {TakeBigProfitEnabled}");
				}
				else
				{
					Print($"[CONFIG-WARNING] No SpecificStrategyName provided - using default risk parameters");
					// Create empty config that will use defaults
					currentConfig = new StrategyConfig();
				}
				// ========== END CONFIG VALIDATION ==========
				
				// Send session configuration to relay after config is loaded
				if (dropletService != null)
				{
					_ = Task.Run(async () => await SendSessionConfigToRelay());
				}
				
				// Initialize ProjectX Bridge if BlueSky_projectx broker is selected
				if (selectedBroker == brokerSelection.BlueSky_projectx)
				{
					projectXBridge = new ProjectXBridge(this);
					_ = Task.Run(async () => await InitializeProjectXBridge());
				}
				
			
				
			    TradingHours tradingHours = Bars.TradingHours; // Get trading hours from the instrument
	 
				// Set up callbacks
		        myStrategyControlPane.StrategyCallbackEntry = (entryOrderAction, isThisMarketPosition, isNotMarketPosition) =>
		        {
		            OnMyButtonClickActions(entryOrderAction, isThisMarketPosition, isNotMarketPosition);
		        };
		        myStrategyControlPane.StrategyCallbackExit = () =>
		        {
		            OnMyButtonClickExit();
		        };
				

				startTime = DateTime.Now;
			
				Random rand = new Random();
			

				VWAP1 = custom_VWAP(vwap1);
				SMA200 = SMA(BarsArray[0],100);
				
				EMA3 = EMA(BarsArray[0],14);
				EMA4 = EMA(BarsArray[0],25);
		
				
				
				
				
				
				
				
				
				RSI14 = RSI(14,1);
				//AddChartIndicator(EMA3);
				//AddChartIndicator(VWAP1);
				
				
				BB0 = Bollinger(1.4,25);
				//AddChartIndicator(BB0);
	
				EMA3.Plots[0].Brush = Brushes.Fuchsia;
				EMA3.Plots[0].Width = 2;
				EMA3.Plots[0].DashStyleHelper = DashStyleHelper.Dash;
			
				
				for (int i = 0; i < BarsArray.Length; i++)
			    {
			        int currentMinuteValue = BarsArray[i].BarsPeriod.Value;

			        // Compare the current series with the smallest minute value found so far
			        if (currentMinuteValue < smallestMinuteValue)
			        {
			            smallestMinuteValue = currentMinuteValue;
			            smallestBarsInProgress = i;
			        }
			    }
				  // Remove hardcoded override - use calculated value
				  // smallestBarsInProgress = 1;
				for (int i = 0; i < BarsArray.Length; i++)
			    {
			        int currentMinuteValue = BarsArray[i].BarsPeriod.Value;

			        // Compare the current series with the smallest minute value found so far
			        if (currentMinuteValue > largestMinuteValue)
			        {
			            largestMinuteValue = currentMinuteValue;
			            largestBarsInProgress = i;
			        }
			    }

					
				
			
			    // Now, smallestBarsInProgress contains the BarsInProgress for the smallest minute value series
			    // You can use this information to execute specific code for the smallest minute series
				  // Specify the file path from where you want to load the XML file
		                // Initialize StartDate and EndDate based on the performance data
				
	 			//stores the sessions once bars are ready, but before OnBarUpdate is called
	    		sessionIterator = new SessionIterator(Bars);
				/// threading
				
				
				}
				else if (State == State.Realtime)
			    {
					isRealTime = true;
					MasterSimulatedStops.Clear();
					MasterSimulatedEntries.Clear();
					/// Flatten the position if it's not already flat
					if (Position.MarketPosition != MarketPosition.Flat)
					{
					    //Print("Flattening position before real-time trading starts.");
					    ExitActiveOrders(ExitOrderType.ASAP_Other,signalExitAction.FE_EXIT3,false);
					}		
					dailyProfit = 0;

					//Set service to real-time mode for async operations
					 if (curvesService != null)
					 {
						curvesService.SetStrategyState(false); // false = real-time mode
						Print("CurvesService set to Real-time mode (async behavior)");
					 }
					
					
				}
								else if (State == State.Historical)
				{
					///threads
					/// threading
					
					//  Set service to historical mode for sync operations  
					 if (curvesService != null)
					 {
						curvesService.SetStrategyState(true); // true = historical mode
						Print("CurvesService set to Historical mode (sync behavior)");
					 }
					 
					 

				
				}

				else if (State == State.Terminated)
				{
				   // Check if termination logic has already run for this instance
				   if (isTerminationLogicRun)
				   {
					   return; 
				   }
				   isTerminationLogicRun = true; // Set the flag
				   
				   // Track instance termination
				   lock(InstanceLock)
				   {
					   ActiveInstances.Remove(this.GetHashCode());
					   // Only log for instances that had meaningful initialization
					   if (!IsInStrategyAnalyzer || Account != null)
					   {
						   Print($"[INSTANCE] Terminated: {this.GetHashCode()}, Remaining Active: {ActiveInstances.Count}");
					   }
				   }
				
					// Only print detailed cleanup for non-test instances
					bool isTestInstance = IsInStrategyAnalyzer && Account == null;
					if (!isTestInstance)
					{
						Print($"State.Terminated: Executing termination logic for instance {this.GetHashCode()}.");
						Print("State == State.Terminated");
						
						// Print profit tracking dictionary
						Print("=== PROFIT BY TIMESTAMP TRACKING ===");
						Print($"Total tracked profit calculations: {profitByTimeStamp.Count}");
						foreach (var kvp in profitByTimeStamp.OrderBy(x => x.Key))
						{
							Print($"Time: {kvp.Key} | Profit: ${kvp.Value:F2}");
						}
						Print("=== END PROFIT TRACKING ===");
					}
				

					// Break circular references to prevent memory leaks
					try {
						if (myStrategyControlPane != null)
						{
							myStrategyControlPane.AssociatedStrategy = null;
							myStrategyControlPane = null;
						}
						
						// Clear collections
						MasterSimulatedEntries?.Clear();
						MasterSimulatedStops?.Clear();
						OrderActionResultList?.Clear();
						signalQueue?.Clear();
						Print("Collections and references cleared successfully");
					} catch (Exception ex) {
						Print($"Error clearing collections: {ex.Message}");
					}
					
					if (!isTestInstance)
					{
						Print("----------PATTERNS BEGIN---------");
						foreach(var kvp in patternIdProfits)
						{
						    string pattern = kvp.Key;
						    double profit = kvp.Value;
							
							Print($"pattern {pattern} - ${profit}");
						}
						Print("----------PATTERNS END---------");
						// 1. Reset static data in CurvesV2Service
						Print("1. Resetting all static data in CurvesV2Service");
					}
					CurvesV2Service.ResetStaticData();
					
					// 2. Send performance summary before disposing
					if (curvesService != null)
					{
						try 
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
							
							Print("2. Disposing CurvesV2Service instance (static reset handled within Dispose)");
							curvesService.Dispose();
						}
						catch (Exception ex)
						{
							Print($"Error disposing CurvesV2Service instance {this.GetHashCode()}: {ex.Message}");
						}
						finally
						{
							curvesService = null;
						}
					}
					else if (!isTestInstance)
					{
						Print("1. No active CurvesV2Service instance to dispose for instance {this.GetHashCode()}");
					}
					
					// 2.5. Dispose DropletService
					if (dropletService != null)
					{
						try 
						{
							// DISABLED: Custom session summary (customers should explicitly choose custom vs standard)
							// Task.Run(async () => 
							// {
							//	try
							//	{
							//		await SendCustomSessionSummary();
							//	}
							//	catch (Exception ex)
							//	{
							//		Print($"[DROPLET] Error sending final session data: {ex.Message}");
							//	}
							// }).Wait(TimeSpan.FromSeconds(3)); // Wait up to 3 seconds
							
							Print("2. Disposing DropletService instance");
							dropletService.Dispose();
						}
						catch (Exception ex)
						{
							Print($"Error disposing DropletService instance: {ex.Message}");
						}
						finally
						{
							dropletService = null;
							dropletIntegration = null; // Clear reference to dropletIntegration
						}
					}
					else if (!isTestInstance)
					{
						Print("2. No active DropletService instance to dispose");
					}
					
					// 3. End training data session to ensure final batch write
					if (trainingDataClient != null)
					{
						try 
						{
							if (!isTestInstance)
							{
								Print("2. Ending training data session for final batch write");
							}
							_ = Task.Run(async () => {
								try {
									await trainingDataClient.EndSession();
									if (!isTestInstance) {
										Print("2. Training data session ended successfully");
									}
								} catch (Exception ex) {
									Print($"Error ending training data session: {ex.Message}");
								}
							});
						}
						catch (Exception ex)
						{
							Print($"Error initiating training data session end: {ex.Message}");
						}
					}
					else if (!isTestInstance)
					{
						Print("2. No training data client to end session for");
					}
					
					
				}
			}
			catch (Exception ex)
			{
			    Print($"CRITICAL ERROR in OnStateChange during {State.ToString()} for instance {this.GetHashCode()}: {ex.Message}\nStackTrace: {ex.StackTrace}");
			    Print("Error in OnStateChange during " + State.ToString() + ": " + ex.Message + " StackTrace: " + ex.StackTrace);
			}
			
			
		}
			
		

		protected virtual void debugBarState()
		{
			
		}


   /// ON BAR UPDATE
   		protected override void OnBarUpdate()
		{
		DebugFreezePrint("OnBarUpdate START");
		if(State == State.Historical && endAll == true)
		{		
			return;
		}
		string msg = "OnBarUpdate start";
		try{
		DebugFreezePrint("Basic validation checks");
		///try
		//	{
		if(strategyDefaultQuantity > strategyMaxQuantity || strategyDefaultQuantity > accountMaxQuantity || strategyMaxQuantity > (accountMaxQuantity-1))
		{
			Print("THERES A POSITION SIZING ERROR! strategyDefaultQuantity = "+strategyDefaultQuantity+" strategyMaxQuantity = "+strategyMaxQuantity+" (accountMaxQuantity-1) = "+(accountMaxQuantity-1));
			return;
		}
		
		 msg = "OnBarUpdate start 2";

		DebugFreezePrint("Checking BarsRequiredToTrade");
		// TEMPORARY: Reduced bars requirement for faster backtesting
		// Allow Series0 to be 2 bars short to handle synchronization issues
		// Fix for Strategy Analyzer getting stuck at 95%
		if (CurrentBars[0] < BarsRequiredToTrade - 2) // Allow 2 bar tolerance
		{
		    DebugPrint(debugSection.OnBarUpdate, "start 3");
			
			// Show progress for both data series
			double series0Progress = Math.Round((((double)CurrentBars[0]/BarsRequiredToTrade)*100),1);	double series1Progress = Math.Round((((double)CurrentBars[1]/(BarsRequiredToTrade*12))*100),1);
			Print($"Loading: Series0={series0Progress}% ({CurrentBars[0]}/{BarsRequiredToTrade})");
		    return;
		}
		else if (CurrentBars[0] < BarsRequiredToTrade)
		{
			// We're within 2 bars of requirement - proceed anyway to avoid getting stuck
			Print($"[LOADING] Near completion: {CurrentBars[0]}/{BarsRequiredToTrade} bars. Proceeding to avoid Strategy Analyzer hang.");
		}
		 msg = "OnBarUpdate start 2.1";
		if(UNRPNL != null )
		{
			msg = "OnBarUpdate start 2.1 a";
			if(GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Short)
			{
				msg = "OnBarUpdate start 2.1 b";
				UNRPNL.SetShortProfitValue(OpenPositionProfitRisk);
				UNRPNL.SetLongProfitValue(0);

			}
			else if(GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Long)
			{
				msg = "OnBarUpdate start 2.1 c";
				UNRPNL.SetLongProfitValue(OpenPositionProfitRisk);
				UNRPNL.SetShortProfitValue(0);
			
			}
			else if(GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Flat)
			{
				msg = "OnBarUpdate start 2.1 d";
				UNRPNL.SetLongProfitValue(0);
				UNRPNL.SetShortProfitValue(0);
			}
		}
		 msg = "OnBarUpdate start 3";
		if(BarsInProgress == largestBarsInProgress)
		{
			bool notEnough = cumProfitArray[0] != 0 && cumProfitArray[1]  != 0 && cumProfitArray[2]  != 0 && cumProfitArray[0] < 1000 && cumProfitArray[1] < 1000 && cumProfitArray[2] < 1000;
			if((SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit <-5000/* || notEnough*/ ) && (State == State.Historical))
			{
				endAll = true;
				Print($"END ALL ${SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit} OR notEnough = {notEnough}");
				return;
			}
		
			
		}
		/// <summary>
		///  for API CALLS, capture the timeframes
		/// GET time ofr first tick
				
		
		
		/// check for session close
		DateTime now = Time[0];
		DebugFreezePrint("Session close check START");
			 msg = "OnBarUpdate start 3444";

		sessionIterator.GetNextSession(Time[0], true);
	 msg = "OnBarUpdate start 3555";

		DateTime sessionEnd = sessionIterator.ActualSessionEnd;
		
	    DateTime flattenTime = sessionEnd.AddMinutes(-10); // 10 minutes before session end
	 msg = "OnBarUpdate start 3666";
		DebugFreezePrint("Session close check COMPLETE");
		
	    // Reset announcement flag when outside flatten window (new session)
	    if (now < flattenTime)
	    {
	        flattenWindowAnnounced = false;
	    }
	    
	    // Use time window instead of exact match for more reliable triggering
	    if (now >= flattenTime && now < sessionEnd) // In the 10-minute flattening window
	    {
			DebugFreezePrint("HIT SESSION CLOSE WINDOW");
			
			// One-time announcement when entering flatten window
			if (!flattenWindowAnnounced)
			{
				Print($"⚠️ SESSION CLOSE PROTECTION: Entered 10-minute flatten window. Time: {now:HH:mm:ss}, Session ends: {sessionEnd:HH:mm:ss}");
				flattenWindowAnnounced = true;
			}
			
			if(GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Flat)
			{
		        MarketPosition currentPosition = GetMarketPositionByIndex(BarsInProgress);
		        Print($"🔄 AUTO-FLATTEN: Flattening {currentPosition} position at {now:HH:mm:ss}. Session ends at {sessionEnd:HH:mm:ss} (10 min window)");
		        Print($"📋 Using ExitActiveOrders() with proper ExitLong/ExitShort calls (not NT built-in EOSC)");
		        ExitActiveOrders(ExitOrderType.EOSC,signalExitAction.FE_EOD,false);
				
	    	}
			//Print("RETURN : HIT FE_EOD 1");

			return;/// dont do more things
		}
	 msg = "OnBarUpdate start 3777";

		/// FOR EOSC, Track the open avg pricepoint
		double totalPrice = 0;
		int totalQuantity = 0;
		
		lock (eventLock)
		{
			foreach (var openOrder in openOrders)
			{
			    // Ensure the order is not null and is an entry order
			    if (openOrder.EntryOrder != null && openOrder.ExitOrder == null)
			    {
			        totalPrice += openOrder.EntryOrder.AverageFillPrice * openOrder.EntryOrder.Quantity;
			        totalQuantity += openOrder.EntryOrder.Quantity;
			
			        
			    }
			}
		}
		// Calculate the average entry price across all open orders
		avgRollingEntryPrice = totalQuantity > 0 ? totalPrice / totalQuantity : 0;

		double NetProfit = SystemPerformance.AllTrades.TradesPerformance.NetProfit;
 		msg = "OnBarUpdate start 4";
	
		
		if (BarsArray[0] == null)
        {
            Print("OnBarUpdate BarsArray[0] is null.");
          //return null;
        }
		
			 msg = "OnBarUpdate start 4.5";
		
		/// on new bars session, find the next trading session
		  if (Bars.IsFirstBarOfSession)
		  {
		    //Print("Calculating trading day for " + Time[0]);
		    // use the current bar time to calculate the next session
		    sessionIterator.GetNextSession(Time[0], true);
		 
		    // store the desired session information
		    DateTime tradingDay   = sessionIterator.ActualTradingDayExchange;
		    DateTime beginTime   = sessionIterator.ActualSessionBegin;
		    DateTime endTime  = sessionIterator.ActualSessionEnd;
		 
	
		  }
		/// check the stats thread for all open order stats
	
		DebugFreezePrint("Stats thread check START");
		if (!stopStatsThread)
		{

	        lock (statsLock)
	        {
	        	DebugFreezePrint("Inside statsLock");
	             foreach (simulatedStop simStop in MasterSimulatedStops)
	                {
	                    if (simStop.OrderRecordMasterLite != null)
	                    {
							
			                OrderPriceStats priceStats = simStop.OrderRecordMasterLite.PriceStats;
			                if (priceStats != null)
			                {
			                   // Print($"Order: {simStop.OrderRecordMasterLite.EntryOrderUUID}, Profit: {priceStats.OrderStatsProfit}, ATH Profit: {priceStats.OrderStatsAllTimeHighProfit}, ATL Profit: {priceStats.OrderStatsAllTimeLowProfit}");
			                }
							if(simStop.OrderRecordMasterLite.OrderSupplementals.forceExit == true)
							{
								DebugFreezePrint("Processing forceExit");
								
								// Safety check: Ensure exit action is set
								if (simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction == signalExitAction.NULL)
								{
									// Set a default exit action if none was set
									simStop.OrderRecordMasterLite.OrderSupplementals.thisSignalExitAction = signalExitAction.MLL; // Default to max loss
									simStop.OrderRecordMasterLite.OrderSupplementals.ExitReason = "Force exit (default)";
									Print($"[FORCE-EXIT] Setting default exit action for {simStop.EntryOrderUUID}");
								}
								
								int instrumentSeriesIndex = simStop.instrumentSeriesIndex;
					            var action = simStop.OrderRecordMasterLite.EntryOrder.OrderAction == OrderAction.Buy ? OrderAction.Sell : OrderAction.BuyToCover;
					            // Execute the force exit order
					            int orderSeriesIndex = GetOrderSeriesIndex();
					            if(action == OrderAction.Sell)
								{
									ExitLong(orderSeriesIndex,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
								}
								if(action == OrderAction.BuyToCover)
								{
									ExitShort(orderSeriesIndex,simStop.OrderRecordMasterLite.EntryOrder.Quantity,simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
								}
								/*SubmitOrderUnmanaged(
					                instrumentSeriesIndex,
					                action,
					                OrderType.Market,
					                simStop.OrderRecordMasterLite.EntryOrder.Quantity,
					                0,
					                0,
					                simStop.OrderRecordMasterLite.EntryOrderUUID,
					                simStop.OrderRecordMasterLite.ExitOrderUUID
					            );
					            // Reset the flag to prevent duplicate submissions
								*/
					            simStop.OrderRecordMasterLite.OrderSupplementals.forceExit = false;
					
					          // Print($"Submitted force exit for {simStop.OrderRecordMasterLite.EntryOrderUUID}");
							}
						}
	            }
	        }
		}
		DebugFreezePrint("Stats thread check COMPLETE");
		
		///updatePriceStats();/// moved to thread
		///syncStops();/// moved to thread
		OpenPositionProfitRisk = CalculateTotalOpenPositionProfit();
		
	
		if(MasterSimulatedEntries.Count() > 0)
		{
			lock (eventLock)
			{
			simulatedEntryConditions();
			}
		}
		if(MasterSimulatedStops.Count() > 0)
		{
			DebugFreezePrint("MasterSimulatedStops processing START");
			
			
				lock (eventLock)
				{
					OrderActionResultList.Clear();
				    foreach (var simStop in MasterSimulatedStops)
				    {
				        try
				        {
				            msg = "MasterSimulatedStops 1";
				      
				            // Process on both BarsInProgress 0 and 1 for faster exits
				            // Use the current series for evaluation but allow both to trigger
				            OrderActionResult exitAction = null;
				            
				            if (BarsInProgress == 0 || BarsInProgress == 1)
				            {
				                // Always evaluate using the instrument's series index, not the current BarsInProgress
				                // This allows 5-second updates to trigger exits for primary series positions
				                int evaluationBar = (simStop.instrumentSeriesIndex == 0) ? CurrentBars[0] : CurrentBars[BarsInProgress];
				                exitAction = UpdateOrderStats(simStop, simStop.instrumentSeriesIndex, evaluationBar);
				            }
				            
				            // Only add actions that need to be executed
				            if (exitAction != null && exitAction.accountEntryQuantity > 0)
				            {
				                OrderActionResultList.Add(exitAction);
				            }
				            
				        }
				        catch (Exception ex)
				        {
				            Print($"[ERROR] Error updating stats for simulated stop: {ex.Message} + {msg}");
				        }
				    }
				}
			
				if(OrderActionResultList.Count() > 0)
				{
					int totalAccountQuantityX = getAllcustomPositionsCombined();
				
				    // Check if we have room to scale for ANY actions
				    if(totalAccountQuantityX+strategyDefaultQuantity <= strategyMaxQuantity && 
				       totalAccountQuantityX+strategyDefaultQuantity <= (accountMaxQuantity) && 
				       getAllcustomPositionsCombined() < strategyMaxQuantity)
				    {
						int limit = strategyMaxQuantity - getAllcustomPositionsCombined();
				        // Execute scale-in actions
				        for(int i = 0; i < limit; i++)
				        {
				            var action = OrderActionResultList[i];
				            
				            // Use approval-gated entry during real-time, direct entry in historical
				            
				           
				                EntryLimitFunctionLite(
				                    action.accountEntryQuantity, 
				                    action.OA, 
				                    action.signalPackageParam, 
				                    "SCALE IN", 
				                    CurrentBars[0], 
				                    action.orderType, 
				                    action.builtSignal
				                );
				                BackBrush = Brushes.DarkBlue;
				               
				            
							break;
				        }
						OrderActionResultList.Clear();
				    }
				    else
				    {
				        // No room to scale - execute exits instead
				        Print($"[NO-ROOM] Portfolio full, executing exits for {OrderActionResultList.Count()} signals");
				        // TODO: Add exit logic here if needed
				    }
				}
			
			DebugFreezePrint("MasterSimulatedStops processing COMPLETE");
			
	
		}
		
		  msg = "OnBarUpdate start 1";
		/// clear anything that might be stuck
		clearExpiredWorkingOrders();
		  msg = "OnBarUpdate start 1.1";
		/// SUPER IMPORTANT. IF ANYTHING ISNT CAPTURED IN OUR LISTS, IT CAN WRECK US. 
		/// JUST EXIT ANY POSITIONS IF WE HAVE NO STOPS BUT WE ARE NOT FLAT
		
		if(BarsInProgress == largestBarsInProgress)     
		{

			if (Bars.IsFirstBarOfSession)
		    {
		        // Process or log the previous day's profit here
		       // Print(" Daily Profit :"+dailyProfit+" resetting!");
				msg = "OnBarUpdate start 2 c";
				dailyProfitDynamic = 0;
		        dailyProfit = 0;
				dailyProfitGoal = dailyProfitGoalParameter;
				dailyProfit_Long = 0;
				dailyProfit_Short = 0;
		  		openRisk = 0;
				dailyProfitATH = 0;
				firstBarOfSession = CurrentBars[0];			
				//dailyProfitMaxLoss = 0;
				BackBrush = Brushes.WhiteSmoke;
				dailyLossATL = 0;
		    }
		
			previousNetProfit = currentNetProfit;
		    currentNetProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit; // Or however you access this in your strategy
		    
			
	
			
			 msg = "OnBarUpdate start 1.4";
				
			   
			
			//////////USING TRADING HOUR TEMPLATES!
			
		
			
			}
			double unrealizedDailyProfit = dailyProfit + CalculateTotalOpenPositionProfit();
			
			// ========== IMPROVED DAILY PROFIT MANAGEMENT ==========
			
			// 1. DAILY LOSS LIMIT - Stop trading if unrealized daily loss exceeds limit
			// Using positive value for clarity (e.g., 500 means $500 max loss)
			double dailyLossLimit = Math.Abs(dailyProfitMaxLoss); // Convert to positive for clarity
			
			if(unrealizedDailyProfit <= -dailyLossLimit)
			{
				// Track worst daily loss (most negative value)
				dailyLossATL = Math.Min(dailyLossATL, unrealizedDailyProfit);
				
				if(GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Flat)
				{
					Print($"[DAILY LOSS LIMIT] Unrealized P&L: ${unrealizedDailyProfit:F2} exceeded limit of -${dailyLossLimit:F2}");
					ExitActiveOrders(ExitOrderType.ASAP_OutOfMoney, signalExitAction.FE_CAP_ML, false);
				}
			
				return;
			}
			
			// 2. HARD STOP on realized losses
			if(dailyProfit <= -dailyLossLimit)
			{
				//Print($"[HARD STOP] Realized daily loss: ${dailyProfit:F2} exceeded limit of -${dailyLossLimit:F2}");
				//BackBrush = Brushes.DarkRed;
				return;	
			}
			
			if(dailyProfit > dailyProfitATH) 
			{
				dailyProfitATH = dailyProfit;
				// Print($"[ATH] New daily profit high: ${dailyProfitATH:F2}");
			}
				
			if(dailyProfit > dailyProfitATH)
			{
				//Print($"[HARD STOP] Realized daily loss: ${dailyProfit:F2} exceeded limit of -${dailyLossLimit:F2}");
				// = Brushes.Green;
				return;	
			}
			// 3. Process profit goals only on main time frame
			/*if(BarsInProgress == largestBarsInProgress)
			{
				// PROFIT GOAL TRACKING - Increment goals as we hit milestones
				if(dailyProfit > 0 && dailyProfitGoalParameter > 0)
				{
					// Use integer division for reliable milestone tracking
					int currentMilestone = (int)(dailyProfit / dailyProfitGoalParameter);
					int goalLevel = (int)(dailyProfitGoal / dailyProfitGoalParameter);
					
					// If we've reached a new milestone, update the goal
					if(currentMilestone > goalLevel)
					{
						double previousGoal = dailyProfitGoal;
						dailyProfitGoal = dailyProfitGoalParameter * (currentMilestone + 1);
						Print($"[PROFIT GOAL] Increased from ${previousGoal:F2} to ${dailyProfitGoal:F2} (Milestone {currentMilestone} reached)");
					}
				}
				
				// ATH TRACKING
				
				
				// TRAILING PROFIT PROTECTION - Lock in profits after goal achieved
				// Using percentage-based buffer for flexibility
				double trailingBufferPercent = 0.20; // 20% pullback allowed from goal
				double trailingBuffer = dailyProfitGoal * trailingBufferPercent;
				
				bool goalWasAchieved = dailyProfitATH >= dailyProfitGoal && dailyProfitGoal > 0;
				bool profitPulledBack = unrealizedDailyProfit < (dailyProfitGoal - trailingBuffer);
				
				if(goalWasAchieved && profitPulledBack)
				{
					if(GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Flat)
					{
						Print($"[TRAILING STOP] Peak: ${dailyProfitATH:F2}, Current: ${unrealizedDailyProfit:F2}");
						Print($"[TRAILING STOP] Exit triggered: Below ${dailyProfitGoal - trailingBuffer:F2} (Goal: ${dailyProfitGoal:F2} - {trailingBufferPercent*100}% buffer)");
						ExitActiveOrders(ExitOrderType.ASAP_OutOfMoney, signalExitAction.FE_CAP_TP_SAFE, false);
					}
					BackBrush = Brushes.Aqua;
					return;
				}
			}
			*/
			// ========== END OF IMPROVED DAILY PROFIT MANAGEMENT ==========
			
			/// Calculate the current tier and next tier based on daily profit
			int currentTier = (int)(Math.Floor((double)dailyProfit / dailyProfitGoalParameter) * dailyProfitGoalParameter);
			double nextTierGoal = currentTier + dailyProfitGoalParameter;
			
			/// If unrealized profit dips below the current tier threshold
			if (unrealizedDailyProfit < currentTier && dailyProfitATH > dailyProfitGoalParameter)
			{
			    if (dailyProfitATH > currentTier && Bars.IsLastBarOfSession)
			    {
			        // Log if necessary
			        // Print($"DAILY PROFIT DROP: Current Tier {currentTier}, Profit {dailyProfit}, ATH {dailyProfitATH}");
			    }
			
			    // Exit open positions immediately
			    if (GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Flat)
			    {
			        ExitActiveOrders(ExitOrderType.ASAP_OutOfMoney, signalExitAction.FE_CAP_ML, false);
			    }
			    return;
			}
			
			/// If profits surpass the next tier, adjust the daily goal to the next increment
			if (dailyProfit >= nextTierGoal)
			{
			    dailyProfitGoal = nextTierGoal; // Move goal to the next tier
			    // Log if needed
			    // Print($"New Goal Tier: {dailyProfitGoal}");
			}	
			/// take out trash
			/// 
		
				
			while(MastersimulatedEntryToDelete.Count > 0)
			{
				
				simulatedEntry simulatedEntryDelete = MastersimulatedEntryToDelete.Dequeue();
				MasterSimulatedEntries.Remove(simulatedEntryDelete);
			}
			while(MastersimulatedStopToDelete.Count > 0)	
			{
				//Print("Delete stop!");
				simulatedStop simulatedStopDelete = MastersimulatedStopToDelete.Dequeue();
				MasterSimulatedStops.Remove(simulatedStopDelete);
			}	

	
			DebugPrint(debugSection.OnBarUpdate,"OnBarUpdate 7");
	        #region PNL
			/// CHECK PNL
			realizedPNL = SystemPerformance.AllTrades.TradesPerformance.GrossProfit - SystemPerformance.AllTrades.TradesPerformance.GrossLoss;
				
			
			unrealizedPNL = CalculateTotalOpenPositionProfit();
			
			
			
			if (unrealizedPNL < lowest_PNL)
			{
				
				lowest_PNL = unrealizedPNL; 
			}
		
			#endregion
			
			
			/// UPDATE ENTRY STOP LOSS
			openProfit = Math.Round(CalculateTotalOpenPositionProfit());
			msg = "OnBarUpdate btnStates 1";
			
			myStrategyControlPane.updateStates();
			
			
			/// for TopStep reqs
			
			msg = "almost BuildNewSignal";
		
			
			int totalAccountQuantity = getAllcustomPositionsCombined();
			//Print($"[DEBUG] totalAccountQuantity: {totalAccountQuantity}, BarsInProgress: {BarsInProgress}");

			
			if(BarsInProgress == 1)
			{
				// Add these two lines:
			    SignalBackgroundCheck(); // Signal the background thread to check stops
			    ProcessStatsQueue();     // Process any pending updates to collections
			}
			
			//Print($"[DEBUG] About to check entry signals - BarsInProgress: {BarsInProgress}");				
			
			///get entry signals - ONLY ON PRIMARY SERIES (BarsInProgress = 0)
			if(BarsInProgress != 0 || !IsFirstTickOfBar)
			{
				return; // Skip signal generation on secondary series
			}
			
			//FunctionResponses newSignal = BuildNewSignal();
			msg = "about BuildNewSignal";
			DebugFreezePrint("About to call BuildNewSignal");
			//Print($"[MAIN] About to call BuildNewSignal - BarsInProgress: {BarsInProgress}, Time: {Time[0]}");
			
			
			patternFunctionResponse builtSignal = BuildNewSignal(); 
			FunctionResponses newSignal = builtSignal.newSignal;
			//Print($"[DEBUG-CRITICAL] newSignal assigned from builtSignal: {newSignal}");
			DebugFreezePrint("BuildNewSignal returned");
			msg = "after BuildNewSignal";
			//Print($"[MAIN] BuildNewSignal returned: {newSignal}, patternSubType: {builtSignal.patternSubType}");
			
			// NEW: RF Model Filtering
		
			if(builtSignal.newSignal == FunctionResponses.NoAction)
			{
				///no signal
				return;
			}
			Print($"builtSignal.newSignal = {builtSignal.newSignal.ToString()}");
		

			  // DEBUG: Check each condition
			  
			if(lastActionBar < CurrentBars[0] && totalAccountQuantity+strategyDefaultQuantity <= strategyMaxQuantity && totalAccountQuantity+strategyDefaultQuantity <= (accountMaxQuantity) && getAllcustomPositionsCombined() < strategyMaxQuantity) /// eg mcl = 1, sil = 1 , and we're considering mcg 2.  if 2 is less than 5 do something.
			{
				
				DebugFreezePrint("Signal processing START");
				msg = "BuildNewSignal()?";
				
				
				signalPackage noActionSignalPackage = new signalPackage
				{
					SignalReturnAction = new signalReturnAction(signalReturnActionEnum.noAction.ToString(), signalReturnActionType.Neutral), // Default sentiment
				
				};
				
				signalPackage thisSignalPackage = noActionSignalPackage;
				
			
				  msg = "OnBarUpdate start 1.5";
				
				///end just to count
				
			
				if(isRealTime && realTimeCounter < 5)
				{
				//	Print("ANNOUNCEMENT: realTimeCounter "+realTimeCounter+" of 5 Patterns Available: "+PatternObject.patternDictionary.Count()+" Time: "+Time[0]);
					realTimeCounter++;
				}
				bool dequeueLast = false;
				Print($"[DEBUG] newSignal check: newSignal={newSignal}, is null? {newSignal == null}, is NoAction? {newSignal == FunctionResponses.NoAction}");
				if (newSignal == null || newSignal == FunctionResponses.NoAction)
				{
					Print($"[DEBUG] Signal blocked: newSignal is {(newSignal == null ? "NULL" : "NoAction")}");
					if(signalQueue.Count() > 0)
					{
						//BackBrush = Brushes.Blue;
						dequeueLast = true;
					}
					else if(signalQueue.Count() == 0)
					{
						
						
					}
				}
				msg = "OnBarUpdate start 1.5A";
				int targetSeriesIndex = 0; /// e.g. SIL
				/// set series index that we are targeting
				FunctionResponses signalPackageSignal = newSignal;
		
				///assign the correct series index and then reset long/short value to single set
				if( newSignal == FunctionResponses.EnterLongAggressive ||  newSignal == FunctionResponses.EnterShortAggressive)
				{	
						
					msg = "OnBarUpdate start 1.5B";
					targetSeriesIndex = 0; /// e.g. SI
					Print("newSignal Std");
				}
				if(targetSeriesIndex == 0)
				{
					
					
						thisSignalPackage = getOrderFlowSignalPackage(targetSeriesIndex, newSignal, noActionSignalPackage);
						//Print("newSignal Micro"+thisSignalPackage.SignalReturnAction);
				}
				else
				{
					thisSignalPackage = getOrderFlowSignalPackage(targetSeriesIndex, newSignal, noActionSignalPackage);
				}
				//Print($"{Time[0]} index {thisSignalPackage.instrumentSeriesIndex}, getAllcustomPositionsCombined() {getAllcustomPositionsCombined()} && GetMarketPositionByIndex(BarsInProgress) {GetMarketPositionByIndex(BarsInProgress)}");

				msg = "OnBarUpdate start 1.5C";
				//Print(standardPattern.SOMGrid.ToString());
				DebugPrint(debugSection.OnBarUpdate,"Mark 5 ");	
				msg = "thisSignalPackage";
				
			
			//// NOW EMPTY QUEUE AND MAKE ENTRIES
			
					if (!signalsOnly)
					{		
									lastFunction = "DO accrualGO NAME OK";
								
					                   if (!isRealTime)///historical
										{
										    // Use the strategy's last entry time if the account is null
										    AccountLastEntryTime = StrategyLastEntryTime;
										}
										else if(myAccount != null && isRealTime) /// realtime
										{
											
										    tempAccountLastEntryTime = GetLastEntryTime(myAccount);
											
										}
										//Print("tempAccountLastEntryTime "+tempAccountLastEntryTime+" AccountLastEntryTime "+AccountLastEntryTime);
										    AccountLastEntryTime = StrategyLastEntryTime >= tempAccountLastEntryTime 
										                            ? StrategyLastEntryTime 
										                            : tempAccountLastEntryTime;
										
					                    DateTime xMinutesSpacedGoal = StrategyLastEntryTime.AddMinutes(entriesPerDirectionSpacingTime);
					                    if (Times[0][0] >= xMinutesSpacedGoal && entriesPerDirectionSpacingTime > 0)
					                    {
					                        lastFunction = "DO accrualGO TIME OK";
					                        if (!IsInStrategyAnalyzer && State == State.Realtime) Print("entry spacing ok");
					
					                       
											
											int indexQuantity = strategyDefaultQuantity;
											//if(isRealTime) Print($"{signalPackageSignal} continue BarsInProgress: {BarsInProgress}, Time: {Times[BarsInProgress][0]}");

					                        if (thisSignalPackage.SignalReturnAction.Sentiment == signalReturnActionType.Bullish && marketPosAllowed != marketPositionsAllowed.Short)
					                        {   
																							
												if(isRealTime) Print($"BAR: {CurrentBars[BarsInProgress]} TIME{Time[0]}, Bullish");
					                            lastFunction = "DO Bullish";
					                            if (GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Short)
					                            {
					                                
													if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Long)
					                            	{ 
														// ========== SOFT TAKE PROFIT CHECK FOR PYRAMIDING ==========
														// Only add to position if we have at least soft take profit in open profit
														double openProfit = CalculateTotalOpenPositionProfit();
														double softTPThreshold = softTakeProfitMult; // e.g., 0.2 * 500 = $100
														
														if (openProfit < softTPThreshold)
														{
															Print($"[PYRAMID BLOCKED] Open profit ${openProfit:F2} < Soft TP ${softTPThreshold:F2}");
															Print($"[PYRAMID BLOCKED] Need at least {softTakeProfitMult:P0} of TP (${softTakeProfitMult}) = ${softTPThreshold:F2}");
															return;
														}
														
														if (isRealTime) 
														{
															Print(Time[0] + " ENTER LONG FROM LONG");
															Print($"[PYRAMID APPROVED] Open profit ${openProfit:F2} >= Soft TP ${softTPThreshold:F2}");
														}
														// Use approval-gated entry during real-time, direct entry in historical
														
														EntryLimitFunctionLite(indexQuantity, OrderAction.Buy, thisSignalPackage, "", CurrentBars[0], mainEntryOrderType,builtSignal, currentConfig);
													
													}
													else if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Flat)
													{
														if (isRealTime) Print(Time[0] + " ENTER LONG FROM FLAT");
														// Use approval-gated entry during real-time, direct entry in historical
														
														EntryLimitFunctionLite(indexQuantity, OrderAction.Buy, thisSignalPackage, "", CurrentBars[0], mainEntryOrderType,builtSignal, currentConfig);
														
						                                return;
													}
													else
													{
														Print(Time[0] + " ENTER LONG WAS REJECTED");
													}
													
					                            }
					                        }
					                        else if (thisSignalPackage.SignalReturnAction.Sentiment == signalReturnActionType.Bearish && marketPosAllowed != marketPositionsAllowed.Long)
					                        {
												if(isRealTime) Print($"BAR: {CurrentBars[BarsInProgress]} TIME{Time[0]}, Bearish");
					                            lastFunction = "DO Bearish";
					                            if (GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Long)
					                            {   
													
					                          
					                                if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Short)
					                           		{  
														// ========== SOFT TAKE PROFIT CHECK FOR PYRAMIDING ==========
														// Only add to position if we have at least soft take profit in open profit
														double openProfit = CalculateTotalOpenPositionProfit();
														double softTPThreshold = softTakeProfitMult; // e.g., 0.2 * 500 = $100
														
														if (openProfit < softTPThreshold)
														{
															Print($"[PYRAMID BLOCKED] Open profit ${openProfit:F2} < Soft TP ${softTPThreshold:F2}");
															Print($"[PYRAMID BLOCKED] Need at least {softTakeProfitMult:P0} of TP (${softTakeProfitMult}) = ${softTPThreshold:F2}");
															return;
														}
														
														if (isRealTime)
														{
															Print(Time[0] + " ENTER SHORT FROM SHORT");
															Print($"[PYRAMID APPROVED] Open profit ${openProfit:F2} >= Soft TP ${softTPThreshold:F2}");
														}
														// Use approval-gated entry during real-time, direct entry in historical
														
														
														EntryLimitFunctionLite(indexQuantity, OrderAction.SellShort, thisSignalPackage, "", CurrentBars[0], mainEntryOrderType,builtSignal, currentConfig);
														
						                                return;
													}
													else if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Flat)
													{
														Print(Time[0] + " ENTER SHORTFROM FLAT");
														// Use approval-gated entry during real-time, direct entry in historical
														
													
														EntryLimitFunctionLite(indexQuantity, OrderAction.SellShort, thisSignalPackage, "", CurrentBars[0], mainEntryOrderType,builtSignal, currentConfig);
														
						                                return;
						                               
													}
													else
													{
														Print(Time[0] + " ENTER SHORT WAS REJECTED");
													}
													
					                            }
					                        }
					                    }
										else
										{
										
										}
								}
					
								
					thisSignalPackage.SignalReturnAction = new signalReturnAction(signalReturnActionEnum.noAction.ToString(), signalReturnActionType.Neutral);
					lastBar = CurrentBars[0];
			
				
		 	}

		
		DebugFreezePrint("OnBarUpdate END");
		}
		catch (Exception ex)
		{
		    DebugFreezePrint($"ERROR in OnBarUpdate: {ex.Message}");
		    Print("MAINSTRAT Error in OnBarUpdate  " + ex.Message +" "+msg);
		}
	}
		
		
		public signalPackage getOrderFlowSignalPackage(int seriesIndex,FunctionResponses entrySignalDirection, signalPackage fallbackSignalPackage)
		{
			string msg = "getOrderFlowSignalPackage 0";
			
			try
				{
				/// if never tried
				/// or if tried and what we think it shoulkd be matches what we expect
				
					msg = "getOrderFlowSignalPackage 4";
			        // Configure the winning signal
					bool fResponseLong = (entrySignalDirection == FunctionResponses.EnterLong || entrySignalDirection == FunctionResponses.EnterLongAggressive);
					bool fResponseShort = (entrySignalDirection == FunctionResponses.EnterShort || entrySignalDirection == FunctionResponses.EnterShortAggressive);
					
				
					
					msg = "getOrderFlowSignalPackage 5";
					double seriesIndexValue = seriesIndex == 3 ? instrumentSeriesStandardTickValue : instrumentSeriesMicroTickValue;

					// Update scalar
					
					signalReturnActionType directionPrediction = fResponseLong ? signalReturnActionType.Bullish : (fResponseShort ? signalReturnActionType.Bearish : signalReturnActionType.Neutral) ;
					msg = "getOrderFlowSignalPackage 6";
					
			        signalPackage aSignalPackage = new signalPackage
			        {
			            SignalReturnAction = new signalReturnAction("OFP-" + directionPrediction, directionPrediction),			           
			            Sentiment = directionPrediction,
						instrumentSeriesIndex = seriesIndex,
						instrumentSeriesTickValue = seriesIndexValue,
						price = GetCurrentBid(BarsInProgress),
						SignalContextId = CurvesV2Service.CurrentContextId ?? null
			        };
					
				    // Check if we found a matching pattern
					return aSignalPackage;
			//	}
				//BackBrush = Brushes.Yellow;
			//	return fallbackSignalPackage;
										
			}
			catch (Exception ex)
		    { 
		        Print("Error in getOrderFlowSignalPackage: " + ex.Message+" "+msg);
		     	return fallbackSignalPackage;
		    }
		}
		private double GetPriceRange(int period)
		{
		    double high = double.MinValue;
		    double low = double.MaxValue;
		
		    // Loop through the specified period of bars
		    for (int i = 0; i < period; i++)
		    {
		        if (High[i] > high)
		            high = High[i];
		        if (Low[i] < low)
		            low = Low[i];
		    }
		
		    // Calculate the range in ticks
		    double tickRange = high - low;
		
		    // Convert ticks to dollars using TickSize and PointValue
		    double dollarRange = tickRange * Instrument.MasterInstrument.PointValue * strategyDefaultQuantity;
		
		    return Math.Round(dollarRange, 2); // Round to 2 decimal places for clarity
		}

	
		private void CloseAndStopStrategy(string reason)
		{
		    try
		    {
		        Print("Closing and stopping strategy: " + reason);
		        Log("Closing and stopping strategy: " + reason, LogLevel.Error);
		        foreach (simulatedStop simStop in MasterSimulatedStops)
				{
			        // Flatten all positions
			        if (GetMarketPositionByIndex(BarsInProgress) != MarketPosition.Flat)
			        {
			            // Place market order to close position
			            int orderSeriesIndex = GetOrderSeriesIndex();
			            if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Long)
					    {
							
					        // Close Long position by selling the quantity
					        ExitLong(orderSeriesIndex,GetPositionCountByIndex(BarsInProgress),simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
							
							//SubmitOrderUnmanaged(BarsInProgress, OrderAction.Sell, OrderType.Market, GetPositionCountByIndex(BarsInProgress), 0, 0, null, "CloseAndStopStrategy_Long");
					        Print($"Closing Long position of {Position.Quantity} contracts.");
					    }
					    else if (GetMarketPositionByIndex(BarsInProgress) == MarketPosition.Short)
					    {
					        // Close Short position by buying to cover the quantity
							ExitShort(orderSeriesIndex,GetPositionCountByIndex(BarsInProgress),simStop.OrderRecordMasterLite.ExitOrderUUID,simStop.OrderRecordMasterLite.EntryOrderUUID);
					        //SubmitOrderUnmanaged(BarsInProgress, OrderAction.BuyToCover, OrderType.Market, GetPositionCountByIndex(BarsInProgress), 0, 0, null, "CloseAndStopStrategy_Short");
					        Print($"Closing Short position of {Position.Quantity} contracts.");
					    }
	
			        }
				}
		
		        ExitActiveOrders(ExitOrderType.ASAP_ExitSignal,signalExitAction.CLOSE_STOP,false);
		        
		
		        // Disable the strategy
		        CloseStrategy("CloseStrategy: "+reason);
		    }
		    catch (Exception ex)
		    {
		        Print("Error in CloseAndStopStrategy: " + ex.Message);
		        Log("Error in CloseAndStopStrategy: " + ex.ToString(), LogLevel.Error);
		    }
		}


		public void clearEverything()
		{
		    
		    if (MasterSimulatedEntries != null)
		    {
		        MasterSimulatedEntries.Clear();
		    }
		    if (MasterSimulatedStops != null)
		    {
		       // MasterSimulatedStops.Clear();
		    }

		}
		
	
		protected virtual patternFunctionResponse BuildNewSignal()
		{
			//Print("MainStrat [BuildNewSignal]");
			
			
			
			
			
			
			
				// Original Mode: Use traditional strategies
				// Legacy: patternFunctionResponse traditionalSignal = null;//traditionalStrategies.CheckAllTraditionalStrategies(this, TraditionalStrategyFilter,RiskAgentConfidenceThreshold);
				// Execute strategies based on filter selection
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
				if (traditionalSignal != null)
				{
					// Check for rapid direction changes
					string currentDirection = traditionalSignal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
					if (!string.IsNullOrEmpty(lastSignalDirection) && lastSignalDirection != currentDirection)
					{
						int barsSinceLastSignal = CurrentBars[0] - lastSignalBar;
						if (barsSinceLastSignal < signalCooldownBars * 2) // Double cooldown for opposite direction
						{
							Print($"[SIGNAL-COOLDOWN] Rejecting opposite direction signal - only {barsSinceLastSignal} bars since {lastSignalDirection} signal");
							patternFunctionResponse noSignal = new patternFunctionResponse();
							noSignal.newSignal = FunctionResponses.NoAction;
							noSignal.patternSubType = "direction_cooldown";
							return noSignal;
						}
					}
					
					// ========== SMA DIRECTION VERIFICATION ==========
					// Final qualifier: verify signal aligns with SMA trend
					// Calculate SMA direction using multiple periods for confirmation
					int smaPeriod1 = 20;  // Fast SMA
					int smaPeriod2 = 50;  // Medium SMA
					int smaPeriod3 = 100; // Slow SMA
					
					// Ensure we have enough bars
					if (CurrentBar > smaPeriod3)
					{
							// Calculate SMAs
							double sma20 = EMA(Close, smaPeriod1)[0];
							double sma50 = EMA(Close, smaPeriod2)[0];
							double sma100 = EMA(Close, smaPeriod3)[0];
							
							// Previous values for direction
							double sma20Prev = EMA(Close, smaPeriod1)[1];
							double sma50Prev = EMA(Close, smaPeriod2)[1];
							
							// Calculate SMA slopes
							double sma20Slope = sma20 - sma20Prev;
							double sma50Slope = sma50 - sma50Prev;
							
							// Trend alignment check
							bool uptrend = sma20 > sma50 && sma50 > sma100;
							bool downtrend = sma20 < sma50 && sma50 < sma100;
							
							// Direction momentum
							bool bullishMomentum = sma20Slope > 0 && sma50Slope > 0;
							bool bearishMomentum = sma20Slope < 0 && sma50Slope < 0;
							
							// Verify signal aligns with SMA trend
							bool smaVerificationPassed = true;
							
							if (traditionalSignal.newSignal == FunctionResponses.EnterLong)
							{
								// For long entries, require uptrend or bullish momentum
								smaVerificationPassed = uptrend || bullishMomentum;
								
								if (!smaVerificationPassed)
								{
									Print($"[SMA VERIFY] LONG signal rejected - SMA20: {sma20:F2} (slope: {sma20Slope:F2}), SMA50: {sma50:F2} (slope: {sma50Slope:F2}), SMA100: {sma100:F2}");
									Print($"[SMA VERIFY] Uptrend: {uptrend}, Bullish Momentum: {bullishMomentum}");
									patternFunctionResponse noSignal = new patternFunctionResponse();
									noSignal.newSignal = FunctionResponses.NoAction;
									noSignal.patternSubType = "sma_filter_rejected";
									return noSignal;
								}
								else
								{
									Print($"[SMA VERIFY] LONG signal confirmed - Uptrend: {uptrend}, Momentum: {bullishMomentum}");
									lastSignalBar = CurrentBars[0];
									lastSignalDirection = currentDirection;
									return traditionalSignal;
								}
								
							}
							else if (traditionalSignal.newSignal == FunctionResponses.EnterShort)
							{
								// For short entries, require downtrend or bearish momentum
								smaVerificationPassed = downtrend || bearishMomentum;
								
								if (!smaVerificationPassed)
								{
									Print($"[SMA VERIFY] SHORT signal rejected - SMA20: {sma20:F2} (slope: {sma20Slope:F2}), SMA50: {sma50:F2} (slope: {sma50Slope:F2}), SMA100: {sma100:F2}");
									Print($"[SMA VERIFY] Downtrend: {downtrend}, Bearish Momentum: {bearishMomentum}");
									patternFunctionResponse noSignal = new patternFunctionResponse();
									noSignal.newSignal = FunctionResponses.NoAction;
									noSignal.patternSubType = "sma_filter_rejected";
									return noSignal;
								}
								else
								{
									Print($"[SMA VERIFY] SHORT signal confirmed - Downtrend: {downtrend}, Momentum: {bearishMomentum}");
									lastSignalBar = CurrentBars[0];
									lastSignalDirection = currentDirection;
									return traditionalSignal;
								}
							}
							else
							{
								// Signal is neither EnterLong nor EnterShort (e.g., NoAction or other)
								return traditionalSignal;
							}
						}
					else
					{
						// Not enough bars for SMA calculation
						Print($"[SMA VERIFY] Skipped - insufficient bars ({CurrentBar} < {smaPeriod3})");
						// Still track the signal if it's valid
						if (traditionalSignal.newSignal == FunctionResponses.EnterLong || traditionalSignal.newSignal == FunctionResponses.EnterShort)
						{
							lastSignalBar = CurrentBars[0];
							lastSignalDirection = traditionalSignal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
						}
						return traditionalSignal;
					}
					
				}
				else
				{
					// noaction
				
					return traditionalSignal;
				}
				

		}
		
		protected virtual FunctionResponses exitFunctionResponse()
		{
		
			return FunctionResponses.NoAction;
								
		}
		
		
		
		
		
		
	
		
		protected int getAllcustomPositionsCombined()
		{
		    /// Initialize total quantity to zero
		    int totalAccountQuantity = 0;
			if(IsUnmanaged == false)
			{
			    /// Check if there are any positions in the account
			    if (PositionsAccount.Count() > 0)
			    {
			        /// Iterate through all positions in the account
			        foreach (Position position in PositionsAccount)
			        {
			            // Add the quantity of each position to the total
			            totalAccountQuantity += position.Quantity;
			        }
			    }
			
			    /// Check if total quantity exceeds the strategy max quantity
			    if (totalAccountQuantity >= strategyMaxQuantity)
			    {
			        foreach (var position in PositionsAccount)
			        {
			            Print($"Instrument: {position.Instrument.FullName}, Quantity: {position.Quantity}");
			        }
			        Print($"PositionsAccount.Count() = {PositionsAccount.Count()} and totalAccountQuantity = {totalAccountQuantity}!");
			    }
			}
			else if(IsUnmanaged == true)
			{
				//Print($"getAllcustomPositionsCombined : {GetPositionCountByIndex(0)} + {GetPositionCountByIndex(1)}");
				totalAccountQuantity = GetPositionCountByIndex(0) + GetPositionCountByIndex(1);
			}
		
		    /// Return the total combined quantity
		    return totalAccountQuantity;
		}

		
		protected virtual signalPackage getVanillaSignalPackage(signalPackage fallback)
		{
			signalPackage noActionSignalPackage = new signalPackage
			{
				SignalReturnAction = new signalReturnAction(signalReturnActionEnum.noAction.ToString(), signalReturnActionType.Neutral), // Default sentiment
		
			};
			
			return noActionSignalPackage;
		}
		
		
		// Update the array with the latest value
		void UpdateCumProfit(double newCumProfit)
		{
		    // Shift values left and add the new one at the end
		    for (int i = 0; i < cumProfitArray.Length - 1; i++)
		        cumProfitArray[i] = cumProfitArray[i + 1];
		    cumProfitArray[cumProfitArray.Length - 1] = newCumProfit;
		
		    // Increment the counter until it stabilizes at 3
		    if (valueCounter < 3) valueCounter++;
		}
		
		
		private string GetContractSymbol(string baseInstrument, DateTime date)
		{
		    // Only quarterly months H,M,U,Z
		    var quarterlyMonths = new Dictionary<int, char>
		    {
		        { 3, 'H' },  // March
		        { 6, 'M' },  // June
		        { 9, 'U' },  // September
		        { 12, 'Z' }  // December
		    };
		
		    int month = date.Month;
		    int year = date.Year % 100;  // Get last two digits
		
		    // If within 3 months of quarterly expiry, use that contract
		    foreach (var kvp in quarterlyMonths.OrderBy(x => x.Key))
		    {
		        if (kvp.Key - month >= -3 && kvp.Key - month <= 3)
		            return $"{baseInstrument}{kvp.Value}{year % 10}";
		    }
		
		    // Past September, use next year's March
		    if (month >= 9)
		        return $"{baseInstrument}H{(year + 1) % 10}";
		
		    // Use next quarterly
		    foreach (var kvp in quarterlyMonths)
		        if (kvp.Key > month)
		            return $"{baseInstrument}{kvp.Value}{year % 10}";
		
		    return $"{baseInstrument}H{(year + 1) % 10}";
		}
		
		// RF FILTERING METHODS
	
		
		private Dictionary<string, double> CollectCurrentFeatures()
		{
			var features = new Dictionary<string, double>();
			
			try
			{
				// Market data features
				features["close"] = Close[0];
				features["high"] = High[0];
				features["low"] = Low[0];
				features["open"] = Open[0];
				features["volume"] = Volume[0];
				
				// Technical indicators - check if they exist before accessing
				if (EMA(9) != null && EMA(9).Count > 0)
					features["ema_9"] = EMA(9)[0];
				if (EMA(21) != null && EMA(21).Count > 0)
					features["ema_21"] = EMA(21)[0];
				if (EMA(50) != null && EMA(50).Count > 0)
					features["ema_50"] = EMA(50)[0];
				
				if (RSI(14, 3) != null && RSI(14, 3).Count > 0)
					features["rsi_14"] = RSI(14, 3)[0];
				
				if (ATR(14) != null && ATR(14).Count > 0)
					features["atr_14"] = ATR(14)[0];
				
				if (Bollinger(2, 20) != null && Bollinger(2, 20).Count > 0)
				{
					features["bb_upper"] = Bollinger(2, 20).Upper[0];
					features["bb_middle"] = Bollinger(2, 20).Middle[0];
					features["bb_lower"] = Bollinger(2, 20).Lower[0];
				}
				
				// Volume indicators
				if (SMA(Volume, 20) != null && SMA(Volume, 20).Count > 0)
					features["volume_sma_20"] = SMA(Volume, 20)[0];
				
				// Market condition features
				features["hour_of_day"] = Time[0].Hour;
				features["day_of_week"] = (double)Time[0].DayOfWeek;
				
				// Position and account info
				features["position_quantity"] = Position.Quantity;
				features["unrealized_pnl"] = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
				
				// Price range features
				if (Bars.Count > 1)
				{
					features["price_change"] = Close[0] - Close[1];
					features["price_change_pct"] = (Close[0] - Close[1]) / Close[1];
				}
				
				// Add range values if available
				features["price_range_0"] = GetPriceRangeValue0;
				features["price_range_1"] = GetPriceRangeValue1;
				features["price_range_2"] = GetPriceRangeValue2;
				features["price_range_3"] = GetPriceRangeValue3;
			}
			catch (Exception ex)
			{
				Print($"[RF] Error collecting features: {ex.Message}");
			}
			
			return features;
		}
		
		private void LogFilteredSignal(patternFunctionResponse signal, RFFilterResult filterResult)
		{
			try
			{
				if (trainingDataClient != null)
				{
					var filteredSignalLog = new
					{
						timestamp = DateTime.Now,
						signal_type = signal.signalType,
						signal_definition = signal.signalDefinition,
						pattern_id = signal.patternId,
						pattern_subtype = signal.patternSubType,
						signal_score = signal.signalScore,
						rf_confidence = filterResult.confidence,
						rf_decision = filterResult.shouldTake,
						rf_reason = filterResult.reason,
						model_used = filterResult.modelUsed,
						instrument = Instrument.FullName,
						entry_price = Close[0],
						features = signal.signalFeatures
					};
					
					string logData = JsonConvert.SerializeObject(filteredSignalLog, Formatting.Indented);
					trainingDataClient.SendFilteredSignal(filteredSignalLog);
				}
			}
			catch (Exception ex)
			{
				Print($"[RF] Error logging filtered signal: {ex.Message}");
			}
		}
		
		// REMOVED: Heavy CollectPositionOutcome method - replaced with lightweight outcome data sent to ME service
		
		public void updateRangeValues()
		{
			// Calculate ATR values and update static states
		    GetPriceRangeValue0 = GetPriceRange(1);
			GetPriceRangeValue1 = GetPriceRange(10);
		    GetPriceRangeValue2 = GetPriceRange(50);
		    GetPriceRangeValue3 = GetPriceRange(100);
			//Print($"updateRangeValues {strategyDefaultQuantity}, {GetPriceRangeValue0} {GetPriceRangeValue1} {GetPriceRangeValue2} {GetPriceRangeValue3}");
		    myStrategyControlPane.updateStaticStates(GetPriceRangeValue0,GetPriceRangeValue1,GetPriceRangeValue2,GetPriceRangeValue3);
		}
		
		public void toggleSignalsOnly()
		{
			signalsOnly = signalsOnly == true ? false : true;
			
		}
		public void clearDrawings()
		{
				
			 RemoveDrawObjects();
	
		}
		
		// Load and send default matching configuration to MatchingEngine
		protected virtual void LoadMEConfig()
		{
			try
			{
				if (curvesService == null)
				{
					Print("[CONFIG] CurvesV2Service not available - skipping config load");
					return;
				}
				
				// Create default configuration
				var defaultConfig = new MatchingConfig
				{
					ZScoreThreshold = 0.5,              // Allow patterns 0.5 std dev better than average
					ReliabilityPenaltyEnabled = true,   // Enable reliability penalties
					MaxThresholdPenalty = 0.1,          // Moderate penalty for unreliable patterns
					AtmosphericThreshold = 0.8,         // Pre-filtering threshold
					CosineSimilarityThresholds = new CosineSimilarityThresholds
					{
						DefaultThreshold = 0.70,        // Relaxed default threshold
						EmaRibbon = 0.75,               // Slightly higher for EMA patterns
						SensitiveEmaRibbon = 0.78       // Highest for sensitive patterns
					},
					// NEW: Risk Management Configuration
					RiskManagement = new RiskManagementConfig
					{
						MaxTolerance = 100.0,           // $100 max stop loss
						DefaultStopPct = 0.60,          // 60% of max tolerance default
						DefaultPullbackPct = 0.20,      // 20% pullback default
						PatternPreferences = new Dictionary<string, PatternRiskConfig>
						{
							// Mean Reversion patterns - tighter stops, quicker exits
							["MGC_MeanReversion"] = new PatternRiskConfig { StopPct = 0.45, PullbackPct = 0.15, ConfidenceScaling = true },
							["gc_range_reversion_strong"] = new PatternRiskConfig { StopPct = 0.50, PullbackPct = 0.18, ConfidenceScaling = true },
							["mean_reversion_oversold"] = new PatternRiskConfig { StopPct = 0.45, PullbackPct = 0.15, ConfidenceScaling = true },
							
							// Trend Continuation patterns - wider stops, patient exits  
							["gc_trend_continuation_strong"] = new PatternRiskConfig { StopPct = 0.75, PullbackPct = 0.25, ConfidenceScaling = true },
							["trend_continuation_momentum"] = new PatternRiskConfig { StopPct = 0.70, PullbackPct = 0.22, ConfidenceScaling = true },
							["EMARibbonHarmonics"] = new PatternRiskConfig { StopPct = 0.65, PullbackPct = 0.20, ConfidenceScaling = true },
							
							// IBI Composite patterns - adaptive approach
							["ibi_composite_bullish"] = new PatternRiskConfig { StopPct = 0.60, PullbackPct = 0.20, ConfidenceScaling = true },
							["ibi_composite_bearish"] = new PatternRiskConfig { StopPct = 0.60, PullbackPct = 0.20, ConfidenceScaling = true },
							["IBI_Confluence"] = new PatternRiskConfig { StopPct = 0.60, PullbackPct = 0.20, ConfidenceScaling = true },
							
							// MGC Specific patterns (EXACT NAMES from your logs)
							["MGC_Pullback"] = new PatternRiskConfig { StopPct = 0.55, PullbackPct = 0.18, ConfidenceScaling = true },
							
							// Momentum patterns - balanced approach
							["momentum_divergence"] = new PatternRiskConfig { StopPct = 0.65, PullbackPct = 0.20, ConfidenceScaling = true },
							["volatility_contraction"] = new PatternRiskConfig { StopPct = 0.55, PullbackPct = 0.18, ConfidenceScaling = true },
							
							// Fuzzy Shapes - slightly more conservative
							["FuzzyShape"] = new PatternRiskConfig { StopPct = 0.65, PullbackPct = 0.22, ConfidenceScaling = true }
						},
						ScalingFactors = new ScalingFactors
						{
							HighConfidenceBoost = 1.1,      // 10% looser stops for high confidence (>0.8)
							LowConfidencePenalty = 0.8,     // 20% tighter stops for low confidence (<0.6)
							HighConfluenceBoost = 1.2,      // 20% more patient exits for high confluence (>0.8)
							LowConfluencePenalty = 0.8      // 20% quicker exits for low confluence (<0.6)
						}
					}
				};
				
				// Get instrument code
				string instrumentCode = GetInstrumentCode();
				
				// Send configuration asynchronously - fire and forget
				Task.Run(async () => {
					try {
						bool configSent = await curvesService.SendMatchingConfigAsync(instrumentCode, defaultConfig,UseRemoteServiceParameter);
						if (configSent)
						{
							Print($"[CONFIG] Default matching configuration sent successfully for {instrumentCode}");
						}
						else
						{
							Print($"[CONFIG] Failed to send matching configuration for {instrumentCode}");
						}
					}
					catch (Exception ex) {
						Print($"[CONFIG] Error sending configuration: {ex.Message}");
					}
				});
			}
			catch (Exception ex)
			{
				Print($"[CONFIG] Error in LoadMEConfig: {ex.Message}");
			}
		}
		
		// Helper method to get instrument code (can be overridden in derived classes)
		protected virtual string GetInstrumentCode()
		{
			string instrumentCode = Instrument?.FullName?.Split(' ')?.FirstOrDefault() ?? "";
			if (string.IsNullOrEmpty(instrumentCode))
			{
				Print("[CONFIG] Warning: Unable to determine instrument code - using fallback");
				instrumentCode = "UNKNOWN";
			}
			return instrumentCode;
		}

		/// <summary>
		/// Collect all session configuration parameters for risk management tracking
		/// </summary>
		protected virtual Dictionary<string, object> CollectSessionConfig()
		{
			var config = new Dictionary<string, object>
			{
				// Core risk management parameters - convert enum and object to strings
				["ExecutionMode"] = strategyExecutionMode.ToString(),
				["Time_Period"] = BarsArray[0].BarsPeriod.BarsPeriodType.ToString(),
				["Time_Value"] = BarsArray[0].BarsPeriod.Value,
				["microContractStoploss"] = microContractStoploss,
				["microContractTakeProfit"] = microContractTakeProfit,
				["softTakeProfitMult"] = softTakeProfitMult,
				["dailyProfitMaxLoss"] = dailyProfitMaxLoss,
				["dailyProfitGoalParameter"] = dailyProfitGoalParameter,
				["PullBackExitEnabled"] = PullBackExitEnabled,
				["TakeBigProfitEnabled"] = TakeBigProfitEnabled,
				["entriesPerDirectionSpacingTime"] = entriesPerDirectionSpacingTime,
			
				// Additional strategy parameters that could affect risk
				["strategyDefaultQuantity"] = strategyDefaultQuantity,
				["strategyMaxQuantity"] = strategyMaxQuantity,
				["accountMaxQuantity"] = accountMaxQuantity,
				
				// Session metadata
				["instrument"] = GetInstrumentCode(),
				["strategyName"] = Name,
				["specificStrategyName"] = SpecificStrategyName,
				["isLiveTrading"] = !IsInStrategyAnalyzer,
				["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
			};
			
			return config;
		}

		/// <summary>
		/// Send session configuration to cloud relay for analysis tracking
		/// </summary>
		protected virtual async Task SendSessionConfigToRelay()
		{
			try
			{
				if (dropletService == null || string.IsNullOrEmpty(SessionID))
				{
					Print("[SESSION-CONFIG] DropletService not available or SessionID not set - skipping config send");
					return;
				}

				var config = CollectSessionConfig();
				bool success = await dropletService.SendSessionConfig(SessionID, config);
				
				if (success)
				{
					Print($"[SESSION-CONFIG] ✓ Session configuration sent for {SessionID}");
				}
				else
				{
					Print($"[SESSION-CONFIG] ✗ Failed to send session configuration for {SessionID}");
				}
			}
			catch (Exception ex)
			{
				Print($"[SESSION-CONFIG] Error sending session config: {ex.Message}");
			}
		}
		
		// Signal persistence validation - check if last N signals are all same direction
		public bool ValidateSignalPersistence(double currentScore, out string direction)
		{
			direction = "";
			
			if (Math.Abs(currentScore) < 0.1) return false; // Need meaningful signal
			
			// Update current bar index and store current signal
			currentBarIndex = CurrentBars[0];
			signalHistory[currentBarIndex] = currentScore;
			
			// Clean up old signal history (keep only last 50 bars to prevent memory leaks)
			var keysToRemove = signalHistory.Keys.Where(k => k < currentBarIndex - 50).ToList();
			foreach (var key in keysToRemove)
			{
				signalHistory.Remove(key);
			}
			
			// Get the last SIGNAL_CONFIRMATION_BARS signals including current
			var recentSignals = new List<double>();
			for (int i = 0; i < SIGNAL_CONFIRMATION_BARS; i++)
			{
				int barIndex = currentBarIndex - i;
				if (signalHistory.ContainsKey(barIndex))
				{
					recentSignals.Add(signalHistory[barIndex]);
				}
			}
			
			// Relaxed requirement: Need at least 2 signals instead of full SIGNAL_CONFIRMATION_BARS
			if (recentSignals.Count < Math.Min(2, SIGNAL_CONFIRMATION_BARS))
			{
				//Print($"[PERSISTENCE] Not enough signals: {recentSignals.Count}/{SIGNAL_CONFIRMATION_BARS}");
				return false;
			}
			
			// Check for directional bias with relaxed threshold
			int bullCount = recentSignals.Count(s => s > 0.1); // Positive signals above noise
			int bearCount = recentSignals.Count(s => s < -0.1); // Negative signals below noise
			double totalSignals = recentSignals.Count;
			
			// More lenient threshold: 51% instead of 67%
			const double BIAS_THRESHOLD = 0.51;
			double bullBias = bullCount / totalSignals;
			double bearBias = bearCount / totalSignals;
			
			// Also check signal strength - average of recent signals
			double avgSignalStrength = recentSignals.Average();
			
			if (bullBias >= BIAS_THRESHOLD && avgSignalStrength > 0.2)
			{
				direction = "BULL";
				//Print($"[PERSISTENCE] ✅ BULL BIAS: {bullCount}/{recentSignals.Count} signals ({bullBias:P0}) avg={avgSignalStrength:F2}");
				return true;
			}
			else if (bearBias >= BIAS_THRESHOLD && avgSignalStrength < -0.2)
			{
				direction = "BEAR";
				//Print($"[PERSISTENCE] ✅ BEAR BIAS: {bearCount}/{recentSignals.Count} signals ({bearBias:P0}) avg={avgSignalStrength:F2}");
				return true;
			}
			else
			{
				//Print($"[PERSISTENCE] ❌ NO CLEAR BIAS: Bull {bullCount}/{recentSignals.Count}, Bear {bearCount}/{recentSignals.Count}, avg={avgSignalStrength:F2}");
				return false;
			}
		}
			
	

///Properties	

		#region Properties
		
		[NinjaScriptProperty]    
		[Display(Name="Use CurvesService Synchronous Processing", Order=1, GroupName="Class Parameters")]
		public bool UseDirectSync { get; set; }

		
		
		
	
		
		[NinjaScriptProperty]
		[Display(Name="RawScore Requirement", Order=5, GroupName="Class Parameters")]
		public double OutlierScoreRequirement
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="effectiveScore Requirement", Order=5, GroupName="Class Parameters")]
		public double effectiveScoreRequirement
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Divergence Threshold", Order=5, GroupName="Class Parameters")]
		public double DivergenceThreshold
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Signals Only", Description="", Order=6, GroupName="Class Parameters")]
		public bool signalsOnly 
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "SL Micro", Order=7, GroupName = "Class Parameters")]
		public double microContractStoploss
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "TP Micro", Order=8, GroupName = "Class Parameters")]
		public double microContractTakeProfit
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Soft Take Profit $", Order=9, GroupName = "Class Parameters")]
		public double softTakeProfitMult
		{ get; set; }
		
		[NinjaScriptProperty]    
		[Display(Name="entry mechanism", Order=0, GroupName="Entry Parameters")]
		public entryMechanic EntryMechanism
		{ get; set; }
		
		[NinjaScriptProperty]    
		[Display(Name="Bar Zone Imblance", Order=1, GroupName="Entry Parameters")]
		public bool BarZoneImbalance
		{ get; set; }
		
	
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="pullbackPct ", Order=8, GroupName="Entry Parameters")]
		public double pullBackPct
		{ get; set; }
	
		[NinjaScriptProperty]
		[Range(0, 0.1)]
		[Display(Name = "perOrderMaxAccountRisk(%)", Order = 2, GroupName = "Entry Parameters")]
		public double perOrderMaxAccountRisk
		{ get; set; }
		
		
		
		[NinjaScriptProperty]
		[Range(double.MinValue, double.MaxValue)]
		[Display(Name="Max Loss to get tight on losses", Order=11, GroupName="Class Parameters")]
		public double dailyProfitMaxLoss
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1,double.MaxValue)]
		[Display(Name="Profit Goal for session", Order=12, GroupName="Class Parameters")]
		public double dailyProfitGoalParameter
		{ get; set; }
		
		
		[NinjaScriptProperty]
		[Display(Name="PullBackExitEnable", Description="Turn on to DebugDraw debug messages", Order=18, GroupName="Class Parameters")]
		public bool PullBackExitEnabled 
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="TakeBigProfitEnabled", Description="Turn on to DebugDraw debug messages", Order=19, GroupName="Class Parameters")]
		public bool TakeBigProfitEnabled 
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(0,int.MaxValue)]
		[Display(Name="entriesPerDirectionSpacing(Time)", Order=20, GroupName="Class Parameters")]
		public int entriesPerDirectionSpacingTime
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Override Strategy Config", Description="If true, use NT user inputs for SL/TP instead of config file", Order=21, GroupName="Class Parameters")]
		public bool OverrideStrategyConfig
		{ get; set; }

		
		[NinjaScriptProperty]
		[Display(Name="Selected Broker", Order=0, GroupName="Broker Settings")]
		public brokerSelection selectedBroker
		{ get; set; }
		
		// ProjectX Configuration Properties
		[NinjaScriptProperty]
		[Display(Name="ProjectX API Key", Order=1, GroupName="Broker Settings")]
		public string ProjectXApiKey
		{ get; set; } = "XkOumk2pucUSbjyf6YIrOgfv5XnR4SiXMnNmNo6XWdI=";

		[NinjaScriptProperty]
		[Display(Name="ProjectX Username", Order=2, GroupName="Broker Settings")]
		public string ProjectXUsername
		{ get; set; } = "BLU_USER_511Y75_P";

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="ProjectX Account ID", Order=3, GroupName="Broker Settings")]
		public int ProjectXAccountId
		{ get; set; } = 13417; // Verified account ID for LAUPGKBZYWQK0

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="ema3_val", Order=9, GroupName="EMA")]
		public int ema3_val
		{ get; set; } 
	

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="VWAP1", Order=13, GroupName="EMA")]
		public int vwap1
		{ get; set; } 

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="commonPeriod_smooth", Order=14, GroupName="EMA")]
		public int commonPeriod_smooth
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="VWAPScalingFactor", Order=23, GroupName="EMA")]
		public double VWAPScalingFactor
		{ get; set; } 
	
	
	

		[NinjaScriptProperty]
		[Display(Name="Main Entry Order Type", Description="Evaluate for ReEntry", Order=3, GroupName="Entry Behaviors")]
		public OrderType mainEntryOrderType 
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="market Positions Allowed", Description="", Order=9, GroupName="Signal Parameters")]
		public marketPositionsAllowed marketPosAllowed
		{ get; set; }
	
		
		[NinjaScriptProperty]
		[Display(Name="Debug (Print)", Description="Turn on to DebugPrint debug messages", Order=0, GroupName="Debug")]
		public bool DebugModeParameter 
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Debug (Draw)", Description="Turn on to DebugDraw debug messages", Order=2, GroupName="Debug")]
		public bool debugModeDraw 
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Filter Debug mode", Description="Filter DebugPrint debug messages", Order=3, GroupName="Debug")]
		public debugSection DebugFilter 
		{ get; set; }
		
	
	
	
		
		[NinjaScriptProperty]
		[Display(Name="Enable Profit Colors", Description="Turn on to use backtest conditions", Order=4, GroupName="Debug")]
		public double enableProfitColors 
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Enable Loss Colors", Description="Turn on to use backtest conditions", Order=5, GroupName="Debug")]
		public double enableLossColors 
		{ get; set; }
	
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="strategyDefaultQuantity", Order=15, GroupName="Strategy Level Params")]
		public int strategyDefaultQuantity
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="strategyMaxQuantity", Order=16, GroupName="Strategy Level Params")]
		public int strategyMaxQuantity
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="accountMaxQuantity", Order=17, GroupName="Strategy Level Params")]
		public int accountMaxQuantity
		{ get; set; }

		
	

		[NinjaScriptProperty]
		[Range(0,int.MaxValue)]
		[Display(Name="entriesPerDirectionSpacing", Order=18, GroupName="Strategy Level Params")]
		public int entriesPerDirectionSpacing
		{ get; set; }
		
		

		[NinjaScriptProperty]
		[Range(0.0, 1.0)]
		[Display(Name="Risk Agent Confidence Threshold", Description="Minimum confidence for RISK model to accept signal", Order=4, GroupName="Risk Agent Config")]
		public double RiskAgentConfidenceThreshold { get; set; } = 0.5;
	
		
		// Legacy: [NinjaScriptProperty]
		// Legacy: [Display(Name="Traditional Strategy Filter", Description="Test specific traditional strategy type for pure training data", Order=6, GroupName="Risk Agent Config")]
		// Legacy: public TraditionalStrategyType TraditionalStrategyFilter { get; set; } = TraditionalStrategyType.ALL;
		
		[NinjaScriptProperty]
		[Display(Name="Improved Strategy Filter", Description="Filter strategies by category or execution type", Order=6, GroupName="Risk Agent Config")]
		public ImprovedStrategyFilter StrategyFilter { get; set; } = ImprovedStrategyFilter.ALL;
		
		[NinjaScriptProperty]
		[Display(Name="Specific Strategy Name", Description="Strategy name when filter is SPECIFIC_STRATEGY", Order=7, GroupName="Risk Agent Config")]
		public string SpecificStrategyName { get; set; } = "PUMPKIN";
		protected StrategyConfig currentConfig;
		
		
		
		[NinjaScriptProperty]
		[Display(Name="Do Not Store (Out-of-Sample)", Description="Skip storing positions to Agentic Memory for out-of-sample testing", Order=9, GroupName="Risk Agent Config")]
		public bool DoNotStore { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name="Store As Recent (Live Training)", Description="Store positions as RECENT data for live graduation learning", Order=10, GroupName="Risk Agent Config")]
		public bool StoreAsRecent { get; set; } = false;

		// COMMENTED OUT: SignalFeatures Risk Agent Configuration Properties
		// [NinjaScriptProperty]
		// [Range(3000, 9999)]
		// [Display(Name="Risk Agent Port", Description="Risk Agent service port (default: 3017)", Order=11, GroupName="Risk Agent Config")]
		// public int RiskAgentPort { get; set; } = 3017;

		// [NinjaScriptProperty]
		// [Display(Name="Enable Anti-Overfitting", Description="Enable anti-overfitting protection for pattern analysis", Order=12, GroupName="Risk Agent Config")]
		// public bool EnableAntiOverfitting { get; set; } = true;

		// [NinjaScriptProperty]
		// [Range(0.5, 0.99)]
		// [Display(Name="Diminishing Factor", Description="Confidence reduction factor per pattern exposure (0.5=strong penalty, 0.99=light penalty)", Order=13, GroupName="Risk Agent Config")]
		// public double DiminishingFactor { get; set; } = 0.8;

		// [NinjaScriptProperty]
		// [Range(1, 20)]
		// [Display(Name="Max Pattern Exposure", Description="Maximum times a pattern can be used before heavy penalty", Order=14, GroupName="Risk Agent Config")]
		// public int MaxPatternExposure { get; set; } = 5;

		// [NinjaScriptProperty]
		// [Range(15, 240)]
		// [Display(Name="Time Window Minutes", Description="Time window for pattern clustering detection (minutes)", Order=15, GroupName="Risk Agent Config")]
		// public int TimeWindowMinutes { get; set; } = 60;

		// [NinjaScriptProperty]
		// [Display(Name="Backtest Mode", Description="Current backtest mode for anti-overfitting", Order=16, GroupName="Risk Agent Config")]
		// public BacktestModes BacktestMode { get; set; } = BacktestModes.LiveTrading;

		// [NinjaScriptProperty]
		// [Display(Name="Reset Learning on Backtest", Description="Reset pattern exposure when starting new backtests", Order=17, GroupName="Risk Agent Config")]
		// public bool ResetLearningOnBacktest { get; set; } = true;

		[NinjaScriptProperty]
		[Range(500, 10000)]
		[Display(Name="Risk Agent Timeout (ms)", Description="Timeout for Risk Agent requests in milliseconds", Order=18, GroupName="Risk Agent Config")]
		public int RiskAgentTimeoutMs { get; set; } = 2000;
		
		#endregion
		
		#region Trade Correlation Analysis Helper Methods
		
		/// <summary>
		/// Get the count of trades executed today
		/// </summary>
		/// <returns>Daily trade count</returns>
		public int GetDailyTradeCount()
		{
			// Reset counter on new trading day
			if (Time[0].Date != lastTradeDate.Date)
			{
				dailyTradeCounter = 0;
				lastTradeDate = Time[0].Date;
			}
			
			return dailyTradeCounter;
		}
		
		/// <summary>
		/// Get the result of the previous trade
		/// </summary>
		/// <returns>WIN, LOSS, BE (break-even), or NONE</returns>
		public string GetLastTradeResult()
		{
			return lastTradeOutcome;
		}
		
		/// <summary>
		/// Update trade outcome tracking for correlation analysis
		/// </summary>
		/// <param name="pnl">Trade P&L</param>
		public void UpdateTradeOutcomeTracking(double pnl)
		{
			// Increment daily trade counter
			dailyTradeCounter++;
			
			// Update last trade result
			if (pnl > 5.0) // Small buffer for break-even
				lastTradeOutcome = "WIN";
			else if (pnl < -5.0)
				lastTradeOutcome = "LOSS";
			else
				lastTradeOutcome = "BE";
				
			lastTradeDate = Time[0].Date;
		}
		
	
		
	#endregion
	
	}
	
	
}






