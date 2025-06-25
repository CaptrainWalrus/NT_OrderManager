const express = require('express');
const axios = require('axios');
const fs = require('fs').promises;
const path = require('path');
const { ChartJSNodeCanvas } = require('chartjs-node-canvas');
const { v4: uuidv4 } = require('uuid');

const app = express();
const PORT = process.env.PORT || 8000;

// In-memory storage (replace with Redis/DB in a larger-scale app)
const trades = new Map();
const TRADE_TIMEOUT_MINUTES = 5;

// Pushcut Configuration
const PUSHCUT_URL = "https://api.pushcut.io/8a_iGKpg-bNQDqFVFQAON/notifications/Trade%20Approval";
const SERVER_URL = process.env.RENDER_EXTERNAL_URL || `http://localhost:${PORT}`;

// Chart Configuration
const chartWidth = 1200;
const chartHeight = 800;
const chartJSNodeCanvas = new ChartJSNodeCanvas({
    width: chartWidth,
    height: chartHeight,
    backgroundColour: '#2f3640',
});

app.use(express.json());

// --- API Endpoints ---

app.post('/trade/request', async (req, res) => {
    const tradeData = req.body;
    const tradeId = uuidv4();

    trades.set(tradeId, {
        status: 'pending',
        data: tradeData,
        timestamp: new Date(),
        expiresAt: new Date(new Date().getTime() + TRADE_TIMEOUT_MINUTES * 60000),
    });

    // Process asynchronously
    processTradeAsync(tradeId, tradeData);

    res.status(202).json({
        trade_id: tradeId,
        status: 'pending',
        chart_url: `${SERVER_URL}/chart/${tradeId}`,
    });
});

app.get('/trade/status/:trade_id', (req, res) => {
    const { trade_id } = req.params;
    const trade = trades.get(trade_id);

    if (!trade) {
        return res.status(404).json({ error: 'Trade not found' });
    }

    if (new Date() > trade.expiresAt && trade.status === 'pending') {
        trade.status = 'timeout';
        trade.decision_time = new Date();
    }

    res.json({
        trade_id,
        status: trade.status,
        timestamp: trade.timestamp.toISOString(),
        expires_at: trade.expiresAt.toISOString(),
        decision_time: trade.decision_time ? trade.decision_time.toISOString() : null,
    });
});

app.post('/trade/approve/:trade_id', (req, res) => {
    const { trade_id } = req.params;
    const trade = trades.get(trade_id);
    if (trade && trade.status === 'pending') {
        trade.status = 'approved';
        trade.decision_time = new Date();
    }
    res.json({ status: 'approved', trade_id });
});

app.post('/trade/reject/:trade_id', (req, res) => {
    const { trade_id } = req.params;
    const trade = trades.get(trade_id);
    if (trade && trade.status === 'pending') {
        trade.status = 'rejected';
        trade.decision_time = new Date();
    }
    res.json({ status: 'rejected', trade_id });
});

app.get('/chart/:trade_id', async (req, res) => {
    const { trade_id } = req.params;
    const chartPath = path.join(__dirname, 'charts', `${trade_id}.png`);

    try {
        await fs.access(chartPath);
        res.sendFile(chartPath);
    } catch (error) {
        res.status(202).json({ message: 'Chart is being generated...' });
    }
});

app.get('/health', (req, res) => {
    res.json({
        status: 'healthy',
        timestamp: new Date().toISOString(),
        active_trades: Array.from(trades.values()).filter(t => t.status === 'pending').length,
    });
});

// --- Helper Functions ---

async function processTradeAsync(tradeId, tradeData) {
    try {
        await generateChart(tradeId, tradeData);
        await sendPushcutNotification(tradeId, tradeData);
    } catch (error) {
        console.error(`Error processing trade ${tradeId}:`, error);
        const trade = trades.get(tradeId);
        if (trade) {
            trade.status = 'error';
            trade.error = error.message;
        }
    }
}

async function generateChart(tradeId, tradeData) {
    const chartDir = path.join(__dirname, 'charts');
    await fs.mkdir(chartDir, { recursive: true });

    const { bars, signal, instrument } = tradeData;
    const labels = bars.map(b => new Date(b.time));
    const closeData = bars.map(b => b.close);

    const configuration = {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Price',
                data: closeData,
                borderColor: '#00ff88',
                backgroundColor: 'rgba(0, 255, 136, 0.1)',
                borderWidth: 2,
                pointRadius: 0,
                tension: 0.1,
                fill: true,
            }],
        },
        options: {
            scales: {
                x: {
                    type: 'time',
                    time: {
                        unit: 'minute',
                        tooltipFormat: 'MMM D, h:mm a',
                        displayFormats: {
                            minute: 'h:mm a'
                        }
                    },
                    grid: { color: 'rgba(255, 255, 255, 0.2)' },
                    ticks: { color: 'white', maxRotation: 45, minRotation: 45 },
                },
                y: {
                    grid: { color: 'rgba(255, 255, 255, 0.2)' },
                    ticks: { color: 'white' },
                },
            },
            plugins: {
                legend: { labels: { color: 'white' } },
                title: {
                    display: true,
                    text: `${instrument} - ${signal.direction} Signal`,
                    color: 'white',
                    font: { size: 20 },
                    padding: { top: 10, bottom: 30 }
                },
                annotation: {
                    annotations: {
                        entry: {
                            type: 'line',
                            yMin: signal.entry_price,
                            yMax: signal.entry_price,
                            borderColor: '#ff6b6b',
                            borderWidth: 3,
                            label: { content: `Entry: ${signal.entry_price.toFixed(2)}`, enabled: true, position: 'start', backgroundColor: '#ff6b6b' }
                        }
                    }
                }
            },
        },
    };

    const imageBuffer = await chartJSNodeCanvas.renderToBuffer(configuration);
    await fs.writeFile(path.join(chartDir, `${tradeId}.png`), imageBuffer);
    console.log(`Chart generated for trade ${tradeId}`);
}

async function sendPushcutNotification(tradeId, tradeData) {
    const { instrument, signal } = tradeData;
    const chartUrl = `${SERVER_URL}/chart/${tradeId}`;
    
    const payload = {
        title: `🔥 ${instrument} ${signal.direction}`,
        text: `Entry: $${signal.entry_price.toFixed(2)}\nRisk: $${signal.risk_amount.toFixed(0)} | Target: $${signal.target_amount.toFixed(0)}\nPattern: ${signal.pattern_type}`,
        image: chartUrl,
        isTimeSensitive: true,
        actions: [{
            name: "APPROVE ✅",
            url: `${SERVER_URL}/trade/approve/${tradeId}`,
            urlBackgroundOptions: { "httpMethod": "POST" }
        }, {
            name: "REJECT ❌",
            url: `${SERVER_URL}/trade/reject/${tradeId}`,
            urlBackgroundOptions: { "httpMethod": "POST" }
        }]
    };

    try {
        await axios.post(PUSHCUT_URL, payload, { timeout: 10000 });
        console.log(`Pushcut notification sent for trade ${tradeId}`);
    } catch (error) {
        console.error(`Failed to send Pushcut notification for ${tradeId}:`, error.message);
    }
}

// --- Cleanup ---
setInterval(() => {
    const now = new Date();
    let cleanedCount = 0;
    for (const [tradeId, trade] of trades.entries()) {
        const tradeAgeHours = (now - trade.timestamp) / (1000 * 60 * 60);
        if (tradeAgeHours > 1) {
            trades.delete(tradeId);
            const chartPath = path.join(__dirname, 'charts', `${tradeId}.png`);
            fs.unlink(chartPath).catch(err => console.error(`Failed to delete chart ${tradeId}:`, err.message));
            cleanedCount++;
        }
    }
    if (cleanedCount > 0) {
        console.log(`Cleaned up ${cleanedCount} old trades.`);
    }
}, 30 * 60 * 1000); // Run every 30 minutes


app.listen(PORT, () => {
    console.log(`Pushcut Approval Service running at http://localhost:${PORT}`);
}); 