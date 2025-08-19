using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// Strategy segments for voting system
    /// </summary>
    public enum StrategySegment
    {
        Math,  // Leading indicators with no lag
        Lag,   // Confirming indicators with inherent lag
        Reversal,  // Reversal-specific strategies
        Trend      // Trend-following strategies
    }

    /// <summary>
    /// Market regime types for signal classification
    /// </summary>
    public enum RegimeType
    {
        Neutral,    // Works in any regime
        Trend,      // Trend-following signals
        Reversal,   // Mean reversion/reversal signals
        Volatility  // Volatility-based signals
    }

    /// <summary>
    /// Strategy attribute to mark and configure strategy methods
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class TradingStrategyAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IncludeInAll { get; set; } = true;
        public int Priority { get; set; } = 100;
        public double MinBars { get; set; } = 20;
        public string[] RequiredIndicators { get; set; }
        public string Category { get; set; } = "General";
        public StrategySegment Segment { get; set; } = StrategySegment.Math;
        
        // NEW: Signal Strength & Decay Properties
        public RegimeType RegimeType { get; set; } = RegimeType.Neutral;
        public double InitialStrength { get; set; } = 50;  // Starting points when signal fires
        public double DecayRate { get; set; } = 0.95;      // Exponential decay per bar (0.95 = 5% decay)
        public string DecayCondition { get; set; } = "";   // Condition that affects decay rate
        public double StrengthCap { get; set; } = 100;     // Maximum strength this signal can reach
        public string ContradictionSignal { get; set; } = "";  // Signal that kills this one
        public double ConfidenceMultiplier { get; set; } = 1.0;  // Multiply strength by confidence
        public bool AccumulatesStrength { get; set; } = false;   // Can fire multiple times to add strength
        public string Direction { get; set; } = "both";    // "long", "short", or "both"

        public TradingStrategyAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Active signal tracking with decay
    /// </summary>
    public class ActiveSignal
    {
        public string SignalName { get; set; }
        public double CurrentStrength { get; set; }
        public int BarsSinceSignal { get; set; }
        public double InitialStrength { get; set; }
        public double DecayRate { get; set; }
        public string DecayCondition { get; set; }
        public string Direction { get; set; }  // "long" or "short"
        public double Confidence { get; set; }
        public DateTime LastUpdate { get; set; }
        
        public double GetDecayedStrength(Strategy strategy)
        {
            // Base exponential decay
            double decayedStrength = CurrentStrength * Math.Pow(DecayRate, BarsSinceSignal);
            
            // Modify decay based on condition
            double conditionMultiplier = EvaluateDecayCondition(strategy, Direction);
            decayedStrength *= conditionMultiplier;
            
            // Apply confidence multiplier
            decayedStrength *= (0.5 + Confidence * 0.5); // Scale confidence impact
            
            return Math.Max(0, decayedStrength); // Never go negative
        }
        
        private double EvaluateDecayCondition(Strategy strategy, string direction)
        {
            try
            {
                switch (DecayCondition)
                {
                    case "EMA_Separation":
                        // If EMAs spreading apart in signal direction, maintain strength
                        if (strategy is MainStrategy ms && ms.EMA3 != null && ms.EMA4 != null)
                        {
                            double separation = ms.EMA3[0] - ms.EMA4[0];
                            double prevSeparation = ms.EMA3[1] - ms.EMA4[1];
                            
                            if (direction == "long")
                                return separation > prevSeparation ? 1.1 : 0.9;
                            else
                                return separation < prevSeparation ? 1.1 : 0.9;
                        }
                        return 1.0;
                        
                    case "RSI_Recovery":
                        // RSI moving away from extreme maintains strength
                        var rsi = strategy.RSI(14, 1);
                        double rsiChange = rsi[0] - rsi[1];
                        
                        if (direction == "long" && rsi[0] < 50)
                            return rsiChange > 0 ? 1.15 : 0.85;
                        else if (direction == "short" && rsi[0] > 50)
                            return rsiChange < 0 ? 1.15 : 0.85;
                        return 1.0;
                        
                    case "Volume_Support":
                        // High volume maintains signal strength
                        double volRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 20)[0];
                        return volRatio > 1.2 ? 1.2 : (volRatio < 0.8 ? 0.8 : 1.0);
                        
                    case "Price_Respect":
                        // Price respecting breakout/reversal level
                        // This would need the original signal level stored
                        return 1.0; // Simplified for now
                        
                    default:
                        return 1.0;  // No condition modifier
                }
            }
            catch
            {
                return 1.0; // Safe default on any error
            }
        }
    }

    /// <summary>
    /// Signal Strength Manager - tracks all active signals with decay
    /// </summary>
    public class SignalStrengthManager
    {
        private Dictionary<string, ActiveSignal> activeSignals = new Dictionary<string, ActiveSignal>();
        private Strategy strategy;
        
        // Thresholds for entry
        public const double ENTRY_THRESHOLD = 150;  // Need 150+ strength points to enter
        public const double DOMINANCE_RATIO = 2.0;  // Need 2x stronger than opposite direction
        public const double MIN_SIGNAL_STRENGTH = 5;  // Below this, signal is dead
        
        public SignalStrengthManager(Strategy strat)
        {
            strategy = strat;
        }
        
        public void AddOrUpdateSignal(string name, double initialStrength, double decayRate, 
            string decayCondition, string direction, double confidence, bool accumulates)
        {
            string key = $"{name}_{direction}";
            
            if (activeSignals.ContainsKey(key))
            {
                if (accumulates)
                {
                    // Add partial strength to existing signal
                    activeSignals[key].CurrentStrength += initialStrength * 0.5;
                    activeSignals[key].CurrentStrength = Math.Min(activeSignals[key].CurrentStrength, 
                        initialStrength * 1.5); // Cap at 1.5x initial
                }
                else
                {
                    // Reset the signal with new strength
                    activeSignals[key].CurrentStrength = initialStrength * confidence;
                }
                activeSignals[key].BarsSinceSignal = 0;  // Reset decay counter
                activeSignals[key].Confidence = confidence;
            }
            else
            {
                activeSignals[key] = new ActiveSignal
                {
                    SignalName = name,
                    CurrentStrength = initialStrength * confidence,
                    InitialStrength = initialStrength,
                    DecayRate = decayRate,
                    BarsSinceSignal = 0,
                    DecayCondition = decayCondition,
                    Direction = direction,
                    Confidence = confidence,
                    LastUpdate = DateTime.Now
                };
            }
            
            // Check for contradiction signals and kill them
            CheckContradictions(name, direction);
        }
        
        private void CheckContradictions(string signalName, string direction)
        {
            // Kill opposite direction signals of same type
            string oppositeKey = $"{signalName}_{(direction == "long" ? "short" : "long")}";
            if (activeSignals.ContainsKey(oppositeKey))
            {
                activeSignals.Remove(oppositeKey);
                strategy.Print($"[SIGNAL-DECAY] Killed contradicting signal: {oppositeKey}");
            }
            
            // Specific contradiction rules
            if (signalName.Contains("EMA_CROSS"))
            {
                // EMA cross in one direction kills opposite
                var toRemove = activeSignals.Keys
                    .Where(k => k.Contains("EMA_CROSS") && !k.Contains(direction))
                    .ToList();
                foreach (var key in toRemove)
                    activeSignals.Remove(key);
            }
        }
        
        public double GetBullStrength()
        {
            return activeSignals
                .Where(s => s.Value.Direction == "long")
                .Sum(s => s.Value.GetDecayedStrength(strategy));
        }
        
        public double GetBearStrength()
        {
            return activeSignals
                .Where(s => s.Value.Direction == "short")
                .Sum(s => s.Value.GetDecayedStrength(strategy));
        }
        
        public void OnBarUpdate()
        {
            // Increment bars counter for all signals
            foreach (var signal in activeSignals.Values)
            {
                signal.BarsSinceSignal++;
            }
            
            // Remove dead signals
            var toRemove = activeSignals
                .Where(s => s.Value.GetDecayedStrength(strategy) < MIN_SIGNAL_STRENGTH)
                .Select(s => s.Key)
                .ToList();
                
            foreach (var key in toRemove)
            {
                strategy.Print($"[SIGNAL-DECAY] Removing dead signal: {key} (strength < {MIN_SIGNAL_STRENGTH})");
                activeSignals.Remove(key);
            }
        }
        
        public string GetActiveSignalsDebug()
        {
            var signals = activeSignals
                .Select(s => $"{s.Key}: {s.Value.GetDecayedStrength(strategy):F1}")
                .ToList();
            return string.Join(", ", signals);
        }
        
        public bool ShouldEnterLong()
        {
            double bullStrength = GetBullStrength();
            double bearStrength = GetBearStrength();
            
            return bullStrength > ENTRY_THRESHOLD && 
                   bullStrength > bearStrength * DOMINANCE_RATIO;
        }
        
        public bool ShouldEnterShort()
        {
            double bullStrength = GetBullStrength();
            double bearStrength = GetBearStrength();
            
            return bearStrength > ENTRY_THRESHOLD && 
                   bearStrength > bullStrength * DOMINANCE_RATIO;
        }
    }

    /// <summary>
    /// Improved Traditional Strategies with automatic registration and discovery
    /// </summary>
    public class ImprovedTraditionalStrategies
    {
        private readonly Random random = new Random();
        private Dictionary<string, MethodInfo> strategyMethods;
        private Dictionary<string, TradingStrategyAttribute> strategyAttributes;
        private bool initialized = false;
        private SignalStrengthManager signalManager;
        
        // Global strength tracking for voting system (LEGACY - kept for compatibility)
        private double MathStrengthBull = 0;
        private double MathStrengthBear = 0;
        private const double DecayRate = 0.95; // Per-bar decay rate
        private const double StrengthIncrement = 0.3; // Increment per agreeing category
        private const double SkewThreshold = 1.5; // 50% skew requirement

        public ImprovedTraditionalStrategies()
        {
            InitializeStrategies();
        }
        
        public void InitializeSignalManager(Strategy strategy)
        {
            if (signalManager == null)
            {
                signalManager = new SignalStrengthManager(strategy);
            }
        }

        /// <summary>
        /// Initialize and discover all strategy methods via reflection
        /// </summary>
        private void InitializeStrategies()
        {
            if (initialized) return;

            strategyMethods = new Dictionary<string, MethodInfo>();
            strategyAttributes = new Dictionary<string, TradingStrategyAttribute>();

            // Get all methods with TradingStrategy attribute
            var methods = this.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TradingStrategyAttribute>() != null)
                .OrderBy(m => m.GetCustomAttribute<TradingStrategyAttribute>().Priority);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<TradingStrategyAttribute>();
                strategyMethods[attr.Name] = method;
                strategyAttributes[attr.Name] = attr;
            }

            initialized = true;
        }

        /// <summary>
        /// Get list of all available strategies
        /// </summary>
        public List<string> GetAvailableStrategies(bool includeAll = true)
        {
            InitializeStrategies();
            return strategyAttributes
                .Where(kvp => includeAll || kvp.Value.IncludeInAll)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Get strategies by category
        /// </summary>
        public List<string> GetStrategiesByCategory(string category)
        {
            InitializeStrategies();
            return strategyAttributes
                .Where(kvp => kvp.Value.Category == category)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Execute a specific strategy by name
        /// </summary>
        public patternFunctionResponse ExecuteStrategy(Strategy strategy, string strategyName)
        {
            InitializeStrategies();
            
            if (!strategyMethods.ContainsKey(strategyName))
                return null;

            var attr = strategyAttributes[strategyName];
            
            // Check minimum bars requirement
            if (strategy.CurrentBar < attr.MinBars)
                return null;

            try
            {
                var method = strategyMethods[strategyName];
                return (patternFunctionResponse)method.Invoke(this, new object[] { strategy });
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error executing {strategyName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Execute all strategies and return signals with consensus
        /// </summary>
        public patternFunctionResponse ExecuteAll(Strategy strategy, double consensusThreshold = 0.3, StrategyConfig config = null)
        {
            InitializeStrategies();
            
            var signals = new List<patternFunctionResponse>();
            var strategiesToRun = GetAvailableStrategies(true);

            foreach (var strategyName in strategiesToRun)
            {
                // Skip if config restricts it
                if (config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(strategyName))
                    continue;
                    
                var signal = ExecuteStrategy(strategy, strategyName);
                if (signal != null)
                    signals.Add(signal);
            }

            return ApplyConsensusLogic(strategy, signals, consensusThreshold);
        }

        /// <summary>
        /// Execute strategies matching a filter pattern
        /// </summary>
        public patternFunctionResponse ExecuteFiltered(Strategy strategy, Func<TradingStrategyAttribute, bool> filter)
        {
            InitializeStrategies();
            
            var signals = new List<patternFunctionResponse>();
            var strategies = strategyAttributes
                .Where(kvp => filter(kvp.Value))
                .Select(kvp => kvp.Key);

            foreach (var strategyName in strategies)
            {
                var signal = ExecuteStrategy(strategy, strategyName);
                if (signal != null)
                    signals.Add(signal);
            }

            return signals.FirstOrDefault(); // Or apply consensus
        }

        /// <summary>
        /// Apply decay to strength values - call this on each bar update
        /// </summary>
        public void ApplyStrengthDecay()
        {
            MathStrengthBull *= DecayRate;
            MathStrengthBear *= DecayRate;
            
            // Clamp to zero if very small
            if (MathStrengthBull < 0.01) MathStrengthBull = 0;
            if (MathStrengthBear < 0.01) MathStrengthBear = 0;
        }

        /// <summary>
        /// Execute decay-based signal strength system
        /// </summary>
        public patternFunctionResponse ExecuteDecaySystem(Strategy strategy, StrategyConfig config = null)
        {
            InitializeStrategies();
            InitializeSignalManager(strategy);
            
            // Update decay for all signals
            signalManager.OnBarUpdate();
            
            // If no config provided, create empty config (no signal filtering)
            if (config == null)
            {
                config = new StrategyConfig();
            }
            
            // Process all enabled signals and add to strength manager
            // DEBUG: Check config status
            if (config != null)
            {
                strategy.Print($"[CONFIG DEBUG] HasFiltering: {config.HasEntryConditionFiltering()}, EntryConditions Count: {config.EntryConditions?.Count ?? 0}");
                if (config.EntryConditions != null && config.EntryConditions.Count > 0)
                {
                    var activeSignals = config.EntryConditions.Where(kvp => kvp.Value == true).Select(kvp => kvp.Key);
                    strategy.Print($"[CONFIG DEBUG] Active signals: {string.Join(", ", activeSignals)}");
                }
            }
            
            foreach (var kvp in strategyAttributes)
            {
                var attr = kvp.Value;
                var signalName = kvp.Key;
                
                // DEBUG: Log each signal check
                bool hasFiltering = config?.HasEntryConditionFiltering() ?? false;
                bool isActive = config?.IsEntryConditionActive(signalName) ?? false;
                
                // Skip if config restricts it
                if (config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(signalName))
                {
                   // strategy.Print($"[SKIP] {signalName} - Not active in config");
                    continue;
                }
                else if (hasFiltering && isActive)
                {
                    //strategy.Print($"[ACTIVE] {signalName} - Enabled in config");
                }
                
                // Execute the signal
                var signal = ExecuteStrategy(strategy, signalName);
                
                if (signal != null && signal.newSignal != FunctionResponses.NoAction)
                {
                    // Determine direction
                    string direction = signal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
                    
                    // Add to signal manager with decay properties
                    signalManager.AddOrUpdateSignal(
                        signalName,
                        attr.InitialStrength,
                        attr.DecayRate,
                        attr.DecayCondition,
                        direction,
                        signal.confidence,
                        attr.AccumulatesStrength
                    );
                    
                    strategy.Print($"[DECAY-SIGNAL] {signalName} fired {direction} - Strength: {attr.InitialStrength * signal.confidence:F1}, Decay: {attr.DecayRate}");
                }
            }
            
            // Get current strength levels
            double bullStrength = signalManager.GetBullStrength();
            double bearStrength = signalManager.GetBearStrength();
            
            // Log current state every 10 bars
            if (strategy.CurrentBar % 10 == 0)
            {
                strategy.Print($"[DECAY-STATE] Bull: {bullStrength:F1}, Bear: {bearStrength:F1} | Active: {signalManager.GetActiveSignalsDebug()}");
            }
            
            // Check for entry conditions
            if (signalManager.ShouldEnterLong())
            {
                strategy.Print($"[DECAY-ENTRY] LONG signal - Bull: {bullStrength:F1} > Threshold: {SignalStrengthManager.ENTRY_THRESHOLD}");
                
                var response = new patternFunctionResponse();
                response.newSignal = FunctionResponses.EnterLong;
                response.signalType = "DECAY_LONG";
                response.signalDefinition = $"Decay system: Bull {bullStrength:F1} vs Bear {bearStrength:F1}";
                response.confidence = Math.Min(0.9, bullStrength / SignalStrengthManager.ENTRY_THRESHOLD);
                response.signalScore = bullStrength;
                return response;
            }
            else if (signalManager.ShouldEnterShort())
            {
                strategy.Print($"[DECAY-ENTRY] SHORT signal - Bear: {bearStrength:F1} > Threshold: {SignalStrengthManager.ENTRY_THRESHOLD}");
                
                var response = new patternFunctionResponse();
                response.newSignal = FunctionResponses.EnterShort;
                response.signalType = "DECAY_SHORT";
                response.signalDefinition = $"Decay system: Bear {bearStrength:F1} vs Bull {bullStrength:F1}";
                response.confidence = Math.Min(0.9, bearStrength / SignalStrengthManager.ENTRY_THRESHOLD);
                response.signalScore = bearStrength;
                return response;
            }
            
            return null; // No signal
        }
        
        /// <summary>
        /// Execute branching strategy system: Math determines regime, then appropriate confirmation
        /// </summary>
        public patternFunctionResponse ExecuteBranchingSystem(Strategy strategy, StrategyConfig config = null)
        {
            InitializeStrategies();
            
            // If no config provided, create empty config (no signal filtering)
            if (config == null)
            {
                config = new StrategyConfig();
            }
            
            // STEP 1: Run Math segment to determine market regime (Reversal vs Trend)
            var mathSignals = new List<patternFunctionResponse>();
            
            // SIMPLIFIED: Only use highest quality signals
            // Reversal: Focus on wick reversals at extremes
            var reversalIndicators = new[] { "WICK_TREND_REVERSAL" };
            // Trend: Focus on clean breakouts
            var trendIndicators = new[] { "PRICE_BREAKOUT" };
            
            int reversalVotes = 0;
            int trendVotes = 0;
            patternFunctionResponse strongestReversal = null;
            patternFunctionResponse strongestTrend = null;
            
            // Check reversal indicators
            foreach (var indicator in reversalIndicators)
            {
                if (config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(indicator))
                    continue;
                    
                var signal = ExecuteStrategy(strategy, indicator);
                if (signal != null && signal.newSignal != FunctionResponses.NoAction)
                {
                    reversalVotes++;
                    if (strongestReversal == null || signal.confidence > strongestReversal.confidence)
                        strongestReversal = signal;
                }
            }
            
            // Check trend indicators
            foreach (var indicator in trendIndicators)
            {
                if (config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(indicator))
                    continue;
                    
                var signal = ExecuteStrategy(strategy, indicator);
                if (signal != null && signal.newSignal != FunctionResponses.NoAction)
                {
                    trendVotes++;
                    if (strongestTrend == null || signal.confidence > strongestTrend.confidence)
                        strongestTrend = signal;
                }
            }
            
            // STEP 2: Determine regime and get confirmation
            string regime = "NEUTRAL";
            patternFunctionResponse primarySignal = null;
            
            if (reversalVotes > trendVotes && strongestReversal != null)
            {
                regime = "REVERSAL";
                primarySignal = strongestReversal;
                strategy.Print($"[BRANCHING] REVERSAL regime detected ({reversalVotes} vs {trendVotes}), Signal: {primarySignal.newSignal}");
                
                // Get reversal confirmation - only need one strong confirmation
                var confirmationStrategies = new[] { "WICK_REVERSAL_CONFIRMATION" };
                int confirmations = 0;
                
                foreach (var confirmator in confirmationStrategies)
                {
                    if (config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(confirmator))
                        continue;
                        
                    var signal = ExecuteStrategy(strategy, confirmator);
                    if (signal != null && signal.newSignal == primarySignal.newSignal)
                    {
                        confirmations++;
                        strategy.Print($"[BRANCHING] Reversal confirmed by {confirmator}");
                    }
                }
                
                // Require at least 1 confirmation for reversal
                if (confirmations == 0)
                {
                    strategy.Print($"[BRANCHING] Reversal signal rejected - no confirmation");
                    return null;
                }
            }
            else if (trendVotes > reversalVotes && strongestTrend != null)
            {
                regime = "TREND";
                primarySignal = strongestTrend;
                strategy.Print($"[BRANCHING] TREND regime detected ({trendVotes} vs {reversalVotes}), Signal: {primarySignal.newSignal}");
                
                // Get trend confirmation from appropriate lag indicators
                var confirmationStrategies = new[] { "PRICE_BREAKOUT_CONFIRMATION", "EMA_CROSS_CONFIRMATION", "MACD_HISTOGRAM_ACCELERATION" };
                int confirmations = 0;
                
                foreach (var confirmator in confirmationStrategies)
                {
                    if (config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(confirmator))
                        continue;
                        
                    var signal = ExecuteStrategy(strategy, confirmator);
                    if (signal != null && signal.newSignal == primarySignal.newSignal)
                    {
                        confirmations++;
                        strategy.Print($"[BRANCHING] Trend confirmed by {confirmator}");
                    }
                }
                
                // Require at least 1 confirmation for trend
                if (confirmations == 0)
                {
                    strategy.Print($"[BRANCHING] Trend signal rejected - no confirmation");
                    return null;
                }
            }
            else
            {
                strategy.Print($"[BRANCHING] No clear regime - Reversal: {reversalVotes}, Trend: {trendVotes}");
                return null;
            }
            
            // Return the confirmed signal with regime information
            if (primarySignal != null)
            {
                primarySignal.signalDefinition = $"{regime}_{primarySignal.signalDefinition}";
                primarySignal.signalScore = primarySignal.confidence; // Use confidence as score
                strategy.Print($"[BRANCHING] Final signal: {primarySignal.newSignal}, Regime: {regime}, Confidence: {primarySignal.confidence:F2}");
            }
            
            return primarySignal;
        }
        
        /// <summary>
        /// Execute voting-based strategy system with Math/Lag segments
        /// </summary>
        public patternFunctionResponse ExecuteVotingSystem(Strategy strategy, StrategyConfig config = null)
        {
            InitializeStrategies();
            
            // Apply decay first
            ApplyStrengthDecay();
            
            // If no config provided, create empty config (no signal filtering)
            if (config == null)
            {
                config = new StrategyConfig();
            }
            
            // Collect Math segment signals by category
            var mathSignalsByCategory = new Dictionary<string, List<patternFunctionResponse>>();
            var mathStrategies = strategyAttributes
                .Where(kvp => kvp.Value.Segment == StrategySegment.Math && kvp.Value.IncludeInAll)
                .ToList();
            
            foreach (var kvp in mathStrategies)
            {
                // Skip signal if config restricts it (only filter if config has entry condition filtering)
                bool shouldSkip = config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(kvp.Key);
                if (shouldSkip)
                {
                   // strategy.Print($"⛔ SKIPPED Math: {kvp.Key} (not active in config)");
                    continue;
                }
                else
                {
                    //strategy.Print($"✅ PROCESSING Math: {kvp.Key}");
                }
                    
                var signal = ExecuteStrategy(strategy, kvp.Key);
                if (signal != null)
                {
                    var category = kvp.Value.Category;
                    if (!mathSignalsByCategory.ContainsKey(category))
                        mathSignalsByCategory[category] = new List<patternFunctionResponse>();
                    mathSignalsByCategory[category].Add(signal);
                }
            }
            
            // Count Math votes by category (one vote per category max)
            int mathBullVotes = 0;
            int mathBearVotes = 0;
            
            foreach (var category in mathSignalsByCategory)
            {
                // Take strongest signal in category
                var strongestSignal = category.Value.OrderByDescending(s => s.confidence).First();
                if (strongestSignal.newSignal == FunctionResponses.EnterLong)
                    mathBullVotes++;
                else if (strongestSignal.newSignal == FunctionResponses.EnterShort)
                    mathBearVotes++;
            }
            
            // Update Math strength based on agreement
            if (mathBullVotes >= 2)
            {
                MathStrengthBull += mathBullVotes * StrengthIncrement;
                strategy.Print($"[VOTING] Math Bull Strength increased: {MathStrengthBull:F2} (votes: {mathBullVotes})");
            }
            if (mathBearVotes >= 2)
            {
                MathStrengthBear += mathBearVotes * StrengthIncrement;
                strategy.Print($"[VOTING] Math Bear Strength increased: {MathStrengthBear:F2} (votes: {mathBearVotes})");
            }
            
            // Collect Lag segment signals by category
            var lagSignalsByCategory = new Dictionary<string, List<patternFunctionResponse>>();
            var lagStrategies = strategyAttributes
                .Where(kvp => kvp.Value.Segment == StrategySegment.Lag && kvp.Value.IncludeInAll)
                .ToList();
                
            // Debug removed - too verbose
            
            foreach (var kvp in lagStrategies)
            {
                // Skip signal if config restricts it (only filter if config has entry condition filtering)
                bool shouldSkip = config?.HasEntryConditionFiltering() == true && !config.IsEntryConditionActive(kvp.Key);
                if (shouldSkip)
                {
                    continue;
                }
                    
                var signal = ExecuteStrategy(strategy, kvp.Key);
                if (signal != null)
                {
                    strategy.Print($"[SIGNAL] {kvp.Key} returned signal: {signal.newSignal}, confidence: {signal.confidence:F2}");
                    var category = kvp.Value.Category;
                    if (!lagSignalsByCategory.ContainsKey(category))
                        lagSignalsByCategory[category] = new List<patternFunctionResponse>();
                    lagSignalsByCategory[category].Add(signal);
                }
            }
            
            // Process Lag signals with Math strength confirmation
            patternFunctionResponse finalSignal = null;
            double highestConfirmedScore = 0;
            
            // If no Lag signals but Math signals exist, use strongest Math signal directly
            if (lagSignalsByCategory.Count == 0 && mathSignalsByCategory.Count > 0)
            {
                strategy.Print($"[VOTING] No Lag signals available, using Math-only approach");
                
                // Find strongest Math signal across all categories
                patternFunctionResponse strongestMathSignal = null;
                double highestMathConfidence = 0;
                
                foreach (var category in mathSignalsByCategory)
                {
                    var strongest = category.Value.OrderByDescending(s => s.confidence).First();
                    if (strongest.confidence > highestMathConfidence)
                    {
                        highestMathConfidence = strongest.confidence;
                        strongestMathSignal = strongest;
                    }
                }
                
                if (strongestMathSignal != null)
                {
                    finalSignal = strongestMathSignal;
                    finalSignal.signalScore = strongestMathSignal.confidence;
                    finalSignal.signalDefinition = $"[MATH-ONLY] {strongestMathSignal.signalDefinition}";
                    strategy.Print($"[VOTING] Math-only signal: {finalSignal.signalDefinition}, Score: {finalSignal.signalScore:F2}");
                }
                
                return finalSignal;
            }
            
            foreach (var category in lagSignalsByCategory)
            {
                var strongestLagSignal = category.Value.OrderByDescending(s => s.confidence).First();
                
                // Check if Math strength confirms the Lag signal
                bool confirmed = false;
                double adjustedScore = 0;
                
                // Check if we have ANY Math signals at all
                bool hasMathSignals = mathSignalsByCategory.Count > 0;
                
                if (!hasMathSignals)
                {
                    // No Math signals available, use Lag signal directly
                    confirmed = true;
                    adjustedScore = strongestLagSignal.confidence;
                    strategy.Print($"[VOTING] Lag-only mode: No Math signals to confirm, using {strongestLagSignal.signalType} directly");
                }
                else if (strongestLagSignal.newSignal == FunctionResponses.EnterLong)
                {
                    if (MathStrengthBull > MathStrengthBear * SkewThreshold)
                    {
                        confirmed = true;
                        // Boost score based on Math strength
                        adjustedScore = strongestLagSignal.confidence * (1 + MathStrengthBull / 10);
                        strategy.Print($"[VOTING] LONG confirmed by Math skew: Bull={MathStrengthBull:F2} > Bear={MathStrengthBear:F2}*{SkewThreshold}");
                    }
                }
                else if (strongestLagSignal.newSignal == FunctionResponses.EnterShort)
                {
                    if (MathStrengthBear > MathStrengthBull * SkewThreshold)
                    {
                        confirmed = true;
                        // Boost score based on Math strength
                        adjustedScore = strongestLagSignal.confidence * (1 + MathStrengthBear / 10);
                        strategy.Print($"[VOTING] SHORT confirmed by Math skew: Bear={MathStrengthBear:F2} > Bull={MathStrengthBull:F2}*{SkewThreshold}");
                    }
                }
                
                if (confirmed && adjustedScore > highestConfirmedScore)
                {
                    finalSignal = strongestLagSignal;
                    finalSignal.signalScore = adjustedScore; // Use voting score instead of confidence
                    if (hasMathSignals)
                    {
                        finalSignal.signalDefinition = $"[VOTING] {finalSignal.signalDefinition} (Math-confirmed)";
                    }
                    else
                    {
                        finalSignal.signalDefinition = $"[LAG-ONLY] {finalSignal.signalDefinition}";
                    }
                    highestConfirmedScore = adjustedScore;
                }
            }
            
            return finalSignal;
        }

        // ============================================================================
        // DEBUG METHODS
        // ============================================================================
        
        public void TestConfigLoading(Strategy strategy, string configName)
        {
            var testConfig = new StrategyConfig(configName);
            strategy.Print($"🧪 Testing config '{configName}':");
            strategy.Print($"   Config exists: {testConfig != null}");
            strategy.Print($"   HasEntryConditionFiltering: {testConfig?.HasEntryConditionFiltering()}");
            
            if (testConfig?.EntryConditions != null)
            {
                strategy.Print($"   EntryConditions count: {testConfig.EntryConditions.Count}");
                foreach (var kvp in testConfig.EntryConditions)
                {
                    strategy.Print($"   {kvp.Key}: {kvp.Value}");
                }
            }
            else
            {
                strategy.Print("   EntryConditions is NULL");
            }
        }

        // ============================================================================
        // STRATEGY METHODS - Just add attribute, no registration needed!
        // ============================================================================

        [TradingStrategy("VOLUME_DIRECTION", 
            Description = "Simple volume direction detection",
            Category = "OrderFlow",
            Segment = StrategySegment.Math,
            Priority = 1,
            MinBars = 10,
            RegimeType = RegimeType.Trend,
            InitialStrength = 75,
            DecayRate = 0.92,
            DecayCondition = "Volume drops below 1.5x average")]
        public patternFunctionResponse VolumeDirection(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 10) return null;
                
                // Simple volume delta approximation
                double bullishVolume = 0, bearishVolume = 0;
                
                for (int i = 0; i < 5; i++) // Reduced from 10 to 5 bars
                {
                    if (strategy.Close[i] > strategy.Open[i])
                        bullishVolume += strategy.Volume[i];
                    else
                        bearishVolume += strategy.Volume[i];
                }
                
                double totalVolume = bullishVolume + bearishVolume;
                if (totalVolume == 0) return null;
                
                double volumeSkew = (bullishVolume - bearishVolume) / totalVolume;
                
                // Simple thresholds, no complex volume filters
                if (volumeSkew > 0.6) // Strong buying
                    return CreateSignal(strategy, "long", "VOLUME_DIRECTION", "Strong buying volume", 0.7);
                if (volumeSkew < -0.6) // Strong selling  
                    return CreateSignal(strategy, "short", "VOLUME_DIRECTION", "Strong selling volume", 0.7);
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Volume Direction: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("PRICE_MEAN_REVERSION", 
            Description = "Simple mean reversion using SMA and ATR",
            Category = "Statistical",
            Segment = StrategySegment.Math,
            Priority = 2,
            MinBars = 30,
            RegimeType = RegimeType.Reversal,
            InitialStrength = 60,
            DecayRate = 0.90,
            DecayCondition = "Price moves back to mean")]
        public patternFunctionResponse PriceMeanReversion(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 30) return null;
                
                // Use simple SMA instead of VWAP calculation
                double sma30 = strategy.SMA(30)[0];
                double atr = strategy.ATR(10)[0];
                
                // Simple mean reversion - no volume complexity
                if (strategy.Close[0] < sma30 - (atr * 2))
                    return CreateSignal(strategy, "long", "PRICE_MEAN_REVERSION", "Oversold vs SMA30", 0.7);
                if (strategy.Close[0] > sma30 + (atr * 2))
                    return CreateSignal(strategy, "short", "PRICE_MEAN_REVERSION", "Overbought vs SMA30", 0.7);
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Price Mean Reversion: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("PRICE_BREAKOUT", 
            Description = "Simple price breakouts of recent highs/lows",
            Category = "Momentum",
            Segment = StrategySegment.Math,
            Priority = 3,
            MinBars = 20,
            RegimeType = RegimeType.Trend,
            InitialStrength = 80,
            DecayRate = 0.95,
            DecayCondition = "Price retraces into range")]
        public patternFunctionResponse PriceBreakout(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 20) return null;
                
                // Simple 20-bar high/low breakout (like EMA logic)
                double recentHigh = strategy.MAX(strategy.High, 20)[1];
                double recentLow = strategy.MIN(strategy.Low, 20)[1];
                
                // Clean breakout logic - no momentum/volume filters
                if (strategy.Close[0] > recentHigh)
                    return CreateSignal(strategy, "long", "PRICE_BREAKOUT", "20-bar high break", 0.7);
                if (strategy.Close[0] < recentLow)
                    return CreateSignal(strategy, "short", "PRICE_BREAKOUT", "20-bar low break", 0.7);
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Price Breakout: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("RSI_EXTREME",
            Description = "Simple RSI extreme detection",
            Category = "Statistical",
            Segment = StrategySegment.Math,
            Priority = 4,
            MinBars = 14,
            RegimeType = RegimeType.Reversal,
            InitialStrength = 70,
            DecayRate = 0.92,
            DecayCondition = "RSI returns to neutral")]
        public patternFunctionResponse RSIExtreme(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 14) return null;
                
                double rsi = strategy.RSI(14, 1)[0];
                
                // Clean RSI extremes like EMA crossover
                if (rsi < 20)
                    return CreateSignal(strategy, "long", "RSI_EXTREME", "RSI oversold", 0.7);
                if (rsi > 80)
                    return CreateSignal(strategy, "short", "RSI_EXTREME", "RSI overbought", 0.7);
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in RSI Extreme: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("SIMPLE_MACD",
            Description = "Simple MACD line crossovers",
            Category = "Structure",
            Segment = StrategySegment.Math,
            Priority = 5,
            MinBars = 26,
            RegimeType = RegimeType.Trend,
            InitialStrength = 65,
            DecayRate = 0.93,
            DecayCondition = "MACD crosses back")]
        public patternFunctionResponse SimpleMacd(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 26) return null;
                
                var macd = strategy.MACD(12, 26, 9);
                
                // Clean MACD crossover like EMA logic
                if (strategy.CrossAbove(macd, macd.Avg, 1))
                    return CreateSignal(strategy, "long", "SIMPLE_MACD", "MACD bullish cross", 0.7);
                if (strategy.CrossBelow(macd, macd.Avg, 1))
                    return CreateSignal(strategy, "short", "SIMPLE_MACD", "MACD bearish cross", 0.7);
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Simple MACD: {ex.Message}");
            }
            
            return null;
        }

        // ============================================================================
        // Z-SCORE ENHANCED TRADITIONAL STRATEGIES
        // Combining proven technical analysis with statistical Z-Score filtering
        // ============================================================================

        [TradingStrategy("SPREAD_ADJUSTED_RSI",
            Description = "RSI adjusted for spread costs on 1-minute timeframe",
            Category = "Momentum",
            Segment = StrategySegment.Lag,
            Priority = 1,
            MinBars = 20,
            RegimeType = RegimeType.Reversal,
            InitialStrength = 55,
            DecayRate = 0.90,
            DecayCondition = "RSI normalizes")]
        public patternFunctionResponse SpreadAdjustedRSI(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 20) return null;
                
                // Use RSI(5) for 1-minute bars, not RSI(14)
                var rsi = strategy.RSI(5, 1)[0];
                
                // Calculate spread approximation (high-low as proxy)
                double avgRange = strategy.ATR(20)[0];
                double currentRange = strategy.High[0] - strategy.Low[0];
                double spreadFactor = currentRange / (avgRange + 0.00001);
                
                // Only trade when spread is reasonable (<1.5x average)
                if (spreadFactor > 1.5) return null;
                
                // Volume confirmation
                double volumeRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 20)[0];
                
                // Calculate momentum for direction confirmation
                double momentum = (strategy.Close[0] - strategy.Close[5]) / strategy.Close[5] * 100;
                
                // Extreme oversold with momentum turning positive
                if (rsi < 20 && momentum > -0.1 && volumeRatio > 1.2)
                {
                    // Check for bullish reversal pattern
                    if (strategy.Close[0] > strategy.Open[0] && strategy.Low[0] < strategy.Low[1])
                    {
                        double confidence = 0.7 + Math.Min(0.2, (20 - rsi) / 50);
                        return CreateSignal(strategy, "long", "SPREAD_ADJUSTED_RSI",
                            $"RSI({rsi:F0}) oversold reversal Spread:{spreadFactor:F1}x", confidence);
                    }
                }
                
                // Extreme overbought with momentum turning negative
                if (rsi > 80 && momentum < 0.1 && volumeRatio > 1.2)
                {
                    // Check for bearish reversal pattern
                    if (strategy.Close[0] < strategy.Open[0] && strategy.High[0] > strategy.High[1])
                    {
                        double confidence = 0.7 + Math.Min(0.2, (rsi - 80) / 50);
                        return CreateSignal(strategy, "short", "SPREAD_ADJUSTED_RSI",
                            $"RSI({rsi:F0}) overbought reversal Spread:{spreadFactor:F1}x", confidence);
                    }
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Spread Adjusted RSI: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("ATR_BAND_MOMENTUM",
            Description = "ATR-based dynamic bands with momentum confirmation",
            Category = "Volatility",
            Segment = StrategySegment.Lag,
            Priority = 2,
            MinBars = 30,
            RegimeType = RegimeType.Trend,
            InitialStrength = 60,
            DecayRate = 0.94,
            DecayCondition = "Price enters middle band")]
        public patternFunctionResponse ATRBandMomentum(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 30) return null;
                
                // Calculate dynamic bands using ATR
                double sma20 = strategy.SMA(20)[0];
                double atr = strategy.ATR(10)[0];
                
                // Dynamic bands based on volatility
                double upperBand = sma20 + (atr * 1.5);
                double lowerBand = sma20 - (atr * 1.5);
                
                // Current price and momentum
                double currentPrice = strategy.Close[0];
                double momentum = (currentPrice - strategy.Close[5]) / strategy.Close[5] * 100;
                
                // Volume analysis
                double volumeRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 20)[0];
                
                // Count consecutive bars outside bands
                int barsAboveBand = 0;
                int barsBelowBand = 0;
                
                for (int i = 0; i < 5; i++)
                {
                    if (strategy.Close[i] > sma20 + (atr * 1.5))
                        barsAboveBand++;
                    if (strategy.Close[i] < sma20 - (atr * 1.5))
                        barsBelowBand++;
                }
                
                // Momentum continuation at lower band
                if (currentPrice < lowerBand && 
                    barsBelowBand < 3 && // Not extended
                    momentum > -0.3 && // Not in freefall
                    volumeRatio > 1.1)
                {
                    double confidence = 0.7 + Math.Min(0.2, volumeRatio - 1.1);
                    return CreateSignal(strategy, "long", "ATR_BAND_MOMENTUM",
                        $"ATR band bounce Mom:{momentum:F1}%", confidence);
                }
                
                // Momentum continuation at upper band
                if (currentPrice > upperBand && 
                    barsAboveBand < 3 && // Not extended
                    momentum < 0.3 && // Not parabolic
                    volumeRatio > 1.1)
                {
                    double confidence = 0.7 + Math.Min(0.2, volumeRatio - 1.1);
                    return CreateSignal(strategy, "short", "ATR_BAND_MOMENTUM",
                        $"ATR band rejection Mom:{momentum:F1}%", confidence);
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in ATR Band Momentum: {ex.Message}");
            }
            
            return null;
        }
		
		 [TradingStrategy("simple_EMACross",
            Description = "EMA crossover with momentum decay",
            Category = "Trend",
            Segment = StrategySegment.Lag,
            Priority = 2,
            MinBars = 30,
            // NEW DECAY ATTRIBUTES
            RegimeType = RegimeType.Trend,
            InitialStrength = 100,      // Strong signal when crosses
            DecayRate = 0.93,            // 7% decay per bar
            DecayCondition = "EMA_Separation",  // Maintained by EMA separation
            StrengthCap = 150,           // Can grow to 150 if EMAs diverge
            ContradictionSignal = "opposite_cross",
            Direction = "both")]
       public patternFunctionResponse simple_EMACross(MainStrategy strategy)
		{
		    try
		    {
		        // Ensure enough bars for the largest EMA
		        if (strategy.CurrentBar < strategy.EMA4.Period) 
		            return null;
		
		        // Show EMA status every 500 bars to monitor
		        if (strategy.CurrentBar % 500 == 0)
		        {
		            strategy.Print($"[EMA] Bar {strategy.CurrentBar}: EMA3={strategy.EMA3[0]:F2}, EMA4={strategy.EMA4[0]:F2}, Diff={Math.Abs(strategy.EMA3[0] - strategy.EMA4[0]):F2}");
		        }
		
		        // Check for crossovers
		        if (strategy.CrossAbove(strategy.EMA3, strategy.EMA4, 1))
		        {
		            strategy.Print($"🟢 EMA CROSS LONG! EMA3: {strategy.EMA3[1]:F2} → {strategy.EMA3[0]:F2}, EMA4: {strategy.EMA4[1]:F2} → {strategy.EMA4[0]:F2}");
		            double confidence = 0.7;
		            var signal = CreateSignal(strategy, "long", "simple_EMACross", "EMA CROSS LONG", confidence);
		            strategy.Print($"✅ SIGNAL CREATED: {signal.newSignal}, Confidence: {signal.confidence}");
		            return signal;
		        }
		        else if (strategy.CrossBelow(strategy.EMA3, strategy.EMA4, 1))
		        {
		            strategy.Print($"🔴 EMA CROSS SHORT! EMA3: {strategy.EMA3[1]:F2} → {strategy.EMA3[0]:F2}, EMA4: {strategy.EMA4[1]:F2} → {strategy.EMA4[0]:F2}");
		            double confidence = 0.7;
		            var signal = CreateSignal(strategy, "short", "simple_EMACross", "EMA CROSS SHORT", confidence);
		            strategy.Print($"✅ SIGNAL CREATED: {signal.newSignal}, Confidence: {signal.confidence}");
		            return signal;
		        }
		
		        return null; // No crossover detected
		    }
		    catch (Exception ex)
		    {
		        strategy.Print("Error in simple_EMACross: " + ex.Message);
		        return null;
		    }
		}

		
		
        [TradingStrategy("WICK_TREND_PUSH",
            Description = "traceback and push",
            Category = "Trend",
            Segment = StrategySegment.Lag,
            Priority = 2,
            MinBars = 20,
            RegimeType = RegimeType.Trend,
            InitialStrength = 50,
            DecayRate = 0.95,
            DecayCondition = "price continues")]
        public patternFunctionResponse Three_Bar_Trend(MainStrategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 20) return null;
                
			
                 // Check for crossovers
		        if (strategy.High[0] == strategy.Close[0] && strategy.Low[0] == strategy.Open[0] && strategy.Body(0) > strategy.Body(1) && strategy.IsRising(strategy.EMA3))
		        {
		            double confidence = 0.7;
		            var signal = CreateSignal(strategy, "long", "WICK_TREND_PUSH", "WICK_TREND_PUSH LONG", confidence);
		            strategy.Print($"✅ SIGNAL CREATED: {signal.newSignal}, Confidence: {signal.confidence}");
		            return signal;
		        }
		        else if (strategy.High[0] == strategy.Open[0] && strategy.Low[0] == strategy.Close[0] && strategy.Body(0) > strategy.Body(1) && strategy.IsFalling(strategy.EMA3))
		        {
		            double confidence = 0.7;
		            var signal = CreateSignal(strategy, "short", "WICK_TREND_PUSH", "WICK_TREND_PUSH SHORT", confidence);
		            strategy.Print($"✅ SIGNAL CREATED: {signal.newSignal}, Confidence: {signal.confidence}");
		            return signal;
		        }
                
                return null;
            }
            catch (Exception ex)
            {
                strategy.Print($"Error in WICK_TREND_PUSH: {ex.Message}");
                return null;
            }
        }

        [TradingStrategy("RSI_EXTREME_CONFIRMATION",
            Description = "RSI extreme confirmation - signal persists without contradiction",
            Category = "Statistical",
            Segment = StrategySegment.Lag,
            Priority = 1,
            MinBars = 20,
            RegimeType = RegimeType.Reversal,
            InitialStrength = 50,
            DecayRate = 0.95,
            DecayCondition = "RSI returns to neutral")]
        public patternFunctionResponse RSI_EXTREME_CONFIRMATION(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 20) return null;
                
                int lookback = 10;
                var rsi = strategy.RSI(14, 1);
                
                // Track RSI extremes
                bool oversoldFound = false;
                bool overboughtFound = false;
                int oversoldBar = -1;
                int overboughtBar = -1;
                double extremeRSIValue = 0;
                
                // Scan for RSI extremes
                for (int i = 1; i <= lookback; i++)
                {
                    if (!oversoldFound && rsi[i] < 30)
                    {
                        oversoldFound = true;
                        oversoldBar = i;
                        extremeRSIValue = rsi[i];
                        
                        // Find the most extreme value in the oversold period
                        for (int j = i; j <= Math.Min(i + 3, lookback); j++)
                        {
                            if (rsi[j] < extremeRSIValue)
                                extremeRSIValue = rsi[j];
                        }
                    }
                    
                    if (!overboughtFound && rsi[i] > 70)
                    {
                        overboughtFound = true;
                        overboughtBar = i;
                        extremeRSIValue = rsi[i];
                        
                        // Find the most extreme value in the overbought period
                        for (int j = i; j <= Math.Min(i + 3, lookback); j++)
                        {
                            if (rsi[j] > extremeRSIValue)
                                extremeRSIValue = rsi[j];
                        }
                    }
                }
                
                // OVERSOLD CONFIRMATION (Bullish)
                if (oversoldFound && !overboughtFound)
                {
                    // Confirmations:
                    // 1. RSI has started recovering (above extreme low)
                    // 2. No overbought signal since oversold
                    // 3. Price showing signs of reversal
                    
                    bool rsiRecovering = rsi[0] > extremeRSIValue + 5; // At least 5 points recovery
                    bool noContradiction = (overboughtBar < 0 || overboughtBar > oversoldBar);
                    bool priceRecovering = strategy.Close[0] > strategy.MIN(strategy.Low, oversoldBar)[0];
                    bool notBackToExtreme = rsi[0] > 25; // Not falling back to extreme
                    
                    if (rsiRecovering && noContradiction && priceRecovering && notBackToExtreme)
                    {
                        double confidence = 0.65 + (0.25 * (1.0 - oversoldBar / (double)lookback));
                        confidence += Math.Min(0.1, (30 - extremeRSIValue) / 100); // More extreme = higher confidence
                        
                        strategy.Print($"[RSI_CONFIRM] OVERSOLD recovery confirmed - RSI was {extremeRSIValue:F0} {oversoldBar} bars ago, now {rsi[0]:F0}");
                        return CreateSignal(strategy, "long", "RSI_EXTREME_CONFIRMATION",
                            $"RSI oversold recovery ({extremeRSIValue:F0} → {rsi[0]:F0})", confidence);
                    }
                }
                
                // OVERBOUGHT CONFIRMATION (Bearish)
                if (overboughtFound && !oversoldFound)
                {
                    // Confirmations:
                    // 1. RSI has started declining (below extreme high)
                    // 2. No oversold signal since overbought
                    // 3. Price showing signs of reversal
                    
                    bool rsiDeclining = rsi[0] < extremeRSIValue - 5;
                    bool noContradiction = (oversoldBar < 0 || oversoldBar > overboughtBar);
                    bool priceDeclining = strategy.Close[0] < strategy.MAX(strategy.High, overboughtBar)[0];
                    bool notBackToExtreme = rsi[0] < 75;
                    
                    if (rsiDeclining && noContradiction && priceDeclining && notBackToExtreme)
                    {
                        double confidence = 0.65 + (0.25 * (1.0 - overboughtBar / (double)lookback));
                        confidence += Math.Min(0.1, (extremeRSIValue - 70) / 100);
                        
                        strategy.Print($"[RSI_CONFIRM] OVERBOUGHT reversal confirmed - RSI was {extremeRSIValue:F0} {overboughtBar} bars ago, now {rsi[0]:F0}");
                        return CreateSignal(strategy, "short", "RSI_EXTREME_CONFIRMATION",
                            $"RSI overbought reversal ({extremeRSIValue:F0} → {rsi[0]:F0})", confidence);
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                strategy.Print($"Error in RSI_EXTREME_CONFIRMATION: {ex.Message}");
                return null;
            }
        }

        [TradingStrategy("PRICE_BREAKOUT_CONFIRMATION",
            Description = "Price breakout confirmation - breakout holds without failure",
            Category = "Momentum",
            Segment = StrategySegment.Lag,
            Priority = 1,
            MinBars = 30,
            RegimeType = RegimeType.Trend,
            InitialStrength = 50,
            DecayRate = 0.95,
            DecayCondition = "Price retraces into range")]
        public patternFunctionResponse PRICE_BREAKOUT_CONFIRMATION(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 30) return null;
                
                int lookback = 10;
                int rangePeriod = 20;
                
                // Track breakouts
                bool upperBreakoutFound = false;
                bool lowerBreakoutFound = false;
                int upperBreakoutBar = -1;
                int lowerBreakoutBar = -1;
                double breakoutLevel = 0;
                
                // Scan for breakouts
                for (int i = 1; i <= lookback; i++)
                {
                    // Get the range before this bar
                    double rangeHigh = strategy.MAX(strategy.High, rangePeriod)[i + 1];
                    double rangeLow = strategy.MIN(strategy.Low, rangePeriod)[i + 1];
                    
                    // Upper breakout
                    if (!upperBreakoutFound && strategy.Close[i] > rangeHigh && strategy.Close[i - 1] <= rangeHigh)
                    {
                        upperBreakoutFound = true;
                        upperBreakoutBar = i;
                        breakoutLevel = rangeHigh;
                    }
                    
                    // Lower breakout
                    if (!lowerBreakoutFound && strategy.Close[i] < rangeLow && strategy.Close[i - 1] >= rangeLow)
                    {
                        lowerBreakoutFound = true;
                        lowerBreakoutBar = i;
                        breakoutLevel = rangeLow;
                    }
                }
                
                // UPPER BREAKOUT CONFIRMATION (Bullish)
                if (upperBreakoutFound && !lowerBreakoutFound)
                {
                    // Confirmations:
                    // 1. Price still above breakout level
                    // 2. No lower breakout since upper
                    // 3. Breakout level now acting as support
                    
                    bool stillAboveBreakout = strategy.Close[0] > breakoutLevel;
                    bool noContradiction = (lowerBreakoutBar < 0 || lowerBreakoutBar > upperBreakoutBar);
                    
                    // Check if we've tested and held the breakout level
                    bool testedAsSupport = false;
                    for (int i = 0; i < upperBreakoutBar; i++)
                    {
                        if (strategy.Low[i] <= breakoutLevel * 1.002 && strategy.Close[i] > breakoutLevel)
                        {
                            testedAsSupport = true;
                            break;
                        }
                    }
                    
                    if (stillAboveBreakout && noContradiction)
                    {
                        double confidence = 0.65 + (0.2 * (1.0 - upperBreakoutBar / (double)lookback));
                        if (testedAsSupport) confidence += 0.15; // Bonus for successful retest
                        
                        strategy.Print($"[BREAKOUT_CONFIRM] UPPER breakout holding - Level {breakoutLevel:F2} broken {upperBreakoutBar} bars ago");
                        return CreateSignal(strategy, "long", "PRICE_BREAKOUT_CONFIRMATION",
                            $"Upper breakout holding above {breakoutLevel:F2}", Math.Min(0.9, confidence));
                    }
                }
                
                // LOWER BREAKOUT CONFIRMATION (Bearish)
                if (lowerBreakoutFound && !upperBreakoutFound)
                {
                    // Confirmations:
                    // 1. Price still below breakout level
                    // 2. No upper breakout since lower
                    // 3. Breakout level now acting as resistance
                    
                    bool stillBelowBreakout = strategy.Close[0] < breakoutLevel;
                    bool noContradiction = (upperBreakoutBar < 0 || upperBreakoutBar > lowerBreakoutBar);
                    
                    // Check if we've tested and failed at the breakout level
                    bool testedAsResistance = false;
                    for (int i = 0; i < lowerBreakoutBar; i++)
                    {
                        if (strategy.High[i] >= breakoutLevel * 0.998 && strategy.Close[i] < breakoutLevel)
                        {
                            testedAsResistance = true;
                            break;
                        }
                    }
                    
                    if (stillBelowBreakout && noContradiction)
                    {
                        double confidence = 0.65 + (0.2 * (1.0 - lowerBreakoutBar / (double)lookback));
                        if (testedAsResistance) confidence += 0.15;
                        
                        strategy.Print($"[BREAKOUT_CONFIRM] LOWER breakout holding - Level {breakoutLevel:F2} broken {lowerBreakoutBar} bars ago");
                        return CreateSignal(strategy, "short", "PRICE_BREAKOUT_CONFIRMATION",
                            $"Lower breakout holding below {breakoutLevel:F2}", Math.Min(0.9, confidence));
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                strategy.Print($"Error in PRICE_BREAKOUT_CONFIRMATION: {ex.Message}");
                return null;
            }
        }

        [TradingStrategy("EMA_CROSS_CONFIRMATION",
            Description = "EMA cross confirmation looking back 20 bars for trend validation",
            Category = "Trend",
            Segment = StrategySegment.Lag,
            Priority = 2,
            MinBars = 30,
            RegimeType = RegimeType.Trend,
            InitialStrength = 50,
            DecayRate = 0.95,
            DecayCondition = "EMAs cross back")]
        public patternFunctionResponse EMA_CROSS_CONFIRMATION(MainStrategy strategy)
        {
            try
            {
                // Ensure enough bars for the largest EMA + lookback
                if (strategy.CurrentBar < strategy.EMA4.Period + 20) 
                    return null;

                // Check if there was a bullish cross in the last 20 bars
                bool bullishCrossExists = strategy.CrossAbove(strategy.EMA3, strategy.EMA4, 20);
                // Make sure there's no recent bearish cross that would negate it
                bool bearishCrossExists = strategy.CrossBelow(strategy.EMA3, strategy.EMA4, 20);
                
                // Additional trend strength check - EMAs should be properly separated
                double emaSeparation = Math.Abs(strategy.EMA3[0] - strategy.EMA4[0]);
                double atr = strategy.ATR(14)[0];
                double separationRatio = emaSeparation / (atr + 0.00001);
                
                // For LONG confirmation: Bullish cross exists, no recent bearish cross, EMAs properly separated
                if (bullishCrossExists && !bearishCrossExists && strategy.EMA3[0] > strategy.EMA4[0] && separationRatio > 0.5)
                {
                    // Find how many bars ago the cross occurred
                    int barsAgo = 0;
                    for (int i = 1; i <= 20; i++)
                    {
                        if (strategy.EMA3[i] <= strategy.EMA4[i] && strategy.EMA3[i-1] > strategy.EMA4[i-1])
                        {
                            barsAgo = i;
                            break;
                        }
                    }
                    
                    // Confidence based on how recent the cross was and current separation
                    double recencyFactor = 1.0 - (barsAgo / 20.0) * 0.3; // More recent = higher confidence
                    double confidence = 0.6 + (separationRatio * 0.2) * recencyFactor;
                    confidence = Math.Min(0.85, confidence);
                    
                    strategy.Print($"[EMA_CONFIRM] LONG confirmed - Cross {barsAgo} bars ago, Separation: {separationRatio:F2}x ATR");
                    return CreateSignal(strategy, "long", "EMA_CROSS_CONFIRMATION", 
                        $"EMA trend confirmed (cross {barsAgo} bars ago)", confidence);
                }
                // For SHORT confirmation: Bearish cross exists, no recent bullish cross, EMAs properly separated
                else if (bearishCrossExists && !bullishCrossExists && strategy.EMA3[0] < strategy.EMA4[0] && separationRatio > 0.5)
                {
                    // Find how many bars ago the cross occurred
                    int barsAgo = 0;
                    for (int i = 1; i <= 20; i++)
                    {
                        if (strategy.EMA3[i] >= strategy.EMA4[i] && strategy.EMA3[i-1] < strategy.EMA4[i-1])
                        {
                            barsAgo = i;
                            break;
                        }
                    }
                    
                    // Confidence based on how recent the cross was and current separation
                    double recencyFactor = 1.0 - (barsAgo / 20.0) * 0.3;
                    double confidence = 0.6 + (separationRatio * 0.2) * recencyFactor;
                    confidence = Math.Min(0.85, confidence);
                    
                    strategy.Print($"[EMA_CONFIRM] SHORT confirmed - Cross {barsAgo} bars ago, Separation: {separationRatio:F2}x ATR");
                    return CreateSignal(strategy, "short", "EMA_CROSS_CONFIRMATION", 
                        $"EMA trend confirmed (cross {barsAgo} bars ago)", confidence);
                }

                return null; // No clear trend confirmation
            }
            catch (Exception ex)
            {
                strategy.Print("Error in EMA_CROSS_CONFIRMATION: " + ex.Message);
                return null;
            }
        }

        [TradingStrategy("MACD_HISTOGRAM_ACCELERATION",
            Description = "MACD histogram acceleration/deceleration for 1-min",
            Category = "Trend",
            Segment = StrategySegment.Lag,
            Priority = 3,
            MinBars = 30,
            RegimeType = RegimeType.Trend,
            InitialStrength = 55,
            DecayRate = 0.93,
            DecayCondition = "Histogram decelerates")]
        public patternFunctionResponse MACDHistogramAcceleration(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 30) return null;
                
                // Use faster MACD for 1-minute (3, 10, 16)
                var macd = strategy.MACD(3, 10, 16);
                var macdHist = macd.Diff[0];
                var prevHist = macd.Diff[1];
                var prevPrevHist = macd.Diff[2];
                
                // Calculate histogram acceleration
                double histChange = macdHist - prevHist;
                double prevHistChange = prevHist - prevPrevHist;
                double acceleration = histChange - prevHistChange;
                
                // Volume confirmation
                double volumeRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 20)[0];
                
                // Price momentum
                double momentum = (strategy.Close[0] - strategy.Close[5]) / strategy.Close[5] * 100;
                
                // Bullish acceleration: Histogram accelerating upward from negative
                if (macdHist < 0 && // Still negative but improving
                    histChange > 0 && // Moving up
                    acceleration > 0 && // Accelerating
                    volumeRatio > 1.1 &&
                    momentum > -0.2) // Not in strong downtrend
                {
                    double confidence = 0.65 + Math.Min(0.2, acceleration * 100);
                    return CreateSignal(strategy, "long", "MACD_HISTOGRAM_ACCELERATION",
                        $"MACD accelerating up Accel:{acceleration:F4}", confidence);
                }
                
                // Bearish acceleration: Histogram accelerating downward from positive
                if (macdHist > 0 && // Still positive but weakening
                    histChange < 0 && // Moving down
                    acceleration < 0 && // Decelerating
                    volumeRatio > 1.1 &&
                    momentum < 0.2) // Not in strong uptrend
                {
                    double confidence = 0.65 + Math.Min(0.2, Math.Abs(acceleration) * 100);
                    return CreateSignal(strategy, "short", "MACD_HISTOGRAM_ACCELERATION",
                        $"MACD decelerating down Accel:{acceleration:F4}", confidence);
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in MACD Histogram Acceleration: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("RELATIVE_VOLUME_STRENGTH",
            Description = "Relative volume strength indicator for 1-min momentum",
            Category = "Volume", 
            Segment = StrategySegment.Lag,
            Priority = 4,
            MinBars = 30,
            RegimeType = RegimeType.Trend,
            InitialStrength = 60,
            DecayRate = 0.92,
            DecayCondition = "Volume normalizes")]
        public patternFunctionResponse RelativeVolumeStrength(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 30) return null;
                
                // Calculate relative volume over different periods
                double vol5 = 0, vol10 = 0, vol20 = 0;
                
                for (int i = 0; i < 20; i++)
                {
                    if (i < 5) vol5 += strategy.Volume[i];
                    if (i < 10) vol10 += strategy.Volume[i];
                    vol20 += strategy.Volume[i];
                }
                
                vol5 /= 5;
                vol10 /= 10;
                vol20 /= 20;
                
                // Calculate relative volume strength
                double rvs = (vol5 / vol20) * (vol5 / vol10);
                
                // Price action analysis
                double priceChange5 = (strategy.Close[0] - strategy.Close[5]) / strategy.Close[5] * 100;
                double priceChange10 = (strategy.Close[0] - strategy.Close[10]) / strategy.Close[10] * 100;
                
                // ATR for volatility context
                double atr = strategy.ATR(10)[0];
                double currentMove = Math.Abs(strategy.Close[0] - strategy.Open[0]);
                
                // Bullish volume surge with price confirmation
                if (rvs > 1.5 && // Strong relative volume
                    priceChange5 > 0.05 && // Positive momentum
                    priceChange10 > -0.1 && // Not in strong downtrend
                    currentMove > atr * 0.3 && // Significant move
                    strategy.Close[0] > strategy.Open[0]) // Bullish bar
                {
                    double confidence = 0.65 + Math.Min(0.25, (rvs - 1.5) * 0.3);
                    return CreateSignal(strategy, "long", "RELATIVE_VOLUME_STRENGTH",
                        $"Volume surge RVS:{rvs:F2} Price:+{priceChange5:F1}%", confidence);
                }
                
                // Bearish volume surge with price confirmation
                if (rvs > 1.5 && // Strong relative volume
                    priceChange5 < -0.05 && // Negative momentum
                    priceChange10 < 0.1 && // Not in strong uptrend
                    currentMove > atr * 0.3 && // Significant move
                    strategy.Close[0] < strategy.Open[0]) // Bearish bar
                {
                    double confidence = 0.65 + Math.Min(0.25, (rvs - 1.5) * 0.3);
                    return CreateSignal(strategy, "short", "RELATIVE_VOLUME_STRENGTH",
                        $"Volume surge RVS:{rvs:F2} Price:{priceChange5:F1}%", confidence);
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Relative Volume Strength: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("EMA_CROSSOVER_MOMENTUM",
            Description = "Fast EMA crossover with momentum filter for 1-min",
            Category = "Trend",
            Segment = StrategySegment.Lag,
            Priority = 5,
            MinBars = 20,
            RegimeType = RegimeType.Trend,
            InitialStrength = 65,
            DecayRate = 0.94,
            DecayCondition = "Momentum weakens")]
        public patternFunctionResponse EMACrossoverMomentum(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 20) return null;
                
                // Use fast EMAs for 1-minute: 5 and 10 period
                var ema5 = strategy.EMA(5)[0];
                var ema10 = strategy.EMA(10)[0];
                var prevEma5 = strategy.EMA(5)[1];
                var prevEma10 = strategy.EMA(10)[1];
                
                // Detect crossovers
                bool bullishCross = ema5 > ema10 && prevEma5 <= prevEma10;
                bool bearishCross = ema5 < ema10 && prevEma5 >= prevEma10;
                
                // Calculate momentum
                double momentum = (strategy.Close[0] - strategy.Close[5]) / strategy.Close[5] * 100;
                double atr = strategy.ATR(10)[0];
                
                // Volume confirmation
                double volumeRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 20)[0];
                
                // Distance from EMA (for entry quality)
                double distanceFromEma = Math.Abs(strategy.Close[0] - ema10) / atr;
                
                // Bullish crossover with momentum
                if (bullishCross && 
                    momentum > 0.05 && // Positive momentum
                    volumeRatio > 1.2 && // Volume confirmation
                    distanceFromEma < 2.0) // Not too extended
                {
                    double confidence = 0.7 + Math.Min(0.2, momentum * 0.5);
                    return CreateSignal(strategy, "long", "EMA_CROSSOVER_MOMENTUM",
                        $"EMA(5>10) cross Mom:{momentum:F2}% Vol:{volumeRatio:F1}x", confidence);
                }
                
                // Bearish crossover with momentum
                if (bearishCross && 
                    momentum < -0.05 && // Negative momentum
                    volumeRatio > 1.2 && // Volume confirmation
                    distanceFromEma < 2.0) // Not too extended
                {
                    double confidence = 0.7 + Math.Min(0.2, Math.Abs(momentum) * 0.5);
                    return CreateSignal(strategy, "short", "EMA_CROSSOVER_MOMENTUM",
                        $"EMA(5<10) cross Mom:{momentum:F2}% Vol:{volumeRatio:F1}x", confidence);
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in EMA Crossover Momentum: {ex.Message}");
            }
            
            return null;
        }

        // ============================================================================
        // SPECIAL STRATEGIES - Set IncludeInAll = false for selective inclusion
        // ============================================================================

        [TradingStrategy("TIME_SESSION_MOMENTUM",
            Description = "Time-of-day based momentum for specific sessions",
            Category = "Time",
            IncludeInAll = false,
            Priority = 1,
            MinBars = 20,
            RegimeType = RegimeType.Neutral,
            InitialStrength = 70,
            DecayRate = 0.90,
            DecayCondition = "Session ends")]
        public patternFunctionResponse TimeSessionMomentum(Strategy strategy)
        {
            try
            {
                var currentTime = strategy.Time[0].TimeOfDay;
                var hour = strategy.Time[0].Hour;
                var minute = strategy.Time[0].Minute;
                
                // Volume and momentum checks
                double volumeRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 20)[0];
                double momentum = (strategy.Close[0] - strategy.Close[5]) / strategy.Close[5] * 100;
                double atr = strategy.ATR(10)[0];
                
                // Market open momentum (9:30-10:00 EST)
                if (hour == 9 && minute >= 30 || (hour == 10 && minute == 0))
                {
                    // Check for opening drive
                    if (momentum > 0.1 && volumeRatio > 1.5)
                    {
                        double confidence = 0.7 + Math.Min(0.2, (volumeRatio - 1.5) * 0.2);
                        return CreateSignal(strategy, "long", "TIME_SESSION_MOMENTUM",
                            $"Open drive Mom:{momentum:F1}% Vol:{volumeRatio:F1}x", confidence);
                    }
                }
                
                // European close fade (11:00-11:30 EST)
                if (hour == 11 && minute <= 30)
                {
                    // Look for exhaustion after morning move
                    if (Math.Abs(momentum) > 0.2 && volumeRatio < 0.8)
                    {
                        string direction = momentum > 0 ? "short" : "long";
                        double confidence = 0.65 + Math.Min(0.15, (0.8 - volumeRatio) * 0.3);
                        return CreateSignal(strategy, direction, "TIME_SESSION_MOMENTUM",
                            $"EU close fade Mom:{momentum:F1}%", confidence);
                    }
                }
                
                // Power hour momentum (15:00-16:00 EST)
                if (hour == 15)
                {
                    // Trade with late day momentum
                    if (Math.Abs(momentum) > 0.15 && volumeRatio > 1.3)
                    {
                        string direction = momentum > 0 ? "long" : "short";
                        double confidence = 0.7 + Math.Min(0.2, (volumeRatio - 1.3) * 0.25);
                        return CreateSignal(strategy, direction, "TIME_SESSION_MOMENTUM",
                            $"Power hour Mom:{momentum:F1}% Vol:{volumeRatio:F1}x", confidence);
                    }
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Time Session Momentum: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("TICK_MOMENTUM_SCALP",
            Description = "Tick-based momentum scalping for 1-min bars",
            Category = "Scalping",
            Segment = StrategySegment.Math,
            IncludeInAll = true,
            MinBars = 10,
            RegimeType = RegimeType.Trend,
            InitialStrength = 75,
            DecayRate = 0.90,
            DecayCondition = "Momentum fades")]
        public patternFunctionResponse TickMomentumScalp(Strategy strategy)
        {
			strategy.Print("TickMomentumScalp");
            try
            {
                if (strategy.CurrentBar < 10) return null;
                
                // Calculate tick momentum (price change in ticks)
                double tickMove = (strategy.Close[0] - strategy.Close[1]) / strategy.TickSize;
                double tickMove2 = (strategy.Close[1] - strategy.Close[2]) / strategy.TickSize;
                double tickAcceleration = tickMove - tickMove2;
                
                // Calculate average and standard deviation of recent tick moves
                double[] recentMoves = new double[10];
                for (int i = 1; i <= 10; i++)
                {
                    recentMoves[i-1] = (strategy.Close[i-1] - strategy.Close[i]) / strategy.TickSize;
                }
                
                double avgTickMove = recentMoves.Average();
                double stdDev = Math.Sqrt(recentMoves.Select(x => Math.Pow(x - avgTickMove, 2)).Average());
                
                // Randomized threshold: 1 standard deviation above average
                double threshold = Randomize(stdDev, 15); // 15% variation
                
                // Volume analysis with randomization
                double volumeRatio = strategy.Volume[0] / strategy.SMA(strategy.Volume, 10)[0];
                double volThreshold = Randomize(1.2, 15); // 1.02-1.38
                
                // Range analysis
                double currentRange = (strategy.High[0] - strategy.Low[0]) / strategy.TickSize;
                double avgRange = 0;
                for (int i = 1; i <= 5; i++)
                    avgRange += (strategy.High[i] - strategy.Low[i]) / strategy.TickSize;
                avgRange /= 5;
                
                // Bullish tick momentum scalp
                if (tickMove > threshold && 
                    tickAcceleration > 0 && 
                    volumeRatio > volThreshold)
                {
                    double confidence = 0.65 + Math.Min(0.2, (tickMove / threshold) * 0.1);
                    return CreateSignal(strategy, "long", "TICK_MOMENTUM_SCALP",
                        $"Tick surge +{tickMove:F1} (>{threshold:F1}) Vol:{volumeRatio:F1}x", confidence);
                }
                
                // Bearish tick momentum scalp
                if (tickMove < -threshold && 
                    tickAcceleration < 0 && 
                    volumeRatio > volThreshold)
                {
                    double confidence = 0.65 + Math.Min(0.2, (Math.Abs(tickMove) / threshold) * 0.1);
                    return CreateSignal(strategy, "short", "TICK_MOMENTUM_SCALP",
                        $"Tick drop {tickMove:F1} (<-{threshold:F1}) Vol:{volumeRatio:F1}x", confidence);
                }
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Tick Momentum Scalp: {ex.Message}");
            }
            
            return null;
        }

        [TradingStrategy("WICK_TREND_REVERSAL", 
            Description = "Simple wick reversal at highs/lows",
            Category = "Reversal",
            Segment = StrategySegment.Math,
            Priority = 6,
            MinBars = 20,
            // NEW DECAY ATTRIBUTES
            RegimeType = RegimeType.Reversal,
            InitialStrength = 80,        // Moderate initial strength
            DecayRate = 0.90,            // 10% decay - reversals are time-sensitive
            DecayCondition = "Price_Respect",  // Maintained if price respects level
            StrengthCap = 100,
            AccumulatesStrength = false,
            Direction = "both")]
        public patternFunctionResponse WickTrendReversal(Strategy strategy)
        {
            try
            {
                if (strategy.CurrentBar < 20) return null;
                
                // Simple randomized thresholds
                double wickRatio = Randomize(2.0, 15); // 1.7-2.3
                
                // Current bar wick analysis
                double upperWick = strategy.High[0] - Math.Max(strategy.Open[0], strategy.Close[0]);
                double lowerWick = Math.Min(strategy.Open[0], strategy.Close[0]) - strategy.Low[0];
                double bodySize = Math.Abs(strategy.Close[0] - strategy.Open[0]);
                
                // Simple 10-bar high/low check
                double highest = strategy.High[0];
                double lowest = strategy.Low[0];
                for (int i = 1; i < 10; i++)
                {
                    highest = Math.Max(highest, strategy.High[i]);
                    lowest = Math.Min(lowest, strategy.Low[i]);
                }
                
                bool atHigh = strategy.High[0] >= highest;
                bool atLow = strategy.Low[0] <= lowest;
                
                // Simple wick conditions
                bool bigUpperWick = upperWick > bodySize * wickRatio;
                bool bigLowerWick = lowerWick > bodySize * wickRatio;
                
                // Long: Big lower wick at 10-bar low
                if (atLow && bigLowerWick)
                    return CreateSignal(strategy, "long", "WICK_TREND_REVERSAL", "Lower wick at low", 0.7);
                
                // Short: Big upper wick at 10-bar high  
                if (atHigh && bigUpperWick)
                    return CreateSignal(strategy, "short", "WICK_TREND_REVERSAL", "Upper wick at high", 0.7);
            }
            catch (Exception ex)
            {
                strategy.Print($"[IMPROVED] Error in Wick Trend Reversal: {ex.Message}");
            }
            
            return null;
        }
		
		

        // ============================================================================
        // CONFIG AUTO-SYNC METHODS
        // ============================================================================
        
        /// <summary>
        /// Gets all available strategy names from coded methods for config sync
        /// </summary>
        public List<string> GetAllAvailableStrategies()
        {
            InitializeStrategies();
            return strategyMethods.Keys.ToList();
        }
        
        /// <summary>
        /// Auto-corrects config file to include all available strategies
        /// Call this on NT startup to ensure configs are up-to-date
        /// </summary>
        public bool AutoCorrectConfig(string configPath)
        {
            try
            {
                var availableStrategies = GetAllAvailableStrategies();
                
                // Read existing config
                if (!File.Exists(configPath))
                {
                    // Create new config with all strategies enabled
                    CreateDefaultConfig(configPath, availableStrategies);
                    return true;
                }
                
                string jsonContent = File.ReadAllText(configPath);
                JObject config = JsonConvert.DeserializeObject<JObject>(jsonContent);
                
                // Ensure EntryConditions section exists
                if (config["EntryConditions"] == null)
                    config["EntryConditions"] = new JObject();
                
                JObject entryConditions = (JObject)config["EntryConditions"];
                bool configChanged = false;
                
                // Add missing strategies (default to false for safety)
                foreach (string strategy in availableStrategies)
                {
                    if (entryConditions[strategy] == null)
                    {
                        entryConditions[strategy] = false;
                        configChanged = true;
                    }
                }
                
                // Remove strategies that no longer exist in code
                var configStrategies = new List<string>();
                foreach (var prop in entryConditions)
                {
                    configStrategies.Add(prop.Key);
                }
                
                foreach (string configStrategy in configStrategies)
                {
                    if (!availableStrategies.Contains(configStrategy))
                    {
                        entryConditions.Remove(configStrategy);
                        configChanged = true;
                    }
                }
                
                // Save if changed
                if (configChanged)
                {
                    string updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                    File.WriteAllText(configPath, updatedJson);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                return false;
            }
        }
        
        /// <summary>
        /// Creates a default config file with all available strategies
        /// </summary>
        private void CreateDefaultConfig(string configPath, List<string> strategies)
        {
            var defaultConfig = new
            {
                TakeProfit = 30,
                StopLoss = 15,
                SoftTakeProfitMult = (object)null,
                PullBackExitEnabled = (object)null,
                TakeBigProfitEnabled = (object)null,
                EntryConditions = strategies.ToDictionary(s => s, s => false) // Default all to false
            };
            
            string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            File.WriteAllText(configPath, json);
        }
        
        /// <summary>
        /// Manual config sync for testing - call this to force sync all configs
        /// </summary>
        public string ForceConfigSync()
        {
            try
            {
                var availableStrategies = GetAllAvailableStrategies();
                string result = $"Available strategies found: {string.Join(", ", availableStrategies)}\n";
                
                string[] configFiles = {"PUMPKIN.json", "PUMPKIN_NQ.json", "PUMPKIN_NQ_200TICK.json", "PUMPKIN_NQ_500tick.json"};
                foreach (string configFile in configFiles)
                {
                    string configPath = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "strategies", "OrganizedStrategy", "Configs", configFile);
                    bool wasUpdated = AutoCorrectConfig(configPath);
                    result += $"{configFile}: {(wasUpdated ? "UPDATED" : "OK")}\n";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        // ============================================================================
        // HELPER METHODS
        // ============================================================================

        private double Randomize(double value, double variationPercent = 0.1)
        {
            double variation = value * variationPercent;
            return value + (random.NextDouble() * 2 - 1) * variation;
        }

        private patternFunctionResponse CreateSignal(Strategy strategy, string direction, 
            string signalType, string reason, double confidence)
        {
            return new patternFunctionResponse
            {
                newSignal = direction == "long" ? FunctionResponses.EnterLong : FunctionResponses.EnterShort,
                signalType = signalType,
                signalDefinition = reason,
                confidence = confidence,
                signalScore = confidence, // Set both confidence and signalScore to the same value
                recStop = 15, // Default stop loss
                recTarget = 30, // Default take profit  
                maxStopLoss = 25, // Max for Risk Agent
                maxTakeProfit = 50 // Max for Risk Agent
            };
        }
        
        // Overload for backward compatibility
        private patternFunctionResponse CreateSignal(Strategy strategy, string direction, 
            string signalType, string reason, double confidence, double baseConfidence)
        {
            return CreateSignal(strategy, direction, signalType, reason, confidence);
        }

        private patternFunctionResponse ApplyConsensusLogic(Strategy strategy, 
            List<patternFunctionResponse> signals, double threshold)
        {
            if (signals.Count == 0) return null;
            
            // Group by direction
            var longSignals = signals.Where(s => s.newSignal == FunctionResponses.EnterLong).ToList();
            var shortSignals = signals.Where(s => s.newSignal == FunctionResponses.EnterShort).ToList();
            
            // Calculate consensus
            double longRatio = (double)longSignals.Count / signals.Count;
            double shortRatio = (double)shortSignals.Count / signals.Count;
            
            // Return strongest consensus if above threshold
            if (longRatio >= threshold && longRatio > shortRatio)
            {
                var bestLong = longSignals.OrderByDescending(s => s.confidence).First();
                bestLong.signalDefinition = $"Consensus LONG ({longSignals.Count}/{signals.Count} strategies)";
                return bestLong;
            }
            else if (shortRatio >= threshold)
            {
                var bestShort = shortSignals.OrderByDescending(s => s.confidence).First();
                bestShort.signalDefinition = $"Consensus SHORT ({shortSignals.Count}/{signals.Count} strategies)";
                return bestShort;
            }
            
            return null;
        }

        /// <summary>
        /// Get strategy information for debugging/display
        /// </summary>
        public string GetStrategyInfo()
        {
            InitializeStrategies();
            
            var info = "=== IMPROVED TRADITIONAL STRATEGIES ===\n";
            info += $"Total Strategies: {strategyMethods.Count}\n\n";
            
            var byCategory = strategyAttributes.GroupBy(kvp => kvp.Value.Category);
            foreach (var category in byCategory.OrderBy(g => g.Key))
            {
                info += $"[{category.Key}]\n";
                foreach (var strategy in category.OrderBy(kvp => kvp.Value.Priority))
                {
                    var attr = strategy.Value;
                    info += $"  • {strategy.Key}: {attr.Description}";
                    info += $" (Priority: {attr.Priority}, MinBars: {attr.MinBars}";
                    info += attr.IncludeInAll ? ", In ALL)" : ", Selective)";
                    info += "\n";
                }
                info += "\n";
            }
            
            return info;
        }
    }

    /// <summary>
    /// Usage example from your main strategy
    /// </summary>
    public class ExampleUsage
    {
        private ImprovedTraditionalStrategies strategies = new ImprovedTraditionalStrategies();
        
        public void OnBarUpdate(Strategy strategy)
        {
            // PRIMARY METHOD: Use the voting system
            var votingSignal = strategies.ExecuteVotingSystem(strategy);
            if (votingSignal != null)
            {
                // Use votingSignal.signalScore instead of confidence
                strategy.Print($"[VOTING] Signal fired: {votingSignal.signalDefinition} Score: {votingSignal.signalScore:F2}");
            }
            
            // Alternative: Execute all strategies with consensus
            var signal = strategies.ExecuteAll(strategy, 0.3);
            
            // Or execute specific strategy
            var timeSignal = strategies.ExecuteStrategy(strategy, "TIME_SESSION_MOMENTUM");
            
            // Or execute by segment
            var mathSignals = strategies.ExecuteFiltered(strategy, 
                attr => attr.Segment == StrategySegment.Math);
            
            var lagSignals = strategies.ExecuteFiltered(strategy,
                attr => attr.Segment == StrategySegment.Lag);
            
            // Get available strategies
            var allStrategies = strategies.GetAvailableStrategies();
            var momentumStrategies = strategies.GetStrategiesByCategory("Momentum");
            
            // Debug info
            strategy.Print(strategies.GetStrategyInfo());
        }
        
        // ========== SIGNAL DISCOVERY FOR CONFIG SYSTEM ==========
        
        /// <summary>
        /// Discovers all available signals from this class (including partial classes)
        /// Used by Electron app to build configuration interface
        /// </summary>
        public string GetSignalCatalog()
        {
            try
            {
                var signals = new List<object>();
                
                // Use reflection to find all methods with TradingStrategy attribute
                // This includes methods from partial classes automatically
                var methods = this.GetType().GetMethods()
                    .Where(m => m.GetCustomAttribute<TradingStrategyAttribute>() != null);
                
                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<TradingStrategyAttribute>();
                    
                    signals.Add(new
                    {
                        name = attr.Name,
                        methodName = method.Name,
                        description = attr.Description ?? "",
                        category = attr.Category ?? "General",
                        segment = attr.Segment.ToString(),
                        minBars = attr.MinBars,
                        priority = attr.Priority,
                        includeInAll = attr.IncludeInAll,
                        enabled = false, // Default for new configs
                        // NEW DECAY PROPERTIES
                        decaySystem = new
                        {
                            regimeType = attr.RegimeType.ToString(),
                            initialStrength = attr.InitialStrength,
                            decayRate = attr.DecayRate,
                            decayPercentPerBar = (1 - attr.DecayRate) * 100,  // e.g., 0.95 = 5% decay
                            decayCondition = attr.DecayCondition ?? "none",
                            strengthCap = attr.StrengthCap,
                            contradictionSignal = attr.ContradictionSignal ?? "",
                            accumulates = attr.AccumulatesStrength,
                            direction = attr.Direction ?? "both",
                            halfLifeBars = attr.DecayRate > 0 ? Math.Log(0.5) / Math.Log(attr.DecayRate) : 0  // Bars until 50% strength
                        }
                    });
                }
                
                var catalog = new
                {
                    version = "2.0",
                    timestamp = DateTime.Now,
                    signalCount = signals.Count,
                    className = this.GetType().Name,
                    signals = signals.OrderBy(s => ((dynamic)s).category).ThenBy(s => ((dynamic)s).name)
                };
                
                return Newtonsoft.Json.JsonConvert.SerializeObject(catalog, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"Failed to generate signal catalog: {ex.Message}\"}}";
            }
        }
        
        /// <summary>
        /// Applies a configuration to enable/disable signals
        /// </summary>
        public void ApplySignalConfig(Dictionary<string, bool> signalConfig)
        {
            // Store the signal configuration for use in voting system
            foreach (var kvp in signalConfig)
            {
                // This could be used to filter signals during execution
                // For now, we'll rely on the existing config system
                // but this provides a hook for dynamic signal enabling/disabling
            }
        }
    }
}