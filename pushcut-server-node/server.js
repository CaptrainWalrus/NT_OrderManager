const express = require('express');
const axios = require('axios');
const fs = require('fs').promises;
const path = require('path');
const { ChartJSNodeCanvas } = require('chartjs-node-canvas');
const { v4: uuidv4 } = require('uuid');
const { Chart, Title, Tooltip, Legend, LineElement, PointElement, TimeScale, LinearScale, Filler } = require('chart.js');
const dateFnsAdapter = require('chartjs-adapter-date-fns');
const { de } = require('date-fns/locale');

// Correct, explicit registration for Chart.js v3 components
Chart.register(Title, Tooltip, Legend, LineElement, PointElement, TimeScale, LinearScale, Filler, dateFnsAdapter);

const app = express();
const PORT = process.env.PORT || 8000;

// In-memory storage for simplicity
const trades = new Map();
const TRADE_TIMEOUT_MINUTES = 5;

// --- Configurations ---
const PUSHCUT_URL = "https://api.pushcut.io/8a_iGKpg-bNQDqFVFQAON/notifications/Trade%20Approval";
const SERVER_URL = process.env.RENDER_EXTERNAL_URL || `http://localhost:${PORT}`;
const CHART_DIR = path.join(__dirname, 'charts');

// Correct way to register date adapter for Chart.js v3 with this library
const chartCallback = (ChartJS) => {
    ChartJS.register(require('chartjs-adapter-date-fns'));
};

// --- Chart Setup with v4 plugin registration ---
const chartJSNodeCanvas = new ChartJSNodeCanvas({
    width: 800,
    height: 600,
    backgroundColour: '#1a1a1a',
    plugins: {
        requireLegacy: ['chartjs-chart-financial']
    }
});

app.use(express.json());

// --- API Endpoints ---

// Health check
app.get('/health', (req, res) => {
    res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// Submit trade for approval
app.post('/trade/request', async (req, res) => {
    try {
        const tradeId = uuidv4();
        const tradeData = req.body;

        // Store trade data
        trades.set(tradeId, {
            ...tradeData,
            status: 'pending',
            timestamp: Date.now()
        });

        // Set timeout for automatic rejection
        setTimeout(() => {
            if (trades.has(tradeId) && trades.get(tradeId).status === 'pending') {
                trades.set(tradeId, { ...trades.get(tradeId), status: 'rejected', reason: 'timeout' });
                console.log(`[TIMEOUT] Trade ${tradeId} automatically rejected`);
            }
        }, TRADE_TIMEOUT_MINUTES * 60 * 1000);

        // Generate chart and send notification asynchronously
        processTradeAsync(tradeId, tradeData).catch(error => {
            console.error(`[ERROR] Failed to process trade ${tradeId}:`, error);
        });

        res.json({
            trade_id: tradeId,
            status: 'pending',
            chart_url: `${SERVER_URL}/chart/${tradeId}`
        });

        console.log(`[REQUEST] New trade request ${tradeId} for ${tradeData.instrument}`);
    } catch (error) {
        console.error('[ERROR] Trade request failed:', error);
        res.status(500).json({ error: 'Internal server error' });
    }
});

// Approve trade
app.post('/trade/approve/:tradeId', (req, res) => {
    const { tradeId } = req.params;
    
    if (!trades.has(tradeId)) {
        return res.status(404).json({ error: 'Trade not found' });
    }

    const trade = trades.get(tradeId);
    if (trade.status !== 'pending') {
        return res.status(400).json({ error: `Trade already ${trade.status}` });
    }

    trades.set(tradeId, { ...trade, status: 'approved', approved_at: Date.now() });
    console.log(`[APPROVED] Trade ${tradeId} approved`);
    
    res.json({ success: true, message: 'Trade approved' });
});

// Reject trade
app.post('/trade/reject/:tradeId', (req, res) => {
    const { tradeId } = req.params;
    
    if (!trades.has(tradeId)) {
        return res.status(404).json({ error: 'Trade not found' });
    }

    const trade = trades.get(tradeId);
    if (trade.status !== 'pending') {
        return res.status(400).json({ error: `Trade already ${trade.status}` });
    }

    trades.set(tradeId, { ...trade, status: 'rejected', rejected_at: Date.now() });
    console.log(`[REJECTED] Trade ${tradeId} rejected`);
    
    res.json({ success: true, message: 'Trade rejected' });
});

// Get trade status
app.get('/trade/status/:tradeId', (req, res) => {
    const { tradeId } = req.params;
    
    if (!trades.has(tradeId)) {
        return res.status(404).json({ error: 'Trade not found' });
    }

    const trade = trades.get(tradeId);
    res.json({
        trade_id: tradeId,
        status: trade.status,
        timestamp: trade.timestamp,
        instrument: trade.instrument,
        signal: trade.signal
    });
});

// Serve chart images
app.get('/chart/:tradeId', async (req, res) => {
    try {
        const { tradeId } = req.params;
        const chartPath = path.join(CHART_DIR, `${tradeId}.png`);
        
        await fs.access(chartPath);
        res.sendFile(chartPath);
    } catch (error) {
        res.status(404).json({ error: 'Chart not found' });
    }
});

// --- Helper Functions ---

async function processTradeAsync(tradeId, tradeData) {
    const chartBase64 = await generateChart(tradeId, tradeData);
    await sendPushcutNotification(tradeId, tradeData, chartBase64);
}

async function generateChart(tradeId, tradeData) {
    await fs.mkdir(CHART_DIR, { recursive: true });
    
    const { bars, signal, instrument } = tradeData;
    
    // Prepare candlestick data in the format required by chartjs-chart-financial
    const candlestickData = bars.map((bar, index) => ({
        x: index + 1,
        o: bar.open,
        h: bar.high,
        l: bar.low,
        c: bar.close
    }));

    const configuration = {
        type: 'candlestick',
        data: {
            datasets: [{
                label: 'Price Action',
                data: candlestickData,
                borderColor: {
                    up: '#00FF00',      // Green for bullish candles
                    down: '#FF0000',    // Red for bearish candles
                    unchanged: '#999999' // Gray for doji
                },
                backgroundColor: {
                    up: 'rgba(0, 255, 0, 0.1)',
                    down: 'rgba(255, 0, 0, 0.1)',
                    unchanged: 'rgba(153, 153, 153, 0.1)'
                },
                borderWidth: 2,
            }]
        },
        options: {
            responsive: false,
            animation: false,
            scales: {
                y: {
                    beginAtZero: false,
                    border: {
                        display: true,
                        color: 'rgba(255, 255, 255, 0.2)'
                    },
                    grid: {
                        color: 'rgba(255, 255, 255, 0.1)'
                    },
                    ticks: {
                        color: '#FFFFFF',
                        callback: function(value) {
                            return '$' + value.toFixed(2);
                        }
                    }
                },
                x: {
                    type: 'linear',
                    position: 'bottom',
                    border: {
                        display: true,
                        color: 'rgba(255, 255, 255, 0.2)'
                    },
                    grid: {
                        color: 'rgba(255, 255, 255, 0.1)'
                    },
                    ticks: {
                        color: '#FFFFFF',
                        callback: function(value) {
                            return `Bar ${Math.floor(value)}`;
                        }
                    }
                }
            },
            plugins: {
                title: {
                    display: true,
                    text: `${instrument} - ${signal.direction} Signal - Entry: $${signal.entry_price}`,
                    color: '#FFFFFF',
                    font: {
                        family: 'Arial, sans-serif',
                        size: 16
                    },
                    padding: 20
                },
                legend: {
                    display: true,
                    labels: {
                        color: '#FFFFFF',
                        font: {
                            family: 'Arial, sans-serif'
                        }
                    }
                }
            }
        }
    };

    try {
        const imageBuffer = await chartJSNodeCanvas.renderToBuffer(configuration);
        await fs.writeFile(path.join(CHART_DIR, `${tradeId}.png`), imageBuffer);
        console.log(`[CHART] Candlestick chart generated for trade ${tradeId}`);
        
        // Return base64 encoded image for direct Pushcut delivery
        return imageBuffer.toString('base64');
    } catch (error) {
        console.error(`[CHART] Error generating chart for ${tradeId}:`, error);
        throw error;
    }
}

async function sendPushcutNotification(tradeId, tradeData, chartBase64) {
    const { instrument, signal } = tradeData;
    
    const payload = {
        title: `🔥 ${instrument} ${signal.direction}`,
        text: `Entry: $${signal.entry_price.toFixed(2)}\nRisk: $${signal.risk_amount}\nTarget: $${signal.target_amount}`,
        image: `data:image/png;base64,${chartBase64}`,
        isTimeSensitive: true,
        actions: [{
            name: "APPROVE",
            url: `${SERVER_URL}/trade/approve/${tradeId}`,
            urlBackgroundOptions: { "httpMethod": "POST" }
        }, {
            name: "REJECT", 
            url: `${SERVER_URL}/trade/reject/${tradeId}`,
            urlBackgroundOptions: { "httpMethod": "POST" }
        }]
    };

    try {
        const response = await axios.post(PUSHCUT_URL, payload);
        console.log(`[PUSHCUT] Notification sent for trade ${tradeId}: ${response.status}`);
        return response.data;
    } catch (error) {
        console.error(`[PUSHCUT] Error sending notification for ${tradeId}:`, error.message);
        throw error;
    }
}

// --- Cleanup Task ---
setInterval(() => {
    const now = new Date();
    let cleanedCount = 0;
    for (const [tradeId, trade] of trades.entries()) {
        const tradeAgeHours = (now - trade.timestamp) / 3600000;
        if (tradeAgeHours > 1) {
            trades.delete(tradeId);
            fs.unlink(path.join(CHART_DIR, `${tradeId}.png`)).catch(err => console.error(`Failed to delete chart ${tradeId}:`, err.message));
            cleanedCount++;
        }
    }
    if (cleanedCount > 0) console.log(`[CLEANUP] Cleaned up ${cleanedCount} old trades.`);
}, 30 * 60 * 1000); // Every 30 minutes

// Start server
app.listen(PORT, () => {
    console.log(`🚀 Pushcut Approval Service running on port ${PORT}`);
    console.log(`📊 Chart directory: ${CHART_DIR}`);
    console.log(`⏰ Trade timeout: ${TRADE_TIMEOUT_MINUTES} minutes`);
}); 