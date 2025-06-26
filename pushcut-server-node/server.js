const express = require('express');
const axios = require('axios');
const fs = require('fs').promises;
const path = require('path');
const { createCanvas } = require('canvas');
const { v4: uuidv4 } = require('uuid');

const app = express();
const PORT = process.env.PORT || 8000;

// In-memory storage for simplicity
const trades = new Map();
const TRADE_TIMEOUT_MINUTES = 5;

// --- Configurations ---
const PUSHCUT_URL = "https://api.pushcut.io/8a_iGKpg-bNQDqFVFQAON/notifications/Trade%20Approval";
const SERVER_URL = process.env.RENDER_EXTERNAL_URL || `http://localhost:${PORT}`;
const CHART_DIR = path.join(__dirname, 'charts');

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
    
    // Prepare candlestick data for Plotly
    const dates = bars.map((_, index) => `Bar ${index + 1}`);
    const open = bars.map(bar => bar.open);
    const high = bars.map(bar => bar.high);
    const low = bars.map(bar => bar.low);
    const close = bars.map(bar => bar.close);

    // Create Plotly candlestick chart
    const trace = {
        x: dates,
        close: close,
        decreasing: {line: {color: '#FF4444'}},
        high: high,
        increasing: {line: {color: '#00AA00'}}, 
        line: {color: 'rgba(31,119,180,1)'},
        low: low,
        open: open,
        type: 'candlestick',
        name: 'Price Action',
        xaxis: 'x',
        yaxis: 'y'
    };

    const layout = {
        title: {
            text: `${instrument} - ${signal.direction} Signal - Entry: $${signal.entry_price}`,
            font: { color: '#FFFFFF', size: 16 }
        },
        dragmode: 'zoom',
        margin: { r: 10, t: 25, b: 40, l: 60 },
        showlegend: false,
        width: 800,
        height: 600,
        paper_bgcolor: '#1a1a1a',
        plot_bgcolor: '#1a1a1a',
        xaxis: {
            autorange: true,
            domain: [0, 1],
            range: ['2017-01-03 12:00', '2017-02-15 12:00'],
            rangeslider: { range: ['2017-01-03 12:00', '2017-02-15 12:00'] },
            title: 'Date',
            type: 'category',
            color: '#FFFFFF',
            gridcolor: 'rgba(255,255,255,0.1)'
        },
        yaxis: {
            autorange: true,
            domain: [0, 1],
            range: [Math.min(...low) * 0.99, Math.max(...high) * 1.01],
            type: 'linear',
            title: 'Price ($)',
            color: '#FFFFFF',
            gridcolor: 'rgba(255,255,255,0.1)',
            tickformat: '$,.2f'
        }
    };

    const figure = { data: [trace], layout: layout };

    try {
        // Use canvas to render the chart
        const canvas = createCanvas(800, 600);
        const ctx = canvas.getContext('2d');
        
        // Draw background
        ctx.fillStyle = '#1a1a1a';
        ctx.fillRect(0, 0, 800, 600);
        
        // Draw title
        ctx.fillStyle = '#FFFFFF';
        ctx.font = '16px Arial';
        ctx.textAlign = 'center';
        ctx.fillText(`${instrument} - ${signal.direction} Signal - Entry: $${signal.entry_price}`, 400, 30);
        
        // Calculate chart area
        const chartTop = 60;
        const chartBottom = 550;
        const chartLeft = 60;
        const chartRight = 740;
        const chartWidth = chartRight - chartLeft;
        const chartHeight = chartBottom - chartTop;
        
        // Find price range
        const minPrice = Math.min(...low);
        const maxPrice = Math.max(...high);
        const priceRange = maxPrice - minPrice;
        const priceMargin = priceRange * 0.05;
        const scaledMinPrice = minPrice - priceMargin;
        const scaledMaxPrice = maxPrice + priceMargin;
        const scaledPriceRange = scaledMaxPrice - scaledMinPrice;
        
        // Draw grid lines
        ctx.strokeStyle = 'rgba(255,255,255,0.1)';
        ctx.lineWidth = 1;
        ctx.setLineDash([2, 2]);
        
        // Horizontal grid lines (price levels)
        for (let i = 0; i <= 5; i++) {
            const y = chartTop + (i / 5) * chartHeight;
            ctx.beginPath();
            ctx.moveTo(chartLeft, y);
            ctx.lineTo(chartRight, y);
            ctx.stroke();
            
            // Price labels
            const price = scaledMaxPrice - (i / 5) * scaledPriceRange;
            ctx.fillStyle = '#FFFFFF';
            ctx.font = '12px Arial';
            ctx.textAlign = 'right';
            ctx.fillText(`$${price.toFixed(2)}`, chartLeft - 5, y + 4);
        }
        
        // Vertical grid lines
        for (let i = 0; i < bars.length; i++) {
            const x = chartLeft + (i / (bars.length - 1)) * chartWidth;
            ctx.beginPath();
            ctx.moveTo(x, chartTop);
            ctx.lineTo(x, chartBottom);
            ctx.stroke();
        }
        
        ctx.setLineDash([]);
        
        // Draw candlesticks
        const candleWidth = chartWidth / bars.length * 0.7;
        
        for (let i = 0; i < bars.length; i++) {
            const bar = bars[i];
            const x = chartLeft + (i / (bars.length - 1)) * chartWidth;
            
            // Scale prices to chart coordinates
            const openY = chartBottom - ((bar.open - scaledMinPrice) / scaledPriceRange) * chartHeight;
            const closeY = chartBottom - ((bar.close - scaledMinPrice) / scaledPriceRange) * chartHeight;
            const highY = chartBottom - ((bar.high - scaledMinPrice) / scaledPriceRange) * chartHeight;
            const lowY = chartBottom - ((bar.low - scaledMinPrice) / scaledPriceRange) * chartHeight;
            
            // Determine candle color
            const isUp = bar.close > bar.open;
            ctx.strokeStyle = isUp ? '#00AA00' : '#FF4444';
            ctx.fillStyle = isUp ? '#00AA00' : '#FF4444';
            ctx.lineWidth = 2;
            
            // Draw high-low line
            ctx.beginPath();
            ctx.moveTo(x, highY);
            ctx.lineTo(x, lowY);
            ctx.stroke();
            
            // Draw open-close rectangle
            const rectHeight = Math.abs(closeY - openY);
            const rectY = Math.min(openY, closeY);
            
            if (isUp) {
                ctx.strokeRect(x - candleWidth/2, rectY, candleWidth, rectHeight);
            } else {
                ctx.fillRect(x - candleWidth/2, rectY, candleWidth, rectHeight);
            }
        }
        
        // Draw axes
        ctx.strokeStyle = '#FFFFFF';
        ctx.lineWidth = 2;
        ctx.setLineDash([]);
        
        // Y-axis
        ctx.beginPath();
        ctx.moveTo(chartLeft, chartTop);
        ctx.lineTo(chartLeft, chartBottom);
        ctx.stroke();
        
        // X-axis
        ctx.beginPath();
        ctx.moveTo(chartLeft, chartBottom);
        ctx.lineTo(chartRight, chartBottom);
        ctx.stroke();
        
        // X-axis labels
        ctx.fillStyle = '#FFFFFF';
        ctx.font = '10px Arial';
        ctx.textAlign = 'center';
        for (let i = 0; i < bars.length; i += Math.ceil(bars.length / 8)) {
            const x = chartLeft + (i / (bars.length - 1)) * chartWidth;
            ctx.fillText(`Bar ${i + 1}`, x, chartBottom + 20);
        }
        
        // Save chart
        const imageBuffer = canvas.toBuffer('image/png');
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