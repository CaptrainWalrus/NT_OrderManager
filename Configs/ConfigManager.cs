using System;
using System.IO;
using Newtonsoft.Json;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Centralized configuration manager for the OrderManager system
    /// </summary>
    public class ConfigManager
    {
        // Singleton instance
        private static ConfigManager _instance;
        
        // Configuration instances
        private CurvesV2Config _curvesV2Config;
        
        // Configuration file paths
        private const string CONFIG_DIRECTORY = "Custom\\OrderManager\\Configs";
        private const string CURVES_V2_CONFIG_FILE = "curves_v2_config.json";
        
        // Default ports
        public const int DEFAULT_MAIN_SERVER_PORT = 3001;
        public const int DEFAULT_SIGNAL_SERVER_PORT = 3000;
        
        // Default endpoints
        public const string API_SIGNALS_ENDPOINT = "/api/signals";
        public const string API_REALTIME_BARS_ENDPOINT = "/api/realtime_bars";
        
        // Private constructor for singleton
        private ConfigManager()
        {
            // Initialize with default configurations
            _curvesV2Config = new CurvesV2Config();
            LoadConfigurations();
        }
        
        /// <summary>
        /// Get the singleton instance of the ConfigManager
        /// </summary>
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigManager();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Get CurvesV2 configuration
        /// </summary>
        public CurvesV2Config CurvesV2Config
        {
            get { return _curvesV2Config; }
        }
        
        /// <summary>
        /// Load all configurations from files
        /// </summary>
        public void LoadConfigurations()
        {
            try
            {
                LoadCurvesV2Config();
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"Error loading configurations: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
            }
        }
        
        /// <summary>
        /// Save all configurations to files
        /// </summary>
        public void SaveConfigurations()
        {
            try
            {
                SaveCurvesV2Config();
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"Error saving configurations: {ex.Message}", NinjaTrader.NinjaScript.PrintTo.OutputTab1);
            }
        }
        
        /// <summary>
        /// Load CurvesV2 configuration from file
        /// </summary>
        private void LoadCurvesV2Config()
        {
            string configPath = GetConfigFilePath(CURVES_V2_CONFIG_FILE);
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    _curvesV2Config = JsonConvert.DeserializeObject<CurvesV2Config>(json) ?? new CurvesV2Config();
                }
                catch
                {
                    // If loading fails, use default config
                    _curvesV2Config = new CurvesV2Config();
                }
            }
        }
        
        /// <summary>
        /// Save CurvesV2 configuration to file
        /// </summary>
        private void SaveCurvesV2Config()
        {
            string configPath = GetConfigFilePath(CURVES_V2_CONFIG_FILE);
            string directory = Path.GetDirectoryName(configPath);
            
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            string json = JsonConvert.SerializeObject(_curvesV2Config, Formatting.Indented);
            File.WriteAllText(configPath, json);
        }
        
        /// <summary>
        /// Get full path for a configuration file
        /// </summary>
        private string GetConfigFilePath(string fileName)
        {
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string ninjaTraderPath = Path.Combine(documentsFolder, "NinjaTrader 8");
            return Path.Combine(ninjaTraderPath, CONFIG_DIRECTORY, fileName);
        }
    }
} 