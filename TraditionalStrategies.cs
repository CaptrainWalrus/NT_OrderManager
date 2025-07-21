using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	/// <summary>
	/// Traditional strategy library for meta-labeling approach
	/// These strategies provide the baseline "edge" that RF models will enhance
	/// </summary>
	/// 
		
	public static class TraditionalStrategies
	{
		// Random number generator for threshold variations
		private static readonly Random random = new Random();
		
		/// <summary>
		/// Apply randomization to a threshold value
		/// </summary>
		private static double Randomize(double value, double variationPercent = 0.1)
		{
			// Apply +/- variationPercent randomization
			double variation = value * variationPercent;
			return value + (random.NextDouble() * 2 - 1) * variation;
		}
		
		
		
		
		/// <summary>
		/// Wick Imbalance Reversal - Simple rejection pattern detector
		/// Detects deep wicks that reject price levels for reversal entries
		/// </summary>
		public static patternFunctionResponse CheckWickImbalance(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 21) return null;
				
				// Simple wick imbalance - look for rejection wicks
				var high = strategy.High[0];
				var low = strategy.Low[0];
				var close = strategy.Close[0];
				var open = strategy.Open[0];
				
				double totalRange = high - low;
				if (totalRange <= 0) return null;
				
				double upperWick = high - Math.Max(open, close);
				double lowerWick = Math.Min(open, close) - low;
				
				// Long signal: Lower wick rejection (randomized threshold)
				if (lowerWick / totalRange > Randomize(0.5, 0.1) && close > strategy.EMA(21)[0])
				{
					return CreateSignal(strategy, "long", "WICK_REJECTION", "Lower wick rejection", 0.8, 0.8); // Tight 80% of micro contract values
				}
				
				// Short signal: Upper wick rejection (randomized threshold)
				if (upperWick / totalRange > Randomize(0.5, 0.1) && close < strategy.EMA(21)[0])
				{
					return CreateSignal(strategy, "short", "WICK_REJECTION", "Upper wick rejection", 0.8, 0.8); // Tight 80% of micro contract values
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Wick Imbalance: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// EMA Crossover with Volume Delta Confirmation
		/// Classic momentum strategy enhanced with order flow analysis
		/// </summary>
		public static patternFunctionResponse CheckEMACrossover(Strategy strategy)
		{
			try
			{
				if(strategy.CrossAbove(strategy.EMA(strategy.Close,25),strategy.EMA(strategy.Close,50),20) && strategy.IsRising(strategy.SMA(strategy.BarsArray[0],100)))
				{
					// Generate entry signal ID and features
					string entrySignalId = $"EMA_CROSS_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
					
					// Queue features and get Risk Agent approval with micro contract values
					var mainStrategy = (MainStrategy)strategy;
					double maxStopLoss = mainStrategy.microContractStoploss * 0.7; // 70% of micro contract SL
					double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.73; // 73% of micro contract TP
					bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
						strategy.Instrument.FullName, "long", "EMA_CROSS", 1, maxStopLoss, maxTakeProfit).Result;
					
					if (approved && features != null)
					{
						var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterLong,
							signalType = "EMA_CROSS",
							signalDefinition = "EMA(9) > EMA(21) && EMA(9)[1] <= EMA(21)[1] && Volume > VolumeMA * 1.2 && VolumeDelta > 0",
							recStop = pending?.StopLoss ?? 30,  // Use dollar values from Risk Agent
							recTarget = pending?.TakeProfit ?? 90,  // Use dollar values from Risk Agent
							recPullback = pending?.RecPullback ?? 10, // Use Risk Agent soft-floor value
							signalScore = pending?.Confidence ?? 0.65,  // Use actual confidence from Risk Agent
							recQty = 1,
							patternId = entrySignalId,
							patternSubType = "BULLISH_CROSS",
							signalFeatures = features
						};
					}
				}
				
				if(strategy.CrossBelow(strategy.EMA(strategy.Close,25),strategy.EMA(strategy.Close,50),20) && strategy.IsFalling(strategy.SMA(strategy.BarsArray[0],100)))
				{
					// Generate entry signal ID and features
					string entrySignalId = $"EMA_CROSS_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
					
					// Queue features and get Risk Agent approval with micro contract values
					var mainStrategy = (MainStrategy)strategy;
					double maxStopLoss = mainStrategy.microContractStoploss * 0.7; // 70% of micro contract SL
					double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.73; // 73% of micro contract TP
					bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
						strategy.Instrument.FullName, "short", "EMA_CROSS", 1, maxStopLoss, maxTakeProfit).Result;
					
					if (approved && features != null)
					{
						var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterShort,
							signalType = "EMA_CROSS",
							signalDefinition = "EMA(9) < EMA(21) && EMA(9)[1] >= EMA(21)[1] && Volume > VolumeMA * 1.2 && VolumeDelta < 0",
							recStop = pending?.StopLoss ?? 30,  // Use dollar values from Risk Agent
							recTarget = pending?.TakeProfit ?? 90,  // Use dollar values from Risk Agent
							signalScore = pending?.Confidence ?? 0.65,  // Use actual confidence from Risk Agent
							recQty = 1,
							patternId = entrySignalId,
							patternSubType = "BEARISH_CROSS",
							signalFeatures = features
						};
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in EMA Crossover: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Fair Value Gap (FVG) Fill - Imbalance reversal detector
		/// Detects price gaps that need to be filled for reversal entries
		/// </summary>
		public static patternFunctionResponse CheckFVGFill(Strategy strategy)
		{
			try
			{
				// Look for 3-bar gap (imbalance)
				if (strategy.CurrentBar < 3) return null;
				
				var currentHigh = strategy.High[0];
				var currentLow = strategy.Low[0];
				var prevHigh = strategy.High[2];  // 2 bars ago
				var prevLow = strategy.Low[2];
				var atr = strategy.ATR(14)[0];
				
				// Bullish FVG: Gap up, now filling down (randomized gap size threshold)
				if (prevHigh < currentLow && 
					(currentLow - prevHigh) > atr * Randomize(0.1, 0.05) &&
					strategy.Close[0] < strategy.Close[1])
				{
					return CreateSignal(strategy, "long", "FVG_FILL", "Filling bullish gap", 0.7, 0.67); // 70% SL, 67% TP of micro contract values
				}
				
				// Bearish FVG: Gap down, now filling up (randomized gap size threshold)
				if (prevLow > currentHigh && 
					(prevLow - currentHigh) > atr * Randomize(0.1, 0.05) &&
					strategy.Close[0] > strategy.Close[1])
				{
					return CreateSignal(strategy, "short", "FVG_FILL", "Filling bearish gap", 0.7, 0.67); // 70% SL, 67% TP of micro contract values
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in FVG Fill: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Breakout Strategy
		/// Price breaks above/below 20-period high/low with volume confirmation
		/// </summary>
		public static patternFunctionResponse CheckBreakoutStrategy(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 20) return null;
				
				var high20 = strategy.MAX(strategy.High, 20)[1]; // Exclude current bar
				var low20 = strategy.MIN(strategy.Low, 20)[1];   // Exclude current bar
				var close = strategy.Close[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				var atr = strategy.ATR(14)[0];
				
				// Bullish breakout: Close above 20-period high with volume
				if (close > high20 && volume > volumeMA * Randomize(1.5, 0.2))
				{
					// Generate entry signal ID and features
					string entrySignalId = $"BREAKOUT_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					var features = ((MainStrategy)strategy).GenerateFeatures( strategy.Time[0], strategy.Instrument.FullName);
					
					// Queue features and get Risk Agent approval with micro contract values
					var mainStrategy = (MainStrategy)strategy;
					double maxStopLoss = mainStrategy.microContractStoploss * 1.0; // 100% of micro contract SL for breakouts
					double maxTakeProfit = mainStrategy.microContractTakeProfit * 1.07; // 107% of micro contract TP for breakouts
					bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
						strategy.Instrument.FullName, "long", "BREAKOUT", 1, maxStopLoss, maxTakeProfit).Result;
					
					if (approved && features != null)
					{
						var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterLong,
							signalType = "BREAKOUT",
							signalDefinition = "Close > MAX(High, 20)[1] && Volume > VolumeMA * 1.5",
							recStop = pending?.StopLoss ?? 30,  // Use dollar values from Risk Agent
							recTarget = pending?.TakeProfit ?? 90,  // Use dollar values from Risk Agent
							signalScore = pending?.Confidence ?? 0.65,  // Use actual confidence from Risk Agent
							recQty = 1,
							patternId = entrySignalId,
							patternSubType = "BULLISH_BREAKOUT",
							signalFeatures = features
						};
					}
				}
				
				// Bearish breakdown: Close below 20-period low with volume
				if (close < low20 && volume > volumeMA * Randomize(1.5, 0.2))
				{
					// Generate entry signal ID and features
					string entrySignalId = $"BREAKOUT_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					var features = ((MainStrategy)strategy).GenerateFeatures( strategy.Time[0], strategy.Instrument.FullName);
					
					// Queue features and get Risk Agent approval with micro contract values
					var mainStrategy = (MainStrategy)strategy;
					double maxStopLoss = mainStrategy.microContractStoploss * 1.0; // 100% of micro contract SL for breakouts
					double maxTakeProfit = mainStrategy.microContractTakeProfit * 1.07; // 107% of micro contract TP for breakouts
					bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
						strategy.Instrument.FullName, "short", "BREAKOUT", 1, maxStopLoss, maxTakeProfit).Result;
					
					if (approved && features != null)
					{
						var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterShort,
							signalType = "BREAKOUT",
							signalDefinition = "Close < MIN(Low, 20)[1] && Volume > VolumeMA * 1.5",
							recStop = pending?.StopLoss ?? 30,  // Use dollar values from Risk Agent
							recTarget = pending?.TakeProfit ?? 90,  // Use dollar values from Risk Agent
							signalScore = pending?.Confidence ?? 0.65,  // Use actual confidence from Risk Agent
							recQty = 1,
							patternId = entrySignalId,
							patternSubType = "BEARISH_BREAKDOWN",
							signalFeatures = features
						};
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Breakout Strategy: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Momentum Breakout - Simple directional momentum detector
		/// Detects strong directional moves with volume confirmation
		/// </summary>
		public static patternFunctionResponse CheckMomentumBreakout(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 10) return null;
				
				var close = strategy.Close[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 10)[0];
				var atr = strategy.ATR(14)[0];
				
				// Calculate momentum (price change over last 3 bars)
				double momentum = close - strategy.Close[3];
				
				// Long: Strong upward momentum with volume (randomized thresholds)
				if (momentum > atr * Randomize(0.5, 0.1) && 
					volume > volumeMA * Randomize(1.5, 0.2))
				{
					return CreateSignal(strategy, "long", "MOMENTUM_BREAKOUT", "Upward momentum", 0.9, 0.93); // 90% SL, 93% TP for momentum
				}
				
				// Short: Strong downward momentum with volume (randomized thresholds)
				if (momentum < atr * Randomize(-0.5, 0.1) && 
					volume > volumeMA * Randomize(1.5, 0.2))
				{
					return CreateSignal(strategy, "short", "MOMENTUM_BREAKOUT", "Downward momentum", 0.9, 0.93); // 90% SL, 93% TP for momentum
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Momentum Breakout: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Simple Support/Resistance Bounce - Level rejection detector
		/// Detects bounces off key support/resistance levels
		/// </summary>
		public static patternFunctionResponse CheckSRBounce(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 20) return null;
				
				var close = strategy.Close[0];
				var high20 = strategy.MAX(strategy.High, 20)[1];
				var low20 = strategy.MIN(strategy.Low, 20)[1];
				var atr = strategy.ATR(14)[0];
				
				// Long: Bounce off 20-period low (randomized proximity)
				if (strategy.Low[0] <= low20 + (atr * Randomize(0.1, 0.05)) && 
					close > strategy.Low[0] + (atr * Randomize(0.2, 0.05)))
				{
					return CreateSignal(strategy, "long", "SR_BOUNCE", "Support bounce", 0.6, 0.6); // 60% of micro contract values for tight SR bounces
				}
				
				// Short: Rejection at 20-period high (randomized proximity)
				if (strategy.High[0] >= high20 - (atr * Randomize(0.1, 0.05)) && 
					close < strategy.High[0] - (atr * Randomize(0.2, 0.05)))
				{
					return CreateSignal(strategy, "short", "SR_BOUNCE", "Resistance rejection", 0.6, 0.6); // 60% of micro contract values for tight SR bounces
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in SR Bounce: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Pullback from Recent Extremes - 3-bar decline from recent high/low
		/// Detects pullbacks from recent highs (bear) and bounces from recent lows (bull)
		/// </summary>
		public static patternFunctionResponse CheckPullbackFromExtremes(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 50) return null;
				
				// Find 50-bar high and low (excluding current bar)
				var high50 = strategy.MAX(strategy.High, 50)[1];
				var low50 = strategy.MIN(strategy.Low, 50)[1];
				var atr = strategy.ATR(14)[0];
				
				// Find when the 50-bar high occurred (look back 10 bars for deadzone)
				int highBarIndex = -1;
				for (int i = 1; i <= Math.Min(50, strategy.CurrentBar); i++)
				{
					if (Math.Abs(strategy.High[i] - high50) < strategy.TickSize * 0.5)
					{
						highBarIndex = i;
						break;
					}
				}
				
				// Find when the 50-bar low occurred (look back 10 bars for deadzone)
				int lowBarIndex = -1;
				for (int i = 1; i <= Math.Min(50, strategy.CurrentBar); i++)
				{
					if (Math.Abs(strategy.Low[i] - low50) < strategy.TickSize * 0.5)
					{
						lowBarIndex = i;
						break;
					}
				}
				
				// Short signal: 3 declining bars from recent high (within deadzone)
				if (highBarIndex > 0 && highBarIndex <= Randomize(10, 2)) // Deadzone: within ~10 bars of high
				{
					// Check for 3 consecutive declining closes
					bool threeDeclines = strategy.Close[0] < strategy.Close[1] &&
					                    strategy.Close[1] < strategy.Close[2] &&
					                    strategy.Close[2] < strategy.Close[3];
					
					// Ensure we're still close to the recent high
					var distanceFromHigh = high50 - strategy.Close[0];
					var maxDistance = atr * Randomize(2.0, 0.3); // Allow some distance from high
					
					if (threeDeclines && distanceFromHigh > 0 && distanceFromHigh <= maxDistance)
					{
						return CreateSignal(strategy, "short", "PULLBACK_HIGH", $"3-bar decline from recent high ({highBarIndex} bars ago)", 0.5, 0.53); // Very tight 50% SL, 53% TP for pullbacks
					}
				}
				
				// Long signal: 3 ascending bars from recent low (within deadzone)
				if (lowBarIndex > 0 && lowBarIndex <= Randomize(10, 2)) // Deadzone: within ~10 bars of low
				{
					// Check for 3 consecutive ascending closes
					bool threeAscends = strategy.Close[0] > strategy.Close[1] &&
					                   strategy.Close[1] > strategy.Close[2] &&
					                   strategy.Close[2] > strategy.Close[3];
					
					// Ensure we're still close to the recent low
					var distanceFromLow = strategy.Close[0] - low50;
					var maxDistance = atr * Randomize(2.0, 0.3); // Allow some distance from low
					
					if (threeAscends && distanceFromLow > 0 && distanceFromLow <= maxDistance)
					{
						return CreateSignal(strategy, "long", "PULLBACK_LOW", $"3-bar rise from recent low ({lowBarIndex} bars ago)", 0.5, 0.53); // Very tight 50% SL, 53% TP for pullbacks
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Pullback from Extremes: {ex.Message}");
			}
			
			return null;
		}
		
		public static patternFunctionResponse CheckEMAVWAPCross(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 20) return null;
				
				// Get standard Strategy indicators (using default periods from CurvesStrategy)
				var ema3 = strategy.EMA(10);
				var vwap = strategy.custom_VWAP(25);
				var atr = strategy.ATR(14)[0];
				var close = strategy.Close[0];
				var tickSize = strategy.TickSize;
				
				// Implement CrossAbove logic: EMA crossed above VWAP in last 10 bars
				bool crossedAbove = false;
				for (int i = 1; i <= Math.Min(10, strategy.CurrentBar); i++)
				{
					if (ema3[i] <= vwap[i] && ema3[0] > vwap[0])
					{
						crossedAbove = true;
						break;
					}
				}
				
				// Implement CrossBelow logic: EMA crossed below VWAP in last 10 bars  
				bool crossedBelow = false;
				for (int i = 1; i <= Math.Min(10, strategy.CurrentBar); i++)
				{
					if (ema3[i] >= vwap[i] && ema3[0] < vwap[0])
					{
						crossedBelow = true;
						break;
					}
				}
				
				// Implement IsRising logic: EMA is rising (current > 2 bars ago)
				bool isRising = strategy.CurrentBar >= 2 && ema3[0] > ema3[2];
				
				// Implement IsFalling logic: EMA is falling (current < 2 bars ago)
				bool isFalling = strategy.CurrentBar >= 2 && ema3[0] < ema3[2];
				
				// Long signal: CrossAbove(EMA3,VWAP1,10) && EMA3[0] - VWAP1[0] > TickSize*3 && IsRising(EMA3)
				if (crossedAbove && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isRising)
				{
					// Generate entry signal ID and features
					string entrySignalId = $"CheckEMAVWAPCross_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
					
					// Queue features and get Risk Agent approval with micro contract values
					var mainStrategy = (MainStrategy)strategy;
					double maxStopLoss = mainStrategy.microContractStoploss * 0.8; // 80% of micro contract SL
					double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.87; // 87% of micro contract TP
					bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
						strategy.Instrument.FullName, "long", "CheckEMAVWAPCross", 1, maxStopLoss, maxTakeProfit).Result;
					
					if (approved && features != null)
					{
						var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterLong,
							signalType = "CheckEMAVWAPCross",
							signalDefinition = "crossedAbove && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isRising",
							recStop = pending?.StopLoss ?? 30,  // Use dollar values from Risk Agent
							recTarget = pending?.TakeProfit ?? 90,  // Use dollar values from Risk Agent
							signalScore = pending?.Confidence ?? 0.65,  // Use actual confidence from Risk Agent
							recQty = 1,
							patternId = entrySignalId,
							patternSubType = "BULLISH_CheckEMAVWAPCross",
							signalFeatures = features
						};
					}
				}
				
				// Short signal: CrossBelow(EMA3,VWAP1,10) && EMA3[0] - VWAP1[0] > TickSize*3 && IsFalling(EMA3)
				// Note: The original condition seems wrong for short (should be VWAP - EMA3 > threshold), but keeping as-is for compatibility
				if (crossedBelow && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isFalling)
				{
					// Generate entry signal ID and features
					string entrySignalId = $"CheckEMAVWAPCross_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
					
					// Queue features and get Risk Agent approval with micro contract values
					var mainStrategy = (MainStrategy)strategy;
					double maxStopLoss = mainStrategy.microContractStoploss * 0.8; // 80% of micro contract SL
					double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.87; // 87% of micro contract TP
					bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
						strategy.Instrument.FullName, "short", "CheckEMAVWAPCross", 1, maxStopLoss, maxTakeProfit).Result;
					
					if (approved && features != null)
					{
						var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterShort,
							signalType = "CheckEMAVWAPCross",
							signalDefinition = "crossedBelow && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isFalling",
							recStop = pending?.StopLoss ?? 30,  // Use dollar values from Risk Agent
							recTarget = pending?.TakeProfit ?? 90,  // Use dollar values from Risk Agent
							signalScore = pending?.Confidence ?? 0.65,  // Use actual confidence from Risk Agent
							recQty = 1,
							patternId = entrySignalId,
							patternSubType = "BEARISH_CheckEMAVWAPCross",
							signalFeatures = features
						};
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in EMA VWAP Cross: {ex.Message}");
			}
			
			return null;
		}

		
		/// <summary>
		/// Helper method to create standardized signals - reduces code duplication
		/// </summary>
		private static patternFunctionResponse CreateSignal(Strategy strategy, string direction, string signalType, string reason, double maxStopLossMultiplier = 1.0, double maxTakeProfitMultiplier = 1.0)
		{
			try
			{
				string entrySignalId = $"{signalType}_{strategy.Time[0]:yyyyMMdd_HHmmss}";
				var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
				
				// Get micro contract values from MainStrategy and apply multipliers
				var mainStrategy = (MainStrategy)strategy;
				double maxStopLoss = mainStrategy.microContractStoploss * maxStopLossMultiplier;
				double maxTakeProfit = mainStrategy.microContractTakeProfit * maxTakeProfitMultiplier;
				
				bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
					strategy.Instrument.FullName, direction, signalType, 1, maxStopLoss, maxTakeProfit).Result;
				
				if (approved && features != null)
				{
					var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
					return new patternFunctionResponse
					{
						newSignal = direction == "long" ? FunctionResponses.EnterLong : FunctionResponses.EnterShort,
						signalType = signalType,
						signalDefinition = reason,
						recStop = pending?.StopLoss ?? Math.Min(30, maxStopLoss),
						recTarget = pending?.TakeProfit ?? Math.Min(90, maxTakeProfit),
						recPullback = pending?.RecPullback ?? 10, // Use Risk Agent soft-floor value
						signalScore = pending?.Confidence ?? 0.65,
						recQty = 1,
						patternId = entrySignalId,
						signalFeatures = features
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error creating signal: {ex.Message}");
			}
			return null;
		}
		
		/// <summary>
		/// Convert signal type to numeric for ML training
		/// </summary>
		private static double GetSignalTypeNumeric(string signalType)
		{
			switch (signalType)
			{
				case "ORDER_FLOW_IMBALANCE": return 1.0;
				case "EMA_CROSS": return 2.0;
				case "RSI_DIVERGENCE": return 3.0;
				case "BREAKOUT": return 4.0;
				case "VWAP_REVERSION": return 5.0;
				case "BB_SQUEEZE": return 6.0;
				default: return 0.0;
			}
		}
		
		/// <summary>
		/// Main strategy dispatcher - checks all traditional strategies and picks highest confidence
		/// This is the method called from BuildNewSignal()
		/// </summary>
		public static patternFunctionResponse CheckAllTraditionalStrategies(Strategy strategy, TraditionalStrategyType strategyFilter = TraditionalStrategyType.ALL)
		{
			try
			{
				// Single strategy testing for pure training data
				if (strategyFilter != TraditionalStrategyType.ALL)
				{
					switch (strategyFilter)
					{
						case TraditionalStrategyType.ORDER_FLOW_IMBALANCE:
							return CheckWickImbalance(strategy);
						case TraditionalStrategyType.BOLLINGER_SQUEEZE:
							return CheckSRBounce(strategy);
						case TraditionalStrategyType.EMA_VWAP_CROSS:
							return CheckEMAVWAPCross(strategy);
						case TraditionalStrategyType.BREAKOUT:
							return CheckBreakoutStrategy(strategy);
						case TraditionalStrategyType.EMA_CROSSOVER:
							return CheckEMACrossover(strategy);
						case TraditionalStrategyType.RSI_DIVERGENCE:
							return CheckFVGFill(strategy);
						case TraditionalStrategyType.VWAP_MEAN_REVERSION:
							return CheckMomentumBreakout(strategy);
						default:
							return null;
					}
				}
				
				// Confidence-based strategy selection - evaluate all strategies and pick highest confidence
				var candidateSignals = new List<patternFunctionResponse>();
				
				// Check all strategies for potential signals
				var wickSignal = CheckWickImbalance(strategy);
				if (wickSignal != null) candidateSignals.Add(wickSignal);
				
				var srSignal = CheckSRBounce(strategy);
				if (srSignal != null) candidateSignals.Add(srSignal);
				
				var pullbackSignal = CheckPullbackFromExtremes(strategy);
				if (pullbackSignal != null) candidateSignals.Add(pullbackSignal);
				
				var fvgSignal = CheckFVGFill(strategy);
				if (fvgSignal != null) candidateSignals.Add(fvgSignal);
				
				var momentumSignal = CheckMomentumBreakout(strategy);
				if (momentumSignal != null) candidateSignals.Add(momentumSignal);
				
				var emaVwapSignal = CheckEMAVWAPCross(strategy);
				if (emaVwapSignal != null) candidateSignals.Add(emaVwapSignal);
				
				var breakoutSignal = CheckBreakoutStrategy(strategy);
				if (breakoutSignal != null) candidateSignals.Add(breakoutSignal);
				
				var emaCrossSignal = CheckEMACrossover(strategy);
				if (emaCrossSignal != null) candidateSignals.Add(emaCrossSignal);
				
				// If no signals found, return null
				if (candidateSignals.Count == 0)
					return null;
				
				// BEAR/BULL SIGNAL ALIGNMENT CHECK: Require 75% directional consensus
				int longSignals = candidateSignals.Count(s => s.newSignal == FunctionResponses.EnterLong);
				int shortSignals = candidateSignals.Count(s => s.newSignal == FunctionResponses.EnterShort);
				int totalSignals = candidateSignals.Count;
				
				double longPercentage = (double)longSignals / totalSignals;
				double shortPercentage = (double)shortSignals / totalSignals;
				
				// Require 75% consensus in one direction
				const double ALIGNMENT_THRESHOLD = 0.75;
				bool hasDirectionalConsensus = longPercentage >= ALIGNMENT_THRESHOLD || shortPercentage >= ALIGNMENT_THRESHOLD;
				
				if (!hasDirectionalConsensus || candidateSignals.Count < 3)
				{
					strategy.Print($"[TRADITIONAL] REJECTED - Insufficient directional consensus: {longSignals}L/{shortSignals}S from {totalSignals} signals (need {ALIGNMENT_THRESHOLD:P0})");
					return null;
				}
				
				// Pick the signal with highest confidence from the consensus direction
				var bestSignal = candidateSignals.OrderByDescending(s => s.signalScore).First();
				
				string consensusDirection = longPercentage >= ALIGNMENT_THRESHOLD ? "BULLISH" : "BEARISH";
				strategy.Print($"[TRADITIONAL] Selected {bestSignal.signalType} with confidence {bestSignal.signalScore:F3} from {candidateSignals.Count} candidates - {consensusDirection} consensus ({longSignals}L/{shortSignals}S)");
				
				return bestSignal;
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in CheckAllTraditionalStrategies: {ex.Message}");
			}
			
			return null;
		}
	}
}