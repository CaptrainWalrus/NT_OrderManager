using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

namespace NinjaTrader.NinjaScript.Strategies
{
	/// <summary>
	/// Training data exporter using ConfigManager patterns for file I/O
	/// </summary>
	public class TrainingDataExporter
	{
		private const string TRAINING_DATA_DIRECTORY = "bin\\Custom\\OrderManager\\TrainingData";
		private const string FILTERED_SIGNALS_DIRECTORY = "bin\\Custom\\OrderManager\\TrainingData\\FilteredSignals";
		
		// In-memory storage for backtest mode
		private Dictionary<string, List<PositionOutcome>> backtestOutcomes = new Dictionary<string, List<PositionOutcome>>();
		private bool isBacktestMode = false;
		
		/// <summary>
		/// Enable backtest mode - collect outcomes in memory and write at end
		/// </summary>
		public void EnableBacktestMode()
		{
			isBacktestMode = true;
			backtestOutcomes.Clear();
			Console.WriteLine("[TRAINING-DEBUG] Backtest mode enabled - outcomes will be stored in memory");
		}
		
		/// <summary>
		/// Disable backtest mode and flush all collected outcomes to files
		/// </summary>
		public void FlushBacktestData()
		{
			try
			{
				Console.WriteLine($"[TRAINING-DEBUG] Flushing {backtestOutcomes.Count} signal types to files");
				
				foreach (var kvp in backtestOutcomes)
				{
					string signalType = kvp.Key;
					var outcomes = kvp.Value;
					
					if (outcomes.Count > 0)
					{
						string fileName = $"{signalType}_{DateTime.Now:yyyyMMdd}.json";
						string filePath = GetTrainingDataFilePath(fileName);
						
						// Load existing data and merge
						var existingData = LoadExistingOutcomes(filePath);
						existingData.AddRange(outcomes);
						
						var exportData = new
						{
							signal_type = signalType,
							last_updated = DateTime.UtcNow,
							total_outcomes = existingData.Count,
							outcomes = existingData
						};
						
						string json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
						
						// Ensure directory exists
						string directory = Path.GetDirectoryName(filePath);
						if (!Directory.Exists(directory))
						{
							Directory.CreateDirectory(directory);
						}
						
						File.WriteAllText(filePath, json);
						Console.WriteLine($"[TRAINING-DEBUG] Wrote {outcomes.Count} outcomes for {signalType} to {filePath}");
					}
				}
				
				isBacktestMode = false;
				backtestOutcomes.Clear();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[TRAINING-DEBUG] Error flushing backtest data: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Export position outcome data for training
		/// </summary>
		public void ExportPositionOutcome(PositionOutcome outcome)
		{
			try
			{
				if (isBacktestMode)
				{
					// Store in memory for backtest mode
					if (!backtestOutcomes.ContainsKey(outcome.SignalType))
					{
						backtestOutcomes[outcome.SignalType] = new List<PositionOutcome>();
					}
					backtestOutcomes[outcome.SignalType].Add(outcome);
					Console.WriteLine($"[TRAINING-DEBUG] Stored outcome in memory: {outcome.SignalType} - P&L: {outcome.RealizedPnL:F2} (Total: {backtestOutcomes[outcome.SignalType].Count})");
					return;
				}
				
				// Original immediate write logic for live trading
				string fileName = $"{outcome.SignalType}_{DateTime.Now:yyyyMMdd}.json";
				string filePath = GetTrainingDataFilePath(fileName);
				
				// DEBUG: Print file path and outcome info
				Console.WriteLine($"[TRAINING-DEBUG] Exporting to: {filePath}");
				Console.WriteLine($"[TRAINING-DEBUG] Signal Type: {outcome.SignalType}");
				Console.WriteLine($"[TRAINING-DEBUG] PnL: {outcome.RealizedPnL}");
				
				// Load existing data or create new
				var existingData = LoadExistingOutcomes(filePath);
				existingData.Add(outcome);
				
				// Use same serialization as ConfigManager
				var exportData = new
				{
					signal_type = outcome.SignalType,
					last_updated = DateTime.UtcNow,
					total_outcomes = existingData.Count,
					outcomes = existingData
				};
				
				string json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
				
				// Ensure directory exists
				string directory = Path.GetDirectoryName(filePath);
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
					Console.WriteLine($"[TRAINING-DEBUG] Created directory: {directory}");
				}
				
				File.WriteAllText(filePath, json);
				Console.WriteLine($"[TRAINING-DEBUG] Successfully wrote {existingData.Count} outcomes to file");
				
				NinjaTrader.Code.Output.Process($"[TRAINING] Exported position outcome for {outcome.SignalType} to {filePath}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error exporting position outcome: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
		}
		
		/// <summary>
		/// Export backtest parameters for training context
		/// </summary>
		public void ExportBacktestParameters(string backtestId, Dictionary<string, object> parameters)
		{
			try
			{
				string fileName = $"backtest_parameters_{backtestId}.json";
				string filePath = GetTrainingDataFilePath(fileName);
				
				var backtestData = new
				{
					backtest_id = backtestId,
					created_timestamp = DateTime.UtcNow,
					parameters = parameters
				};
				
				string json = JsonConvert.SerializeObject(backtestData, Formatting.Indented);
				
				// Ensure directory exists
				string directory = Path.GetDirectoryName(filePath);
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				
				File.WriteAllText(filePath, json);
				
				NinjaTrader.Code.Output.Process($"[TRAINING] Exported backtest parameters for {backtestId}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error exporting backtest parameters: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
		}
		
		/// <summary>
		/// Log filtered signal for analysis
		/// </summary>
		public void LogFilteredSignal(string logData)
		{
			try
			{
				string fileName = $"filtered_signals_{DateTime.Now:yyyyMMdd}.json";
				string filePath = GetFilteredSignalsFilePath(fileName);
				
				// Load existing logs or create new
				var existingLogs = LoadExistingFilteredSignals(filePath);
				
				// Parse the log data to add to collection
				var logEntry = JsonConvert.DeserializeObject(logData);
				existingLogs.Add(logEntry);
				
				var exportData = new
				{
					date = DateTime.Now.ToString("yyyy-MM-dd"),
					last_updated = DateTime.UtcNow,
					total_filtered_signals = existingLogs.Count,
					filtered_signals = existingLogs
				};
				
				string json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
				
				// Ensure directory exists
				string directory = Path.GetDirectoryName(filePath);
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				
				File.WriteAllText(filePath, json);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error logging filtered signal: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
		}
		
		/// <summary>
		/// Load signal type data for analysis
		/// </summary>
		public List<PositionOutcome> LoadSignalTypeData(string signalType, DateTime? fromDate = null)
		{
			var allOutcomes = new List<PositionOutcome>();
			
			try
			{
				var files = GetTrainingDataFiles(signalType);
				
				foreach (var file in files)
				{
					var fileOutcomes = LoadExistingOutcomes(file);
					
					if (fromDate.HasValue)
					{
						fileOutcomes = fileOutcomes.Where(o => o.EntryTime >= fromDate.Value).ToList();
					}
					
					allOutcomes.AddRange(fileOutcomes);
				}
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error loading signal type data: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
			
			return allOutcomes;
		}
		
		/// <summary>
		/// Generate training data summary
		/// </summary>
		public TrainingDataSummary GenerateSummary(string signalType)
		{
			try
			{
				var outcomes = LoadSignalTypeData(signalType);
				
				return new TrainingDataSummary
				{
					SignalType = signalType,
					TotalOutcomes = outcomes.Count,
					WinCount = outcomes.Count(o => o.WinLoss == 1),
					LossCount = outcomes.Count(o => o.WinLoss == 0),
					TotalPnL = outcomes.Sum(o => o.RealizedPnL),
					AveragePnL = outcomes.Count > 0 ? outcomes.Average(o => o.RealizedPnL) : 0,
					WinRate = outcomes.Count > 0 ? (double)outcomes.Count(o => o.WinLoss == 1) / outcomes.Count : 0,
					DateRange = new { 
						From = outcomes.Count > 0 ? outcomes.Min(o => o.EntryTime) : DateTime.MinValue,
						To = outcomes.Count > 0 ? outcomes.Max(o => o.EntryTime) : DateTime.MinValue
					}
				};
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error generating summary: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
				return new TrainingDataSummary { SignalType = signalType };
			}
		}
		
		/// <summary>
		/// Get training data file path using ConfigManager pattern
		/// </summary>
		private string GetTrainingDataFilePath(string fileName)
		{
			// Follow ConfigManager pattern
			string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			string ninjaTraderPath = Path.Combine(documentsFolder, "NinjaTrader 8");
			return Path.Combine(ninjaTraderPath, TRAINING_DATA_DIRECTORY, fileName);
		}
		
		/// <summary>
		/// Get filtered signals file path
		/// </summary>
		private string GetFilteredSignalsFilePath(string fileName)
		{
			string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			string ninjaTraderPath = Path.Combine(documentsFolder, "NinjaTrader 8");
			return Path.Combine(ninjaTraderPath, FILTERED_SIGNALS_DIRECTORY, fileName);
		}
		
		/// <summary>
		/// Load existing training data from file
		/// </summary>
		private List<PositionOutcome> LoadExistingOutcomes(string filePath)
		{
			try
			{
				if (!File.Exists(filePath))
					return new List<PositionOutcome>();
					
				string json = File.ReadAllText(filePath);
				var data = JsonConvert.DeserializeObject<dynamic>(json);
				
				if (data?.outcomes != null)
				{
					return JsonConvert.DeserializeObject<List<PositionOutcome>>(data.outcomes.ToString());
				}
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error loading existing outcomes: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
			
			return new List<PositionOutcome>();
		}
		
		/// <summary>
		/// Load existing filtered signals from file
		/// </summary>
		private List<object> LoadExistingFilteredSignals(string filePath)
		{
			try
			{
				if (!File.Exists(filePath))
					return new List<object>();
					
				string json = File.ReadAllText(filePath);
				var data = JsonConvert.DeserializeObject<dynamic>(json);
				
				if (data?.filtered_signals != null)
				{
					return JsonConvert.DeserializeObject<List<object>>(data.filtered_signals.ToString());
				}
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error loading existing filtered signals: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
			}
			
			return new List<object>();
		}
		
		/// <summary>
		/// Get training data files for a specific signal type
		/// </summary>
		private string[] GetTrainingDataFiles(string signalType)
		{
			try
			{
				string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				string ninjaTraderPath = Path.Combine(documentsFolder, "NinjaTrader 8");
				string directoryPath = Path.Combine(ninjaTraderPath, TRAINING_DATA_DIRECTORY);
				
				if (!Directory.Exists(directoryPath))
					return new string[0];
					
				return Directory.GetFiles(directoryPath, $"{signalType}_*.json");
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[TRAINING] Error getting training data files: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
				return new string[0];
			}
		}
	}
	
	/// <summary>
	/// Training data summary class
	/// </summary>
	public class TrainingDataSummary
	{
		public string SignalType { get; set; }
		public int TotalOutcomes { get; set; }
		public int WinCount { get; set; }
		public int LossCount { get; set; }
		public double TotalPnL { get; set; }
		public double AveragePnL { get; set; }
		public double WinRate { get; set; }
		public object DateRange { get; set; }
	}
}