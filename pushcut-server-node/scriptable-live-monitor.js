// Live Trading Monitor - Fast Polling Version
// Run this in Scriptable app for real-time monitoring

const SERVER_URL = "https://pushcut-server.onrender.com";

async function startLiveMonitoring() {
    console.log("🚀 Starting live trading monitor...");
    
    let lastTradeId = null;
    let pollCount = 0;
    
    while (true) {
        try {
            pollCount++;
            console.log(`📡 Poll #${pollCount} - Checking for trades...`);
            
            // Check for pending trades
            let response = await new Request(`${SERVER_URL}/widget/pending-trade`).loadJSON();
            
            if (response.hasPendingTrade) {
                // Extract trade ID from URL
                let currentTradeId = response.approveUrl.split('/').pop();
                
                if (currentTradeId !== lastTradeId) {
                    // NEW TRADE DETECTED!
                    lastTradeId = currentTradeId;
                    
                    console.log("🚨 NEW TRADE DETECTED!");
                    console.log(`📈 ${response.instrument} ${response.direction} @ $${response.entryPrice}`);
                    console.log(`🎯 SL: $${response.stopLoss} | TP: $${response.takeProfit}`);
                    console.log(`⏰ Time: ${response.timeRemaining}`);
                    
                    // Show notification
                    let notification = new Notification();
                    notification.title = "🚨 TRADE APPROVAL NEEDED";
                    notification.body = `${response.instrument} ${response.direction} @ $${response.entryPrice}\nSL: $${response.stopLoss} | TP: $${response.takeProfit}`;
                    notification.sound = "default";
                    await notification.schedule();
                    
                    // Create quick action alert
                    let alert = new Alert();
                    alert.title = "🚨 Trade Approval Required";
                    alert.message = `${response.instrument} ${response.direction} @ $${response.entryPrice}\n\nSL: $${response.stopLoss} | TP: $${response.takeProfit}\nConfidence: ${Math.round(response.confidence * 100)}%\nTime: ${response.timeRemaining}`;
                    
                    alert.addAction("✅ APPROVE");
                    alert.addAction("❌ REJECT");
                    alert.addAction("📊 VIEW CHART");
                    alert.addCancelAction("⏰ DECIDE LATER");
                    
                    let choice = await alert.presentAlert();
                    
                    if (choice === 0) {
                        // APPROVE
                        await approveRejectTrade(response.approveUrl, "APPROVED");
                        lastTradeId = null; // Reset so we can detect next trade
                    } else if (choice === 1) {
                        // REJECT
                        await approveRejectTrade(response.rejectUrl, "REJECTED");
                        lastTradeId = null; // Reset so we can detect next trade
                    } else if (choice === 2) {
                        // VIEW CHART - Show chart then return to trade alert
                        Safari.open(response.chartUrl);
                        
                        // Wait a moment for Safari to open, then show the trade alert again
                        await new Promise(resolve => Timer.schedule(1000, false, resolve));
                        
                        // Re-show the trade alert
                        let alertAgain = new Alert();
                        alertAgain.title = "🚨 Trade Still Pending";
                        alertAgain.message = `${response.instrument} ${response.direction} @ $${response.entryPrice}\n\nSL: $${response.stopLoss} | TP: $${response.takeProfit}\nConfidence: ${Math.round(response.confidence * 100)}%\nTime: ${response.timeRemaining}`;
                        
                        alertAgain.addAction("✅ APPROVE");
                        alertAgain.addAction("❌ REJECT");
                        alertAgain.addAction("📊 VIEW CHART AGAIN");
                        alertAgain.addCancelAction("⏰ DECIDE LATER");
                        
                        let choiceAgain = await alertAgain.presentAlert();
                        
                        if (choiceAgain === 0) {
                            // APPROVE
                            await approveRejectTrade(response.approveUrl, "APPROVED");
                            lastTradeId = null;
                        } else if (choiceAgain === 1) {
                            // REJECT
                            await approveRejectTrade(response.rejectUrl, "REJECTED");
                            lastTradeId = null;
                        } else if (choiceAgain === 2) {
                            // VIEW CHART AGAIN - just continue monitoring, they can view chart again next poll
                        }
                        // Don't reset lastTradeId, keep monitoring this trade
                    }
                    // Choice 3 (DECIDE LATER) - just continue monitoring
                }
                
                console.log(`📊 Monitoring trade ${currentTradeId} - ${response.timeRemaining} remaining`);
                
            } else {
                if (lastTradeId) {
                    console.log("✅ Trade completed or expired");
                    lastTradeId = null;
                }
                console.log("😴 No pending trades");
            }
            
        } catch (error) {
            console.error("❌ Error:", error.message);
        }
        
        // Wait 2 seconds before next poll
        console.log("⏳ Waiting 2 seconds...");
        await new Promise(resolve => Timer.schedule(2000, false, resolve));
    }
}

async function approveRejectTrade(url, action) {
    try {
        console.log(`${action === "APPROVED" ? "✅" : "❌"} ${action} trade`);
        
        let response = await new Request(url).loadString();
        
        let notification = new Notification();
        notification.title = `Trade ${action}`;
        notification.body = `Trade has been ${action.toLowerCase()}`;
        notification.sound = "default";
        await notification.schedule();
        
        console.log(`✅ Trade ${action} successfully`);
        
    } catch (error) {
        console.error(`❌ Error ${action.toLowerCase()}ing trade:`, error.message);
    }
}

// Check if user has already chosen to monitor (store preference)
let shouldPrompt = Keychain.get("trading_monitor_prompt") !== "false";

if (shouldPrompt) {
    let alert = new Alert();
    alert.title = "Live Trading Monitor";
    alert.message = "This will continuously monitor for new trades every 2 seconds.\n\n• Keep Scriptable app open\n• You'll get instant notifications\n• Approve/reject directly from alerts\n\nReady to start?";
    alert.addAction("🚀 START MONITORING");
    alert.addAction("🚀 START & DON'T ASK AGAIN");
    alert.addCancelAction("❌ Cancel");

    let startMonitoring = await alert.presentAlert();

    if (startMonitoring === 0 || startMonitoring === 1) {
        if (startMonitoring === 1) {
            // Don't ask again
            Keychain.set("trading_monitor_prompt", "false");
        }
        await startLiveMonitoring();
    } else {
        console.log("❌ Monitoring cancelled");
    }
} else {
    // Auto-start monitoring
    console.log("🔄 Auto-starting monitoring (preference saved)");
    await startLiveMonitoring();
} 