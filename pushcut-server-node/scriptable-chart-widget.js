// Trading Chart Widget for Scriptable iOS App
// Displays server-rendered charts with approve/deny buttons

const SERVER_URL = "https://pushcut-server.onrender.com";

// Widget configuration
const WIDGET_SIZE = "medium"; // medium gives us more space for charts
const REFRESH_INTERVAL = 3; // seconds between checks when app is open
const WIDGET_REFRESH_INTERVAL = 5 * 60 * 1000; // 5 minutes for home screen widget

// Create main widget
let widget = new ListWidget();
widget.backgroundColor = new Color("#1a1a1a");
widget.spacing = 4;

try {
    // Check for pending trades
    console.log("🔍 Checking for pending trades...");
    let pendingReq = new Request(`${SERVER_URL}/widget/pending-trade`);
    let pendingData = await pendingReq.loadJSON();
    
    if (pendingData.hasPendingTrade) {
        await createTradingWidget(widget, pendingData);
    } else {
        await createIdleWidget(widget, pendingData);
    }
} catch (error) {
    console.error("❌ Error:", error);
    await createErrorWidget(widget, error);
}

// Present widget
if (config.runsInWidget) {
    Script.setWidget(widget);
    // Set faster refresh for home screen widget
    Script.setWidget(widget, WIDGET_REFRESH_INTERVAL);
} else {
    // When running in app, show immediately
    widget.presentMedium();
}

// Set up auto-refresh when running in Scriptable app
if (!config.runsInWidget) {
    console.log("🔄 App mode - setting up auto-refresh every 3 seconds");
    // This will only work when the app is open
    Timer.schedule(3000, true, async () => {
        console.log("🔄 Auto-refreshing...");
        // Re-run the script
        Script.complete();
    });
}

Script.complete();

// ═══════════════════════════════════════════════════════════════
// WIDGET CREATION FUNCTIONS
// ═══════════════════════════════════════════════════════════════

async function createTradingWidget(widget, data) {
    console.log("🚨 Creating pending trade widget");
    
    // Header with urgent styling
    let header = widget.addText("🚨 TRADE APPROVAL NEEDED");
    header.textColor = Color.red();
    header.font = Font.boldSystemFont(14);
    header.centerAlignText();
    
    widget.addSpacer(6);
    
    // Trade information
    let tradeInfo = widget.addText(`${data.instrument} ${data.direction} @ $${data.entryPrice}`);
    tradeInfo.textColor = data.direction === "LONG" ? Color.green() : Color.red();
    tradeInfo.font = Font.boldSystemFont(16);
    tradeInfo.centerAlignText();
    
    let riskInfo = widget.addText(`SL: $${data.stopLoss} | TP: $${data.takeProfit}`);
    riskInfo.textColor = Color.white();
    riskInfo.font = Font.systemFont(12);
    riskInfo.centerAlignText();
    
    widget.addSpacer(4);
    
    // Load and display chart image
    try {
        console.log(`📊 Loading chart: ${data.chartUrl}`);
        let chartReq = new Request(data.chartUrl);
        let chartImage = await chartReq.loadImage();
        
        let imageElement = widget.addImage(chartImage);
        imageElement.centerAlignImage();
        
        // Scale image to fit widget
        imageElement.imageSize = new Size(280, 120);
        
        console.log("✅ Chart loaded successfully");
    } catch (chartError) {
        console.error("📊 Chart load failed:", chartError);
        let errorText = widget.addText("📊 Chart temporarily unavailable");
        errorText.textColor = Color.orange();
        errorText.font = Font.systemFont(10);
        errorText.centerAlignText();
    }
    
    widget.addSpacer(6);
    
    // Confidence and timer
    let confidence = widget.addText(`Confidence: ${Math.round(data.confidence * 100)}% | ⏰ ${data.timeRemaining}`);
    confidence.textColor = Color.yellow();
    confidence.font = Font.systemFont(11);
    confidence.centerAlignText();
    
    widget.addSpacer(8);
    
    // Action buttons row
    let buttonStack = widget.addStack();
    buttonStack.layoutHorizontally();
    buttonStack.centerAlignContent();
    
    // Approve button
    let approveBtn = buttonStack.addText("  ✅ APPROVE  ");
    approveBtn.textColor = Color.white();
    approveBtn.font = Font.boldSystemFont(12);
    approveBtn.backgroundColor = Color.green();
    approveBtn.cornerRadius = 8;
    approveBtn.url = data.approveUrl;
    
    buttonStack.addSpacer(12);
    
    // Reject button  
    let rejectBtn = buttonStack.addText("  ❌ REJECT  ");
    rejectBtn.textColor = Color.white();
    rejectBtn.font = Font.boldSystemFont(12);
    rejectBtn.backgroundColor = Color.red();
    rejectBtn.cornerRadius = 8;
    rejectBtn.url = data.rejectUrl;
    
    // Widget tap opens chart in full size
    widget.url = data.chartUrl;
    
    console.log("✅ Trading widget created");
}

async function createIdleWidget(widget, data) {
    console.log("😴 Creating idle widget");
    
    // Get summary stats
    let summaryReq = new Request(`${SERVER_URL}/widget/summary`);
    let summary = await summaryReq.loadJSON();
    
    // Status header
    let header = widget.addText("📈 Trading Monitor");
    header.textColor = Color.green();
    header.font = Font.boldSystemFont(16);
    header.centerAlignText();
    
    widget.addSpacer(8);
    
    let status = widget.addText("✅ No pending trades");
    status.textColor = Color.white();
    status.font = Font.systemFont(14);
    status.centerAlignText();
    
    widget.addSpacer(12);
    
    // Today's activity in a stack
    let statsStack = widget.addStack();
    statsStack.layoutVertically();
    
    let todayTitle = statsStack.addText("📊 Today's Activity");
    todayTitle.textColor = Color.white();
    todayTitle.font = Font.boldSystemFont(14);
    todayTitle.centerAlignText();
    
    statsStack.addSpacer(6);
    
    let tradesText = statsStack.addText(`${summary.todayTrades} trades submitted`);
    tradesText.textColor = Color.gray();
    tradesText.font = Font.systemFont(12);
    tradesText.centerAlignText();
    
    let approvedText = statsStack.addText(`✅ ${summary.approvedToday} approved | ❌ ${summary.rejectedToday} rejected`);
    approvedText.textColor = Color.gray();
    approvedText.font = Font.systemFont(12);
    approvedText.centerAlignText();
    
    widget.addSpacer(12);
    
    // Last update time
    let updated = widget.addText(`Updated: ${new Date(data.lastUpdate).toLocaleTimeString()}`);
    updated.textColor = Color.gray();
    updated.font = Font.systemFont(10);
    updated.centerAlignText();
    
    console.log("✅ Idle widget created");
}

async function createErrorWidget(widget, error) {
    console.log("❌ Creating error widget");
    
    let errorHeader = widget.addText("⚠️ Connection Error");
    errorHeader.textColor = Color.red();
    errorHeader.font = Font.boldSystemFont(16);
    errorHeader.centerAlignText();
    
    widget.addSpacer(8);
    
    let errorMsg = widget.addText("Cannot connect to trading server");
    errorMsg.textColor = Color.white();
    errorMsg.font = Font.systemFont(12);
    errorMsg.centerAlignText();
    
    widget.addSpacer(4);
    
    let errorDetail = widget.addText(`${error.message || 'Unknown error'}`);
    errorDetail.textColor = Color.orange();
    errorDetail.font = Font.systemFont(10);
    errorDetail.centerAlignText();
    
    widget.addSpacer(8);
    
    let retryText = widget.addText("Pull down to refresh");
    retryText.textColor = Color.blue();
    retryText.font = Font.systemFont(12);
    retryText.centerAlignText();
} 