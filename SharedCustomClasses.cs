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
using System.Reflection;
using System.Drawing;
using System.IO;

#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	
		
		
		public enum entryMechanic
		{
			BarZoneImbalance,
			CumulativeZoneImbalance
		}
		
		public class globalThompsonValue
		{
			public double value {get;set; }
			public double Alpha { get; set; } = 1.0; // Add Alpha and Beta for Thompson Sampling
		    public double Beta { get; set; } = 1.0; // Add Alpha and Beta for Thompson Sampling
		}
	
		public class vwapStop
		{
			
			public custom_VWAP VWAPValue { get; set; }
			public EMA EMAValue { get; set; }
			public Bollinger BBValue { get; set; }
			public string exitOrderUUID  { get; set; }
			public string entryOrderUUID  { get; set; }
			public bool isLong  { get; set; }
				
		}
		public class OrderFlowPattern
		{
		
			public double[] signalVector { get; set; }
		    public signalReturnActionType OFPrediction { get; set; }
		    public double AvgProfit { get; set; }
		    public double CompositeScore { get; set; }
		    public double lastPrice { get; set; }
		    public double lastBar { get; set; }
		    public DateTime lastTimeObserved { get; set; }
		    public double predictionScore { get; set; }
		    public int observations { get; set; }
		    public string signalID { get; set; }
		    public int penalty { get; set; }
		    public double trackedProfitability { get; set; }
			public double trackedProfitabilityBullish { get; set; }
			public double trackedProfitabilityBearish { get; set; }
		    public double distanceMatched { get; set; }
		    public double trackedDistance { get; set; }
		    public int selections { get; set; }
		    public bool blocked { get; set; }
		    public int createdBarsInProgress { get; set; }
		    public string debugVectorString { get; set; }
		    public PatternType patternTypeClassification { get; set; }
			public double decayValue { get; set; }
			
		 
		    // Add methods to interact with the SOM as needed
		   
		}
	
		public class PatternTypeSubclassOF
		{
			public PatternType PatternTypeName { get; set; } = PatternType.PatternTypeSubclassOF;
		    public double BarDelta  { get; set; }
		    public double CumulativeDelta { get; set; }
		   	public double DeltaPercent { get; set; }
		    public double DistanceFromVWAP { get; set; }
		    // Add other pattern types as needed
		}
		public class PatternTypeSubclassTA
		{
			public PatternType PatternTypeName { get; set; } = PatternType.PatternTypeSubclassTA;
		    public double HourOfDay  { get; set; }
		    public double Volume { get; set; }
		   	public double CloseDiff { get; set; }
		   
		}
	
		public enum PatternType
	    {
	        PatternTypeSubclassOF,
	        PatternTypeSubclassTA,
	        // Add other pattern types as needed
	    }
		[Serializable]
		public class OrderFlowPatternTrail
		{
		    // Enum to define different pattern types
		    public PatternType PatternTypeName { get; set; }
		
		    public List<PatternValue> Values { get; set; } = new List<PatternValue>();

		    // Dictionary to store generic pattern objects
			[XmlIgnore]
		    public Dictionary<PatternType, object> Patterns { get; set; } = new Dictionary<PatternType, object>();
		
		    public OrderFlowPatternTrail() { }
		
		    // Get a pattern of a specific type
		    public T GetPattern<T>(PatternType patternType) where T : class
		    {
		        if (Patterns.TryGetValue(patternType, out var pattern))
		        {
		            return pattern as T;
		        }
		        return null;
		    }
			
			public class PatternValue
		    {
		        public PatternType PatternType { get; set; }
		        public double[] Values { get; set; }
		    }
		    // Add a pattern of a specific type
		     public void AddPattern<T>(T pattern) where T : class
		    {
		        var patternTypeProperty = pattern.GetType().GetProperty("PatternTypeName");
		        if (patternTypeProperty != null)
		        {
		            var patternType = (PatternType)patternTypeProperty.GetValue(pattern);
		            Patterns[patternType] = pattern;
		        }
		    }
		
		    // Retrieve the double values associated with a specific pattern type
		    public double[] GetPatternValues(PatternType patternType)
			{
			    if (Patterns.TryGetValue(patternType, out var pattern))
			    {
			        var doubleProperties = pattern.GetType()
			                                      .GetProperties()
			                                      .Where(p => p.PropertyType == typeof(double))
			                                      .Select(p => (double?)p.GetValue(pattern)) // Use double? to handle nulls
			                                      .Where(v => v.HasValue) // Filter out null values
			                                      .Select(v => v.Value) // Convert double? back to double
			                                      .ToArray();
			        return doubleProperties.Length > 0 ? doubleProperties : null;
			    }
			    return new double[0];
			}
		
		    // Add or update the values associated with a pattern type
		    // Add or update the values associated with a pattern type
		    public void AddOrUpdatePattern(PatternType pattern, double[] values)
		    {
		        var patternValue = Values.FirstOrDefault(p => p.PatternType == pattern);
		        if (patternValue != null)
		        {
		            patternValue.Values = values;
		        }
		        else
		        {
		            Values.Add(new PatternValue { PatternType = pattern, Values = values });
		        }
		    }
		
		    // Calculate distance between two patterns
		    public double CalculateDistance(OrderFlowPatternTrail pattern1, OrderFlowPatternTrail pattern2)
		    {
				double totalDistance = 0;  // Initialize totalDistance to 0
				
				// Debug print to check the keys
				//NinjaTrader.Code.Output.Process(("Keys in pattern1.Values: " + string.Join(", ", pattern1.GetPatternValues(pattern1.PatternTypeName))),PrintTo.OutputTab1);
				//NinjaTrader.Code.Output.Process(("Keys in pattern2.Values: " + string.Join(", ", pattern2.GetPatternValues(pattern2.PatternTypeName))),PrintTo.OutputTab1);
				
				// Retrieve the values for the given pattern types
			    double[] values1 = pattern1.GetPatternValues(pattern1.PatternTypeName);
			    double[] values2 = pattern2.GetPatternValues(pattern2.PatternTypeName);
			
			    // Check if both arrays are not null and have values
			    if (values1 != null && values2 != null)
			    {
			        int minLength = Math.Min(values1.Length, values2.Length);
			
			        // Calculate the distance between corresponding elements
			        for (int i = 0; i < minLength; i++)
			        {
			            totalDistance += Math.Pow(values1[i] - values2[i], 2);
			        }
			
			        // Return the Euclidean distance
			        return Math.Sqrt(totalDistance);
			    }
			    else
			    {
			        // Handle the case where one or both arrays are null or empty
			        NinjaTrader.Code.Output.Process("One or both pattern values arrays are null or empty.", PrintTo.OutputTab1);
			        return double.MaxValue; // Return a large value to indicate an issue
			    }

		    }
		
		    public override bool Equals(object obj)
		    {
		        if (obj is OrderFlowPatternTrail other)
		        {
		            return this.PatternTypeName == other.PatternTypeName &&
		                   this.Patterns.Count == other.Patterns.Count &&
		                   this.Patterns.All(kvp =>
		                   {
		                       if (other.Patterns.TryGetValue(kvp.Key, out var otherPattern))
		                       {
		                           var thisValues = GetPatternValues(kvp.Key);
		                           var otherValues = other.GetPatternValues(kvp.Key);
		                           return thisValues.SequenceEqual(otherValues);
		                       }
		                       return false;
		                   });
		        }
		        return false;
		    }
		
		    public override int GetHashCode()
		    {
		        int hash = 17;
		        hash = hash * 31 + PatternTypeName.GetHashCode();
		        foreach (var kvp in Patterns)
		        {
		            hash = hash * 31 + kvp.Key.GetHashCode();
		            var values = GetPatternValues(kvp.Key);
		            if (values != null)
		            {
		                hash = hash * 31 + values.Aggregate(0, (acc, val) => acc * 31 + val.GetHashCode());
		            }
		        }
		        return hash;
		    }
			
		}
		
		
		
		[Serializable]
		public class PatternValue
		{
		    [XmlAttribute("Type")]
		    public PatternType PatternType { get; set; }
		
		    [XmlAttribute("Values")]
		    public string Values { get; set; } // Store as comma-separated string
		
		    // You can add a helper property or method to convert `Values` back to an array of doubles
		   // [XmlIgnore]
		    public double[] ParsedValues
		    {
		        get => Values.Split(',').Select(double.Parse).ToArray();
		        set => Values = string.Join(",", value);
		    }
		}

		
		[Serializable]
		public class OrderFlowPatternTrailContainer
		{
		    public List<OrderFlowPatternTrail> Patterns { get; set; } = new List<OrderFlowPatternTrail>();
		}
		
		
		
		public enum brokerSelection
		{
			Apex,
			Topstep,
			NinjaTrader
		}
		public enum patternAction
		{
			Create,
			Find,
		}
		public enum macroTrend
		{
			Bullish,
			Bearish,
			Unknown
		}
		public enum optimizationPlans
		{
			FullOptimization,
			LinearRegression
		}
		public enum scoreRankAction
		{
			Promote,
			Demote,
			noActionToTake
		}
		public enum scoreType
		{
			realScore,
			virtualScore
		}
		
		public class SimpleProfiler
		{
		    private Dictionary<string, DateTime> startTimes = new Dictionary<string, DateTime>();
		    private Dictionary<string, double> totalDurations = new Dictionary<string, double>();
		    private Dictionary<string, int> executionCounts = new Dictionary<string, int>();
		
		    // Start timing a section of code
		    public void Start(string sectionName)
		    {
		        startTimes[sectionName] = DateTime.Now;
		    }
		
		    // Stop timing a section of code and log the duration
		    public void Stop(string sectionName)
		    {
		        if (startTimes.ContainsKey(sectionName))
		        {
		            TimeSpan duration = DateTime.Now - startTimes[sectionName];
		
		            if (totalDurations.ContainsKey(sectionName))
		            {
		                totalDurations[sectionName] += duration.TotalMilliseconds;
		                executionCounts[sectionName] += 1;
		            }
		            else
		            {
		                totalDurations[sectionName] = duration.TotalMilliseconds;
		                executionCounts[sectionName] = 1;
		            }
		
		            //NinjaTrader.Code.Output.Process($"{sectionName} took {duration.TotalMilliseconds} ms",PrintTo.OutputTab1);
		        }
		        else
		        {
		       
					//NinjaTrader.Code.Output.Process($"No start time recorded for section: {sectionName}",PrintTo.OutputTab1);
		        }
		    }
		
		    // Print the average recorded duration for all sections
		    public void Report()
		    {
				double durations = 0;
				double counts = 0;
		        foreach (var kvp in totalDurations)
		        {
		            string sectionName = kvp.Key;
		            durations += kvp.Value;
		            counts += executionCounts[sectionName];
		           
		
					
		          
		        }
				 double avgDuration = durations / counts;
				NinjaTrader.Code.Output.Process($"AvgDuration = "+avgDuration,PrintTo.OutputTab1);
		    }
		}

		public class LosingTrade
		{
			
		    public double ExitPrice { get; set; }
			public OrderAction EntryOrderAction { get; set; }
			public string EntryOrderUUID { get; set; }
		    public DateTime ExitTime { get; set; }
		    public bool Reversed { get; set; }
			public int exitBar { get; set; }
		}
		
		public class PerformanceTracker
		{
		    public double longProfit { get; set; }
		    public double shortProfit { get; set; }
		    public int longTrades { get; set; }
		    public int shortTrades { get; set; }
		}
				
		public enum regimeType
		{
			TrendFollowing,
			MeanReverting,
			Unknown
		}
		public enum WickType
		{
		 
			UpperWick,
			LowerWick
		}
		
		public enum entryType
		{
			Buy,
			SellShort,
			Unknown
		}
	
		
		public enum peakValleyState
		{
			peak,
			valley,
			unknown
		}
				
		
		public enum orderClass
		{
		 
			standard,
			scaleIn
		}
			
		public enum ExitOrderType
		{
		 
			ExitLongStopUnprofitable,
			ExitLongStopProfitable,
			ExitShortStopUnprofitable,
			ExitShortStopProfitable,
			ExitLongTakeProfits,
			ExitShortTakeProfits,
			ScaleIn,
			Cutloss,
			ASAP_MarginCheck,
			ASAP_Other,
			ASAP_Reverse,
			ASAP_Expired,
			ASAP_ExitSignal,
			ASAP_OutOfMoney,
			EOSC,
			StopLoss,
			InitialStopOut,
			profitRateChange,
			Manual,
			None
		}
		
		public enum patternSubtypes
		{
			All,
			Trending,
			Reversion,
			Breakout,
			Consolidation,
			
		}
		
		public enum signalExitAction
		{
			TBPL, // TakeBigProfitLong
			PBL, // TakePullBackProfitLong
			CARL, // CutAgeRiskLong
			TBPS, // TakeBigProfitShort
			PBS, // TakePullBackProfitShort
			CARS, // CutAgeRiskShort
			MLS, // MaxLossShort
			MLL, // MaxLossLong
			WDL, // WrongDirectionLong
			WDS, // WrongDirectionShort
			RDL, // ReversingDirectionLong
			RDS, // ReversingDirectionShort
			TSL, // TouchStopLong
			TSS, // TouchStopShort
			PSL, // PastStopLong
			PSS, // PastStopShort
			MS, // ManualShort
			ML, // ManualLong
			PE, // PeakExit
			VE, // ValleyExit
			FE_CAP, // daily cap
			FE_CAP2, // daily cap
			FE_CAP_ML, // daily cap
			FE_CAP_TP, // daily cap
			FE_CAP_TP_SAFE,
			FE_EOD, // margin check
			FE_EXIT,// exit signal
			FE_EOSC,
			FE_EXIT2,
			FE_EXIT3,
			DAY_MAXLOSS,
			CLOSE_STOP,
			MAN,
			FE2, // ForceExit
			NA, // notAvailable
			RSKL,//risk exit long
			RSKS,//risk exit short
			AGEL,//age exit long
			AGES,//age exit short
			CRSL,
			CRSS,
			VWAPL,
			VWAPS,
			STDDEVL,
			STDDEVS,
			FUNCL,
			FUNCS,
			IMBL,
			IMBS,
			NULL,
			STOPVWAP,
			subTypeMisMatch_L,
			subTypeMisMatch_S,
			DIV_L,
			DIV_S
		
		}
		

		
		


		[Serializable]
		public class signalReturnAction
		{
		    public string SignalName { get; set; } // The literal name, dynamic or enum to string
		    public signalReturnActionType Sentiment { get; set; } // Sentiment
		
		    public signalReturnAction(string signalName, signalReturnActionType sentiment)
		    {
		        SignalName = signalName;
		        Sentiment = sentiment;
		    }
		
		    public override string ToString()
		    {
		        return SignalName;
		    }
		}

		
		
		public class SignalNameComparer : IEqualityComparer<signalReturnAction>
		{
		    public bool Equals(signalReturnAction x, signalReturnAction y)
		    {
		        if (x == null || y == null) return false;
		        return x.SignalName == y.SignalName;
		    }
		
		    public int GetHashCode(signalReturnAction obj)
		    {
		        return obj.SignalName.GetHashCode();
		    }
		}
		
		public static class SignalManager
		{
		    public static List<signalReturnAction> GetStaticSignals()
		    {
		        return Enum.GetValues(typeof(signalReturnActionEnum))
		                   .Cast<signalReturnActionEnum>()
		                   .Select(e => new signalReturnAction(e.ToString(), signalReturnActionType.Neutral)) // Default to Neutral sentiment
		                   .ToList();
		    }
		
		    public static void AddDynamicSignal(List<signalReturnAction> signals, string name, signalReturnActionType sentiment)
		    {
		        var signal = new signalReturnAction(name, sentiment);
		        signals.Add(signal);
		    }
		}
	

		//[Serializable]
		public enum signalReturnActionEnum
		{
			patternLong,
			patternShort,
		 	enterLongManual,
			enterShortManual,
			enterSignalOnly,
			/// <summary>
			/// candlesticks
			/// </summary>
			BullishEngulfing, // Strong bullish
		    ThreeWhiteSoldiers, // Strong bullish
		    MorningStar, // Strong bullish
		    BullishBeltHold, // Moderately bullish
		    HammerCandle, // Moderately bullish
		    InvertedHammer, // Moderately bullish
		    BullishHarami, // Moderately bullish
		    BullishHaramiCross, // Moderately bullish
		    PiercingLine, // Moderately bullish
		    Doji, // Neutral
		    StickSandwich, // Neutral
		    RisingThreeMethods, // Neutral
		    UpsideTasukiGap, // Slightly bullish/Neutral
		    DownsideTasukiGap, // Slightly bearish/Neutral
		    DarkCloudCover, // Moderately bearish
		    HangingMan, // Moderately bearish
		    EveningStar, // Strong bearish
		    BearishHarami, // Moderately bearish
		    BearishHaramiCross, // Moderately bearish
		    BearishEngulfing, // Strong bearish
		    BearishBeltHold, // Strong bearish
		    ThreeBlackCrows, // Strong bearish
		    FallingThreeMethods, // Strong bearish
		    ShootingStar, // Moderately bearish
		    UpsideGapTwoCrows, // Moderately bearish
			standardBullish,
			standardBearish,
			RSIBullish,
			RSIBearish,
			RSIBullishFancy,
			RSIBearishFancy,
			EMASMACrossBullish,
			EMASMACrossBearish,
			HTFTrendUp,
			HTFTrendDown,
			PATUp_Long,
			PATDown_Short,
		    MomentumLong,
		    MomentumShort,
		    PullbackLong,
		    PullbackShort,
		    MA_CrossLong,
		    MA_CrossShort,
		    HighVolumeBreakoutLong,
		    HighVolumeBreakdownShort,
		    ATRBreakoutLong,
		    ATRBreakdownShort,
		    GapUpLong,
		    GapDownShort,
			extremeRSILong,
			extremeRSIShort,
			ATH_Long,
			ATL_Short,
			valleyLongSignalEvent,
			peakShortSignalEvent,
			BBLongSignal,
			BBShortSignal,
			exitAction,
			noAction
		}
		public enum signalReturnActionType
		{
			Bearish,
			Bullish,
			Neutral			
		}
		
		public enum XMLactionLoad
		{
		
			UseCombined,
			UseInstrument,
			NoAction
			
		}
		
		public enum XMLactionSave
		{
			OverwriteCombined,
			CreateForInstrument,			
			NoAction
			
		}
		public class signalPackage
		{
			public signalReturnAction SignalReturnAction{ get; set; } 		
			public marketCondition thisMarketCondition { get; set;}
			public signalReturnActionType Sentiment { get; set;}
			public DateTime creationDate  { get; set;}
			public int instrumentSeriesIndex { get; set;}
			public double instrumentSeriesTickValue { get; set;}
			public double price { get; set;}
			public string SignalContextId { get; set; } // Added for pattern performance timeline
		}

		
		public class entryPatternPerformanceTracker
		{
			public signalExitAction SignalExitAction{ get; set; } 
			public double profitGain { get; set; } 
			public double profitLoss { get; set; } 
			public int wins { get; set; } 
			public int losses { get; set; } 
		}
		public class SharpeRatioResult
		{
		    public double SharpeRatio { get; set; }
		    public string DebugInfo { get; set; }
		    public double SharpeBasedScore { get; set; } // Add this field to include the Sharpe-based score
		}
		
		[Serializable]
		public class OrderPerformanceTracker
		{
		    public signalReturnAction EntrySignalReturnAction { get; set; }
		    public int Wins { get; set; }
		    public int Losses { get; set; }
			public int barFirstSeen { get; set; }
			public int latestBarSeen { get; set; }
		    public double ProfitGain { get; set; }
		    public double ProfitLoss { get; set; }
			public double CumulativeNetProfit { get; set; }
		    public double virtualScore { get; set; } // New property for virtual scores
			public double thompsonScoreATH { get; set; } // New property for virtual scores
			public double thompsonScore { get; set; } // New property for virtual scores
		    public double realScore { get; set; } // New property for real scores
			public double realPenaltyScore { get; set; } // New property for real scores
			public double virtualPenaltyScore { get; set; } // New property for real scores
			public double virtualWinRate { get; set; } // New property for real scores
			public bool DQ { get; set; } // New property for real scores			   
			public double Alpha { get; set; } = 1.0; // Add Alpha and Beta for Thompson Sampling
		    public double Beta { get; set; } = 1.0; // Add Alpha and Beta for Thompson Sampling
			
		}




		
		public class marketConditionPerformanceTracker
		{
			public marketCondition MarketCondition { get; set; } 
			public double profitGain { get; set; } 
			public double profitLoss { get; set; } 
			public int wins { get; set; } 
			public int losses { get; set; } 
		}
		
		public class marketCondition
		{
			public marketConditionName conditionName { get; set; } 
			public double conditionValue { get; set; } 
		}
		
		public enum marketConditionName
		{
			BollingerBandWidth,
		}
		
		public class reversalOrder
		{
		    public double reversalOrderPrice { get; set; }
			public marketPositionsAllowed direction { get; set; }    
			public int maxAge { get;set; }
			public string appendSignal { get;set; }
		}
		
		public class OrderSupplementals
		{
			public simulatedStop SimulatedStop { get; set; }
			public simulatedEntry SimulatedEntry { get; set; }
			public bool forceExit { get; set; }
			public signalExitAction thisSignalExitAction { get; set;}
			public signalPackage sourceSignalPackage {get; set; }
			public string ExitReason { get; set; }
			public string patternSubtype { get; set; }
			public string patternId { get; set; }
			public double divergence { get; set; }
			public double maxDivergence { get; set; }
			public bool hasScaledIn { get; set; }
			public double pullbackModifier { get; set; }
			public double stopModifier { get; set; }
			public bool isEntryRegisteredDTW { get; set; }
			public DateTime? forceExitTimestamp { get; set; } // Added to track when exit was flagged

		}
		public class OrderPriceStats
		{
			public double OrderStatsEntryPrice { get; set; }
			public double OrderStatsExitPrice { get; set; }
			public double OrderStatsProfit { get; set; }
			public double OrderStatsProfitSynth { get; set; }
			public double OrderStatsHardProfitTarget { get; set; }
			public double OrderStatsAllTimeHighProfit { get; set; }
			public double OrderStatsAllTimeLowProfit { get; set; }
			public double OrderStatspullBackThreshold {get; set; }
			public double OrderStatspullBackPct {get; set; }
			public double OrderMaxLoss {get; set; }
		}
		public class ExitFunctions
		{
		    public Func<FunctionResponses> profitfunctiongoeshere { get; set; }  // For profit exit logic
		    public Func<FunctionResponses> lossfunctiongoeshere { get; set; }    // For loss exit logic
		}
		
		public enum FunctionResponses
		{
		    EnterLong,
			EnterShort,	
			EnterLongAggressive,
			EnterShortAggressive,	
			ExitAll,
			NoAction
		}
		
		public class patternFunctionResponse
		{
			public FunctionResponses newSignal { get; set; }
			public string patternSubType { get; set; }
			public string patternId { get; set; }
			public double stopModifier { get; set; }
			public double pullbackModifier { get; set; }
			
		}
		
		public class OrderRecordMasterLite
		{
			public signalReturnAction EntrySignalReturnAction { get; set; }
			public string EntryOrderUUID { get; set; }
			public Order EntryOrder { get; set; }
			public Order ExitOrder { get; set; }
			public string ExitOrderUUID { get; set; }
			public OrderPriceStats PriceStats { get; set; }
			public OrderSupplementals OrderSupplementals { get; set; }
			public ExitFunctions ExitFunctions { get; set; }
			public int EntryBar { get; set; }
			public string SignalContextId { get; set; } // Added for pattern performance timeline
		}
		
		public class OrderActionResult 
	    {
			public int accountEntryQuantity  { get; set; }
			public OrderAction OA { get; set; } 
			public signalPackage signalPackageParam { get; set; }
			public string appendSignal { get; set; }
			public int thisBar { get; set; }
			public OrderType orderType { get; set; }
			public patternFunctionResponse builtSignal  { get; set; }

	      
	    }

	
		
		public class confirmationZone
		{
			 public int ExpirationBar { get; set; }
			 public double TriggerPrice { get; set; }			 
		}
		
		public class profitTracker
		{
			 public double profit { get; set; }
			 public int barIndex { get; set; }		
			 public OrderAction profitOrderAction { get; set; }	
		}
		
		public class simulatedEntry
		{
 			 public string EntryOrderUUID { get; set; }
			 public string ExitOrderUUID { get; set; }
			 public OrderAction EntryOrderAction { get; set; }
			 public int quantity { get; set; }
			 public bool isEnterReady { get; set; }
			 public int EntryBar { get; set; }
			 public signalReturnAction EntrySignalReturnAction { get; set; }
			 public OrderType EntryOrderType { get; set;}
			 public OrderRecordMasterLite OrderRecordMasterLite  { get; set;}
			 public double stopPrice  { get; set;}
			 public EMA EMATrack  { get; set;}
			 public int instrumentSeriesIndex  { get; set;}
 			 public double instrumentSeriesTickValue { get; set;}

		}
		
	
		public class simulatedStop
		{
			public string EntryOrderUUID { get; set; }
			public string ExitOrderUUID { get; set; }
			public OrderAction EntryOrderAction { get; set; }
			public int quantity { get; set; }
			public bool isExitReady { get; set; }
			public OrderRecordMasterLite OrderRecordMasterLite  { get; set;}
			public int instrumentSeriesIndex  { get; set;}
 			public double instrumentSeriesTickValue { get; set;}
		}
		
		
		public enum marketPositionsAllowed
		{
		    Any,
		    Long,
		    Short,
			None
		}
		
		public enum debugSection
		{
		    OnOrderUpdate,
		    OnExecutionUpdate,
			OnBarUpdate,
			UpdateOrderLevels,
		    ProcessExits,
			ProcessAdjustments,
			EntryLimitFunction,
			ExitLimitFunction,
			PrimarySignals,
			SecondarySignals,
			Deletions,
			Benchmark,
			signalEvent_Trending,
			signalEvent_Thompson,
			signalEvent_ThompsonTuning,
			Terminated,
			PatternHashing,
			Simulation,
			none
			
		}
		
		public enum scoringSystemMethod
		{
			EnsembleWeighted,
			ThompsonSampling
		}
		
		public enum instrumentScore
		{
			ORDERFLOW,
			ORDERFLOW2,
			TA
		
		}
	
		public class CustomPosition
		{
		    private Strategy strategy;
		    private int maxInstruments; // Number of instruments to track
			private Bars[] barsArray;

		    private Dictionary<int, int> positionQuantities; // Tracks positions by instrument index
		    private Dictionary<int, double> avgEntryPrices; // Tracks average entry price by instrument index
		
		    public CustomPosition(Strategy strategy, Bars[] barsArray, int maxInstruments)
		    {
		        this.strategy = strategy;
		        this.barsArray = barsArray;
		        this.maxInstruments = maxInstruments;
		
		        positionQuantities = new Dictionary<int, int>();
		        avgEntryPrices = new Dictionary<int, double>();
		    }
		
		    // Call this when an order is filled
		    public void OnOrderFilled(int instrumentIndex, OrderAction orderAction, int quantity, double fillPrice)
		    {
		        if (!positionQuantities.ContainsKey(instrumentIndex))
		        {
		            positionQuantities[instrumentIndex] = 0;
		            avgEntryPrices[instrumentIndex] = 0;
		        }
		
		        if (orderAction == OrderAction.Buy || orderAction == OrderAction.BuyToCover)
		        {
		            // Increment position and update average entry price
		            int currentQuantity = positionQuantities[instrumentIndex];
		            double currentAvgPrice = avgEntryPrices[instrumentIndex];
		
		            avgEntryPrices[instrumentIndex] = ((currentQuantity * currentAvgPrice) + (quantity * fillPrice)) / (currentQuantity + quantity);
		            positionQuantities[instrumentIndex] += quantity;
		        }
		        else if (orderAction == OrderAction.Sell || orderAction == OrderAction.SellShort)
		        {
		            // Decrement position
		            positionQuantities[instrumentIndex] -= quantity;
		
		            // Reset average price if position is closed
		            if (positionQuantities[instrumentIndex] == 0)
		            {
		                avgEntryPrices[instrumentIndex] = 0;
		            }
		        }
		
		    }
		
		    // Get current position quantity
		    public int GetPositionQuantity(int instrumentIndex)
		    {
		        return positionQuantities.ContainsKey(instrumentIndex) ? positionQuantities[instrumentIndex] : 0;
		    }
		
		    // Get current average entry price
		    public double GetAvgEntryPrice(int instrumentIndex)
		    {
		        return avgEntryPrices.ContainsKey(instrumentIndex) ? avgEntryPrices[instrumentIndex] : 0;
		    }
		
		    private bool IsInStrategyAnalyzer
		    {
		        get
		        {
		            return strategy.State == State.Historical && strategy.State != State.Realtime;
		        }
		    }
		
		    public int Quantity
		    {
		        get
		        {
		            if (IsInStrategyAnalyzer)
		                return strategy.Position.Quantity;
		            else
		                return strategy.PositionAccount.Quantity;
		        }
		    }
		
		    public MarketPosition MarketPosition
		    {
		        get
		        {
		            if (IsInStrategyAnalyzer)
		                return strategy.Position.MarketPosition;
		            else
		                return strategy.PositionAccount.MarketPosition;
		        }
		    }
		
		    public double AveragePrice
		    {
		        get
		        {
		            if (IsInStrategyAnalyzer)
		                return strategy.Position.AveragePrice;
		            else
		                return strategy.PositionAccount.AveragePrice;
		        }
		    }
		
		    // Get aggregate unrealized profit/loss for all instruments
		    public double GetUnrealizedProfitLossForAll(PerformanceUnit performanceUnit)
		    {
		        double totalUnrealizedPnL = 0;
		
		        // Loop through all account positions
		        int instrumentCount = 0;
		        foreach (var position in strategy.PositionsAccount)
		        {
		            if (instrumentCount >= maxInstruments) break; // Limit to max instruments
		
		            // Get the most recent close price for the instrument
		            double closePrice = GetInstrumentClosePrice(position.Instrument.FullName);
		            if (closePrice > 0) // Ensure valid close price
		            {
		                // Add the unrealized PnL for this position
		                totalUnrealizedPnL += position.GetUnrealizedProfitLoss(performanceUnit, closePrice);
		            }
		            
		
		            instrumentCount++;
		        }
		
		        return totalUnrealizedPnL;
		    }
		
		  
		
		    private double GetInstrumentClosePrice(string instrumentName)
		    {
		        Bars instrumentBars = barsArray.FirstOrDefault(b => b.Instrument.FullName == instrumentName);
		
		        if (instrumentBars != null && instrumentBars.Count > 0)
		            return instrumentBars.GetClose(barsArray[0].Count - 1); // Get the most recent close
		        else
		            throw new Exception($"Instrument {instrumentName} data not available.");
		    }
		}


		

 
}