const express = require('express');
const { ChartJSNodeCanvas } = require('chartjs-node-canvas');
const fs = require('fs');
const path = require('path');
// Use dynamic import to support ESM-only node-fetch in CommonJS
const fetch = (...args) => import('node-fetch').then(({ default: fetch }) => fetch(...args));

const app = express();
const PORT = process.env.PORT || 3000;
const DISCORD_WEBHOOK_URL = process.env.DISCORD_WEBHOOK_URL;

if (DISCORD_WEBHOOK_URL) {
    console.log('🔔 Discord webhook ready');
} else {
    console.log('ℹ️ Discord webhook disabled (no DISCORD_WEBHOOK_URL env var)');
}

app.use(express.json());

// Chart configuration with font fallbacks
const chartJSNodeCanvas = new ChartJSNodeCanvas({
    width: 400,
    height: 300,
    backgroundColour: '#1a1a1a',
    chartCallback: (ChartJS) => {
        // Register required components
        ChartJS.register(
            ChartJS.CategoryScale,
            ChartJS.LinearScale,
            ChartJS.LineElement,
            ChartJS.PointElement,
            ChartJS.LineController,
            ChartJS.Title,
            ChartJS.Tooltip,
            ChartJS.Legend,
            ChartJS.Filler
        );
        
        // Set default font family to system fonts
        ChartJS.defaults.font.family = 'Arial, sans-serif';
        ChartJS.defaults.font.size = 10;
    }
});

// In-memory storage for demo
let pendingTrade = null;
let tradeStats = {
    todayTrades: 0,
    approvedToday: 0,
    rejectedToday: 0,
    lastUpdate: new Date().toISOString()
};

// ═══════════════════════════════════════════════════════════════
// CORE ENDPOINTS FOR NINJATRADER INTEGRATION
// ═══════════════════════════════════════════════════════════════

// Receive trade notification from NinjaTrader
app.post('/trade-notification', async (req, res) => {
    try {
        console.log('📨 Received trade notification:', req.body);
        
        const { instrument, direction, entryPrice, stopLoss, takeProfit, confidence, bars } = req.body;
        
        // Generate unique trade ID
        const tradeId = `trade_${Date.now()}`;
        
        // Create chart
        const chartBuffer = await generateChart(bars, direction, entryPrice, stopLoss, takeProfit);
        const chartFilename = `chart_${tradeId}.png`;
        const chartPath = path.join(__dirname, 'charts', chartFilename);
        
        // Ensure charts directory exists
        if (!fs.existsSync(path.join(__dirname, 'charts'))) {
            fs.mkdirSync(path.join(__dirname, 'charts'), { recursive: true });
        }
        
        // Save chart to file
        fs.writeFileSync(chartPath, chartBuffer);
        
        // Store pending trade
        pendingTrade = {
            id: tradeId,
            instrument,
            direction,
            entryPrice,
            stopLoss,
            takeProfit,
            confidence,
            timestamp: new Date().toISOString(),
            chartUrl: `/chart/${chartFilename}`,
            timeoutAt: new Date(Date.now() + 5 * 60 * 1000) // 5 minutes
        };
        
        tradeStats.todayTrades++;
        tradeStats.lastUpdate = new Date().toISOString();
        
        console.log(`✅ Trade ${tradeId} created with chart: ${chartFilename}`);
        
        // Build full URLs for Discord
        const fullChartUrl = `${req.protocol}://${req.get('host')}/chart/${chartFilename}`;
        const approveUrl = `${req.protocol}://${req.get('host')}/approve/${tradeId}`;
        const rejectUrl  = `${req.protocol}://${req.get('host')}/reject/${tradeId}`;

        // Fire-and-forget Discord webhook (non-blocking)
        sendDiscordNotification({
          id: tradeId,
          instrument,
          direction,
          entryPrice,
          stopLoss,
          takeProfit,
          confidence,
          fullChartUrl,
          approveUrl,
          rejectUrl
        });
        
        res.json({
            success: true,
            tradeId,
            message: 'Trade approval request created',
            chartUrl: `${req.protocol}://${req.get('host')}/chart/${chartFilename}`,
            approveUrl: `${req.protocol}://${req.get('host')}/approve/${tradeId}`,
            rejectUrl: `${req.protocol}://${req.get('host')}/reject/${tradeId}`
        });
        
    } catch (error) {
        console.error('❌ Error processing trade:', error);
        res.status(500).json({ success: false, error: error.message });
    }
});

// Approve trade
app.get('/approve/:tradeId', async (req, res) => {
    const { tradeId } = req.params;
    console.log(`✅ Trade ${tradeId} APPROVED`);
    
    if (pendingTrade && pendingTrade.id === tradeId) {
        // Send Discord confirmation before clearing
        await sendDiscordStatus(pendingTrade, true);
        pendingTrade = null;
        tradeStats.approvedToday++;
        tradeStats.lastUpdate = new Date().toISOString();
    } else {
        // Unknown trade but still notify
        await sendDiscordStatus({ id: tradeId }, true);
    }
    
    res.send(`
        <html>
            <head><title>Trade Approved</title></head>
            <body style="font-family: Arial; text-align: center; margin-top: 100px;">
                <h1 style="color: green;">✅ Trade Approved!</h1>
                <p>Trade ${tradeId} has been approved and executed.</p>
                <p><a href="javascript:window.close()">Close Window</a></p>
            </body>
        </html>
    `);
});

// Reject trade
app.get('/reject/:tradeId', async (req, res) => {
    const { tradeId } = req.params;
    console.log(`❌ Trade ${tradeId} REJECTED`);
    
    if (pendingTrade && pendingTrade.id === tradeId) {
        await sendDiscordStatus(pendingTrade, false);
        pendingTrade = null;
        tradeStats.rejectedToday++;
        tradeStats.lastUpdate = new Date().toISOString();
    } else {
        await sendDiscordStatus({ id: tradeId }, false);
    }
    
    res.send(`
        <html>
            <head><title>Trade Rejected</title></head>
            <body style="font-family: Arial; text-align: center; margin-top: 100px;">
                <h1 style="color: red;">❌ Trade Rejected</h1>
                <p>Trade ${tradeId} has been rejected and will not be executed.</p>
                <p><a href="javascript:window.close()">Close Window</a></p>
            </body>
        </html>
    `);
});

// ═══════════════════════════════════════════════════════════════
// SCRIPTABLE WIDGET ENDPOINTS
// ═══════════════════════════════════════════════════════════════

// Widget endpoint: Check for pending trades
app.get('/widget/pending-trade', (req, res) => {
    console.log('📱 Widget checking for pending trades');
    
    // Check if trade has expired
    if (pendingTrade && new Date() > new Date(pendingTrade.timeoutAt)) {
        console.log('⏰ Trade expired, auto-rejecting');
        pendingTrade = null;
        tradeStats.rejectedToday++;
    }
    
    if (pendingTrade) {
        const timeRemaining = Math.max(0, Math.floor((new Date(pendingTrade.timeoutAt) - new Date()) / 1000));
        const minutes = Math.floor(timeRemaining / 60);
        const seconds = timeRemaining % 60;
        
        res.json({
            hasPendingTrade: true,
            instrument: pendingTrade.instrument,
            direction: pendingTrade.direction,
            entryPrice: pendingTrade.entryPrice,
            stopLoss: pendingTrade.stopLoss,
            takeProfit: pendingTrade.takeProfit,
            confidence: pendingTrade.confidence,
            timeRemaining: `${minutes}:${seconds.toString().padStart(2, '0')}`,
            chartUrl: `${req.protocol}://${req.get('host')}${pendingTrade.chartUrl}`,
            approveUrl: `${req.protocol}://${req.get('host')}/approve/${pendingTrade.id}`,
            rejectUrl: `${req.protocol}://${req.get('host')}/reject/${pendingTrade.id}`,
            lastUpdate: new Date().toISOString()
        });
    } else {
        res.json({
            hasPendingTrade: false,
            lastUpdate: tradeStats.lastUpdate
        });
    }
});

// Widget endpoint: Summary statistics
app.get('/widget/summary', (req, res) => {
    console.log('📊 Widget requesting summary stats');
    
    res.json({
        todayTrades: tradeStats.todayTrades,
        approvedToday: tradeStats.approvedToday,
        rejectedToday: tradeStats.rejectedToday,
        lastUpdate: tradeStats.lastUpdate,
        serverStatus: 'active'
    });
});

// ═══════════════════════════════════════════════════════════════
// SCRIPTABLE CODE SERVING
// ═══════════════════════════════════════════════════════════════

// Serve Scriptable scripts directly
app.get('/scriptable/live-monitor', (req, res) => {
    console.log('📱 Serving live monitor script');
    res.set('Content-Type', 'text/plain');
    res.sendFile(path.join(__dirname, 'scriptable-live-monitor.js'));
});

app.get('/scriptable/chart-widget', (req, res) => {
    console.log('📱 Serving chart widget script');
    res.set('Content-Type', 'text/plain');
    res.sendFile(path.join(__dirname, 'scriptable-chart-widget.js'));
});

// ═══════════════════════════════════════════════════════════════
// CHART SERVING AND GENERATION
// ═══════════════════════════════════════════════════════════════

// Serve chart images
app.get('/chart/:filename', (req, res) => {
    const { filename } = req.params;
    const chartPath = path.join(__dirname, 'charts', filename);
    
    if (fs.existsSync(chartPath)) {
        res.setHeader('Content-Type', 'image/png');
        res.setHeader('Cache-Control', 'no-cache');
        res.sendFile(chartPath);
        console.log(`📊 Served chart: ${filename}`);
    } else {
        res.status(404).json({ error: 'Chart not found' });
        console.log(`❌ Chart not found: ${filename}`);
    }
});

// Generate test chart endpoint
app.post('/test-chart', async (req, res) => {
    try {
        const testBars = req.body.bars || [
            { time: "09:30", open: 5091, high: 5095, low: 5088, close: 5093 },
            { time: "09:31", open: 5093, high: 5098, low: 5091, close: 5096 },
            { time: "09:32", open: 5096, high: 5102, low: 5094, close: 5100 }
        ];
        
        const chartBuffer = await generateChart(testBars, 'LONG', 5100, 5090, 5110);
        
        res.setHeader('Content-Type', 'image/png');
        res.send(chartBuffer);
        
        console.log('📊 Generated test chart');
    } catch (error) {
        console.error('❌ Test chart error:', error);
        res.status(500).json({ error: error.message });
    }
});

// ═══════════════════════════════════════════════════════════════
// CHART GENERATION FUNCTION
// ═══════════════════════════════════════════════════════════════

async function generateChart(bars, direction, entryPrice, stopLoss, takeProfit) {
    const labels = bars.map(bar => bar.time);
    const closePrices = bars.map(bar => bar.close);
    const highPrices = bars.map(bar => bar.high);
    const lowPrices = bars.map(bar => bar.low);
    
    const config = {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'Close',
                    data: closePrices,
                    borderColor: '#00ff88',
                    backgroundColor: 'rgba(0, 255, 136, 0.1)',
                    borderWidth: 2,
                    fill: false,
                    tension: 0.1
                },
                {
                    label: 'High',
                    data: highPrices,
                    borderColor: '#ffaa00',
                    backgroundColor: 'rgba(255, 170, 0, 0.1)',
                    borderWidth: 1,
                    fill: false,
                    tension: 0.1
                },
                {
                    label: 'Low',
                    data: lowPrices,
                    borderColor: '#ff6b6b',
                    backgroundColor: 'rgba(255, 107, 107, 0.1)',
                    borderWidth: 1,
                    fill: false,
                    tension: 0.1
                }
            ]
        },
        options: {
            responsive: true,
            plugins: {
                title: {
                    display: true,
                    text: `${direction} Entry: $${entryPrice}`,
                    color: direction === 'LONG' ? '#00ff88' : '#ff6b6b',
                    font: { size: 14, weight: 'bold', family: 'Arial' }
                },
                legend: {
                    display: false  // Disable legend to save space and avoid font issues
                }
            },
            scales: {
                x: {
                    title: { display: false },  // Remove titles to avoid font rendering issues
                    ticks: { color: '#ffffff', font: { family: 'Arial', size: 9 } },
                    grid: { color: '#333333' }
                },
                y: {
                    title: { display: false },  // Remove titles to avoid font rendering issues
                    ticks: { color: '#ffffff', font: { family: 'Arial', size: 9 } },
                    grid: { color: '#333333' }
                }
            },
            backgroundColor: '#1a1a1a',
            layout: {
                padding: 10
            }
        },
        plugins: [{
            beforeDraw: (chart) => {
                const ctx = chart.canvas.getContext('2d');
                ctx.fillStyle = '#1a1a1a';
                ctx.fillRect(0, 0, chart.width, chart.height);
            }
        }]
    };
    
    return await chartJSNodeCanvas.renderToBuffer(config);
}

// ═══════════════════════════════════════════════════════════════
// BASIC SERVER ENDPOINTS
// ═══════════════════════════════════════════════════════════════

app.get('/', (req, res) => {
    res.json({
        message: 'Trading Approval Server',
        version: '2.0',
        status: 'active',
        endpoints: {
            'POST /trade-notification': 'Submit trade for approval',
            'GET /widget/pending-trade': 'Check for pending trades (Scriptable)',
            'GET /widget/summary': 'Get trade statistics (Scriptable)',
            'GET /chart/:filename': 'Get chart image',
            'GET /approve/:tradeId': 'Approve trade',
            'GET /reject/:tradeId': 'Reject trade'
        },
        pendingTrade: pendingTrade ? true : false,
        stats: tradeStats
    });
});

app.get('/health', (req, res) => {
    res.json({ 
        status: 'healthy', 
        timestamp: new Date().toISOString(),
        hasPendingTrade: !!pendingTrade
    });
});

// ═══════════════════════════════════════════════════════════════
// SERVER STARTUP
// ═══════════════════════════════════════════════════════════════

app.listen(PORT, () => {
    console.log(`🚀 Trading Approval Server running on port ${PORT}`);
    console.log(`📊 Chart generation ready`);
    console.log(`📱 Scriptable widget endpoints active`);
});

// Cleanup expired trades every minute
setInterval(() => {
    if (pendingTrade && new Date() > new Date(pendingTrade.timeoutAt)) {
        console.log('⏰ Auto-rejecting expired trade:', pendingTrade.id);
        pendingTrade = null;
        tradeStats.rejectedToday++;
        tradeStats.lastUpdate = new Date().toISOString();
    }
}, 60000);

async function sendDiscordNotification(trade) {
  try {
    if (!DISCORD_WEBHOOK_URL) {
      console.log('ℹ️  DISCORD_WEBHOOK_URL not set – skipping Discord notification');
      return;
    }

    const embedColor = trade.direction === 'LONG' ? 0x00ff00 : 0xff0000;

    const payload = {
      content: `🚨 **New Trade Pending**` ,
      embeds: [
        {
          title: `${trade.instrument} ${trade.direction} @ ${trade.entryPrice}`,
          color: embedColor,
          description: `[✔ Approve](${trade.approveUrl})   |   [✖ Reject](${trade.rejectUrl})   |   [📈 View Chart](${trade.fullChartUrl})`,
          image: { url: trade.fullChartUrl },
          footer: { text: `Trade ID: ${trade.id}` }
        }
      ],
      components: [
        {
          type: 1,
          components: [
            { type: 2, style: 5, label: '✔ Approve', url: trade.approveUrl },
            { type: 2, style: 5, label: '✖ Reject',  url: trade.rejectUrl },
            { type: 2, style: 5, label: '📈 View Chart', url: trade.fullChartUrl }
          ]
        }
      ]
    };

    await fetch(DISCORD_WEBHOOK_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
      timeout: 5000
    });

    console.log('💬 Discord notification sent');
  } catch (err) {
    console.error('❌ Discord notification failed:', err.message);
  }
}

async function sendDiscordStatus(trade, approved) {
  try {
    if (!DISCORD_WEBHOOK_URL) return;
    const color = approved ? 0x00ff00 : 0xff0000;
    const statusText = approved ? 'APPROVED' : 'REJECTED';
    const payload = {
      embeds: [{
        title: `Trade ${trade.id} ${statusText}`,
        color,
        description: approved ? '✅ Order sent to NinjaTrader' : '❌ Order cancelled',
        footer: { text: new Date().toLocaleString() }
      }]
    };
    await fetch(DISCORD_WEBHOOK_URL, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
  } catch (err) {
    console.error('Discord status webhook failed', err.message);
  }
} 