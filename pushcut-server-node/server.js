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

// --- Chart Setup ---
const chartJSNodeCanvas = new ChartJSNodeCanvas({
    width: 800,
    height: 600,
    backgroundColour: '#1E222D'
});

app.use(express.json());

// --- API Endpoints ---

// Endpoint for NinjaTrader to request approval
app.post('/trade/request', async (req, res) => {
    const tradeData = req.body;
    const tradeId = uuidv4();

    trades.set(tradeId, {
        status: 'pending',
        data: tradeData,
        timestamp: new Date(),
        expiresAt: new Date(new Date().getTime() + TRADE_TIMEOUT_MINUTES * 60000),
    });

    // Asynchronously generate chart and send notification
    processTradeAsync(tradeId, tradeData).catch(err => {
        console.error(`[ERROR] Failed to process trade ${tradeId}:`, err);
        trades.get(tradeId).status = 'error';
    });

    res.status(202).json({
        trade_id: tradeId,
        status: 'pending',
        chart_url: `${SERVER_URL}/chart/${tradeId}`,
    });
});

// Endpoint to check the status of a trade
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
        decision_time: trade.decision_time ? trade.decision_time.toISOString() : null,
    });
});

// Endpoints for Pushcut to call
app.post('/trade/approve/:trade_id', (req, res) => handleApproval(req, res, 'approved'));
app.post('/trade/reject/:trade_id', (req, res) => handleApproval(req, res, 'rejected'));

// Endpoint to serve the generated chart
app.get('/chart/:trade_id', async (req, res) => {
    const { trade_id } = req.params;
    const chartPath = path.join(CHART_DIR, `${trade_id}.png`);

    try {
        await fs.access(chartPath);
        res.sendFile(chartPath);
    } catch (error) {
        res.status(202).json({ message: 'Chart is still generating...' });
    }
});

// Health check for Render
app.get('/health', (req, res) => {
    res.json({ status: 'healthy', active_trades: trades.size });
});


// --- Helper Functions ---

async function processTradeAsync(tradeId, tradeData) {
    await generateChart(tradeId, tradeData);
    await sendPushcutNotification(tradeId, tradeData);
}

function handleApproval(req, res, status) {
    const { trade_id } = req.params;
    const trade = trades.get(trade_id);
    if (trade && trade.status === 'pending') {
        trade.status = status;
        trade.decision_time = new Date();
        console.log(`[APPROVAL] Trade ${trade_id} has been ${status}.`);
    }
    res.json({ status, trade_id });
}

async function generateChart(tradeId, tradeData) {
    await fs.mkdir(CHART_DIR, { recursive: true });
    
    const { bars, signal, instrument } = tradeData;
    
    // Simple labels and data
    const labels = bars.map((_, index) => `Bar ${index + 1}`);
    const closeData = bars.map(b => b.close);
    const highData = bars.map(b => b.high);
    const lowData = bars.map(b => b.low);

    const configuration = {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Close Price',
                data: closeData,
                borderColor: '#00FF00',
                backgroundColor: 'rgba(0, 255, 0, 0.1)',
                borderWidth: 3,
                pointRadius: 4,
                pointBackgroundColor: '#00FF00',
                fill: false,
            }, {
                label: 'High Price',
                data: highData,
                borderColor: '#FF6B6B',
                backgroundColor: 'rgba(255, 107, 107, 0.1)',
                borderWidth: 2,
                pointRadius: 2,
                pointBackgroundColor: '#FF6B6B',
                fill: false,
            }, {
                label: 'Low Price',
                data: lowData,
                borderColor: '#4ECDC4',
                backgroundColor: 'rgba(78, 205, 196, 0.1)',
                borderWidth: 2,
                pointRadius: 2,
                pointBackgroundColor: '#4ECDC4',
                fill: false,
            }]
        },
        options: {
            responsive: false,
            animation: false,
            scales: {
                y: {
                    beginAtZero: false,
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
                    grid: {
                        color: 'rgba(255, 255, 255, 0.1)'
                    },
                    ticks: {
                        color: '#FFFFFF'
                    }
                }
            },
            plugins: {
                title: {
                    display: true,
                    text: `${instrument} - ${signal.direction} Signal - Entry: $${signal.entry_price}`,
                    color: '#FFFFFF',
                    font: {
                        size: 20
                    },
                    padding: 20
                },
                legend: {
                    display: true,
                    labels: {
                        color: '#FFFFFF'
                    }
                }
            }
        }
    };

    try {
        const imageBuffer = await chartJSNodeCanvas.renderToBuffer(configuration);
        await fs.writeFile(path.join(CHART_DIR, `${tradeId}.png`), imageBuffer);
        console.log(`[CHART] Chart generated for trade ${tradeId}`);
    } catch (error) {
        console.error(`[CHART] Error generating chart for ${tradeId}:`, error);
        throw error;
    }
}

async function sendPushcutNotification(tradeId, tradeData) {
    const { instrument, signal } = tradeData;
    const chartUrl = `${SERVER_URL}/chart/${tradeId}`;
    
    const payload = {
        title: `🔥 ${instrument} ${signal.direction}`,
        text: `Entry: $${signal.entry_price.toFixed(2)}\nRisk: $${signal.risk_amount}\nTarget: $${signal.target_amount}`,
        image: chartUrl,
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
        await axios.post(PUSHCUT_URL, payload, { timeout: 10000 });
        console.log(`[PUSHCUT] Notification sent for trade ${tradeId}`);
    } catch (error) {
        console.error(`[PUSHCUT] Failed to send notification for ${tradeId}:`, error.message);
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


// --- Server Start ---
app.listen(PORT, () => {
    console.log(`Pushcut Approval Service running at http://localhost:${PORT}`);
}); 