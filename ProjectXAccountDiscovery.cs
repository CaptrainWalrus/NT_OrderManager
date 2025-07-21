#region Using declarations
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaTrader.Code;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// ProjectX Account Discovery Utility
    /// Simple static class to authenticate and discover account IDs
    /// </summary>
    public static class ProjectXAccountDiscovery
    {
        private static readonly string baseUrl = "https://api.blusky.projectx.com";

        /// <summary>
        /// Run account discovery with provided credentials
        /// </summary>
        public static async Task DiscoverAccounts(string username, string apiKey)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    // Set up HTTP client
                    httpClient.DefaultRequestHeaders.Add("accept", "text/plain");
                    httpClient.Timeout = TimeSpan.FromSeconds(30);

                    LogMessage("🚀 Starting ProjectX Account Discovery...");
                    LogMessage($"   API: {baseUrl}");
                    LogMessage($"   User: {username}");
                    LogMessage("");

                    // Step 1: Authenticate
                    LogMessage("🔐 Step 1: Authenticating...");
                    string authToken = await Authenticate(httpClient, username, apiKey);
                    
                    if (string.IsNullOrEmpty(authToken))
                    {
                        LogMessage("❌ Authentication failed. Check credentials.");
                        LogMessage("   Make sure you have purchased API access and generated an API key.");
                        return;
                    }

                    LogMessage("✅ Authentication successful!");
                    LogMessage("");

                    // Step 2: Search for accounts
                    LogMessage("🔍 Step 2: Searching for accounts...");
                    await SearchAccounts(httpClient, authToken);
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ Discovery failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Authenticate with ProjectX
        /// </summary>
        private static async Task<string> Authenticate(HttpClient httpClient, string username, string apiKey)
        {
            try
            {
                var loginRequest = new
                {
                    userName = username,
                    apiKey = apiKey
                };

                var json = JsonConvert.SerializeObject(loginRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Auth/loginKey", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = JsonConvert.DeserializeObject<AuthResponse>(responseContent);
                    
                    if (authResponse.success)
                    {
                        // Set auth header for future requests
                        httpClient.DefaultRequestHeaders.Authorization = 
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResponse.token);
                            
                        return authResponse.token;
                    }
                    else
                    {
                        LogMessage($"   ❌ Auth failed: {authResponse.errorMessage}");
                        LogMessage($"   Error code: {authResponse.errorCode}");
                        
                        if (authResponse.errorCode == 3)
                        {
                            LogMessage("   💡 Error 3 = Invalid Credentials");
                            LogMessage("      - Make sure you're using an API key, not your password");
                            LogMessage("      - API keys must be generated after purchasing API access");
                        }
                        
                        return null;
                    }
                }
                else
                {
                    LogMessage($"   ❌ HTTP error: {response.StatusCode}");
                    LogMessage($"   Response: {responseContent}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   ❌ Auth exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Search for available accounts
        /// </summary>
        private static async Task SearchAccounts(HttpClient httpClient, string authToken)
        {
            try
            {
                var searchRequest = new { onlyActiveAccounts = true };
                var json = JsonConvert.SerializeObject(searchRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Account/search", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var accountResponse = JsonConvert.DeserializeObject<AccountSearchResponse>(responseContent);
                    
                    if (accountResponse.success && accountResponse.accounts != null)
                    {
                        LogMessage($"✅ Found {accountResponse.accounts.Count} account(s):");
                        LogMessage("");
                        LogMessage("═══════════════════════════════════════");
                        
                        foreach (var account in accountResponse.accounts)
                        {
                            LogMessage($"🏦 ACCOUNT DETAILS:");
                            LogMessage($"   ID: {account.id} ← USE THIS NUMBER");
                            LogMessage($"   Name: {account.name}");
                            LogMessage($"   Balance: ${account.balance:F2}");
                            LogMessage($"   Can Trade: {account.canTrade}");
                            LogMessage($"   Simulated: {account.simulated}");
                            LogMessage($"   Visible: {account.isVisible}");
                            LogMessage("═══════════════════════════════════════");
                            LogMessage("");
                        }

                        LogMessage("📋 INSTRUCTIONS:");
                        LogMessage("1. Find your account above (likely matches LAUPGKBZYWQK0)");
                        LogMessage("2. Copy the ID number");
                        LogMessage("3. Update MainStrategy.cs ProjectXAccountId property");
                        LogMessage("4. Recompile your strategy");
                        LogMessage("");
                        LogMessage("🎯 READY FOR TRADING!");
                    }
                    else
                    {
                        LogMessage($"❌ Account search failed: {accountResponse.errorMessage}");
                        LogMessage($"   Error code: {accountResponse.errorCode}");
                    }
                }
                else
                {
                    LogMessage($"❌ Account search HTTP error: {response.StatusCode}");
                    LogMessage($"   Response: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Account search exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Log message to NinjaTrader output window
        /// </summary>
        private static void LogMessage(string message)
        {
            Output.Process(message, PrintTo.OutputTab1);
        }

        #region Data Models

        private class AuthResponse
        {
            public string token { get; set; }
            public bool success { get; set; }
            public int errorCode { get; set; }
            public string errorMessage { get; set; }
        }

        private class AccountSearchResponse
        {
            public List<AccountInfo> accounts { get; set; }
            public bool success { get; set; }
            public int errorCode { get; set; }
            public string errorMessage { get; set; }
        }

        private class AccountInfo
        {
            public int id { get; set; }
            public string name { get; set; }
            public decimal balance { get; set; }
            public bool canTrade { get; set; }
            public bool isVisible { get; set; }
            public bool simulated { get; set; }
        }

        #endregion
    }
}