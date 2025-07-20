using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    public class SignalApprovalClient
    {
        private readonly HttpClient httpClient;
        private readonly string baseUrl;
        
        public SignalApprovalClient(string signalApprovalServiceUrl = "http://localhost:3017")
        {
            this.baseUrl = signalApprovalServiceUrl;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(5); // Quick timeout for trading
        }
        
        /// <summary>
        /// Check if a signal should be approved for trading
        /// </summary>
        public async Task<SignalApprovalResponse> ApproveSignalAsync(string signalType, object signalFeatures, string patternId, string instrument, string direction = "long", double entryPrice = 0.0)
        {
            try
            {
                // ONLY send basic signal info - ME will provide features
                var request = new
                {
                    instrument = instrument,
                    direction = direction,
                    entry_price = entryPrice,
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    // NO FEATURES - ME provides them
                };
                
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await httpClient.PostAsync($"{baseUrl}/api/approve-signal", content);
                var responseJson = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    return JsonConvert.DeserializeObject<SignalApprovalResponse>(responseJson);
                }
                else
                {
                    return new SignalApprovalResponse
                    {
                        Approved = false,
                        Confidence = 0.0,
                        Reason = $"HTTP Error: {response.StatusCode} - {responseJson}",
                        SignalType = signalType,
                        PatternId = patternId,
                        Instrument = instrument,
                        Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    };
                }
            }
            catch (Exception ex)
            {
                return new SignalApprovalResponse
                {
                    Approved = false, // Fail safe - reject on error
                    Confidence = 0.0,
                    Reason = $"Client Error: {ex.Message}",
                    SignalType = signalType,
                    PatternId = patternId,
                    Instrument = instrument,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
            }
        }
        
        /// <summary>
        /// Synchronous version for use in NinjaTrader event handlers
        /// </summary>
        public SignalApprovalResponse ApproveSignal(string signalType, object signalFeatures, string patternId, string instrument)
        {
            try
            {
                return ApproveSignalAsync(signalType, signalFeatures, patternId, instrument).Result;
            }
            catch (Exception ex)
            {
                return new SignalApprovalResponse
                {
                    Approved = false,
                    Confidence = 0.0,
                    Reason = $"Sync Error: {ex.Message}",
                    SignalType = signalType,
                    PatternId = patternId,
                    Instrument = instrument,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
            }
        }
        
        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
    
    public class SignalApprovalResponse
    {
        [JsonProperty("approved")]
        public bool Approved { get; set; }
        
        [JsonProperty("confidence")]
        public double Confidence { get; set; }
        
        [JsonProperty("reason")]
        public string Reason { get; set; }
        
        [JsonProperty("signalType")]
        public string SignalType { get; set; }
        
        [JsonProperty("patternId")]
        public string PatternId { get; set; }
        
        [JsonProperty("instrument")]
        public string Instrument { get; set; }
        
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
        
        [JsonProperty("threshold")]
        public double Threshold { get; set; }
        
        [JsonProperty("cachedId")]
        public string CachedId { get; set; }
        
        [JsonProperty("source")]
        public string Source { get; set; }
        
        [JsonProperty("suggested_tp")]
        public double SuggestedTP { get; set; }
        
        [JsonProperty("suggested_sl")]
        public double SuggestedSL { get; set; }
    }
}