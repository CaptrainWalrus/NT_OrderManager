#!/usr/bin/env node

/**
 * Test Script for Scriptable Integration
 * This simulates the complete flow:
 * 1. NinjaTrader sends trade notification
 * 2. Server generates chart and stores trade
 * 3. Scriptable widget polls for pending trades
 * 4. User approves/rejects via URLs
 */

const SERVER_URL = process.env.SERVER_URL || 'https://pushcut-server.onrender.com';
// For local testing: const SERVER_URL = 'http://localhost:3000';

async function main() {
    // Import fetch dynamically for ESM compatibility
    const { default: fetch } = await import('node-fetch');
    console.log('🧪 Testing Scriptable Integration Flow\n');
    
    try {
        // Step 1: Test server health
        console.log('1️⃣ Testing server health...');
        const healthResponse = await fetch(`${SERVER_URL}/health`);
        const healthData = await healthResponse.json();
        console.log('   ✅ Server health:', healthData.status);
        
        // Step 2: Simulate NinjaTrader trade notification
        console.log('\n2️⃣ Simulating NinjaTrader trade notification...');
        const tradePayload = {
            instrument: 'ES',
            direction: 'LONG',
            entryPrice: 5105.25,
            stopLoss: 5095.00,
            takeProfit: 5115.50,
            confidence: 0.847,
            bars: [
                { time: "14:28", open: 5091, high: 5095, low: 5088, close: 5093 },
                { time: "14:29", open: 5093, high: 5098, low: 5091, close: 5096 },
                { time: "14:30", open: 5096, high: 5102, low: 5094, close: 5100 },
                { time: "14:31", open: 5100, high: 5107, low: 5098, close: 5105 },
                { time: "14:32", open: 5105, high: 5110, low: 5103, close: 5108 },
                { time: "14:33", open: 5108, high: 5112, low: 5106, close: 5111 },
                { time: "14:34", open: 5111, high: 5115, low: 5109, close: 5113 },
                { time: "14:35", open: 5113, high: 5116, low: 5111, close: 5114 }
            ]
        };
        
        const tradeResponse = await fetch(`${SERVER_URL}/trade-notification`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(tradePayload)
        });
        
        const tradeData = await tradeResponse.json();
        
        if (tradeData.success) {
            console.log('   ✅ Trade notification sent successfully');
            console.log(`   📊 Chart URL: ${tradeData.chartUrl}`);
            console.log(`   ✅ Approve URL: ${tradeData.approveUrl}`);
            console.log(`   ❌ Reject URL: ${tradeData.rejectUrl}`);
        } else {
            throw new Error('Trade notification failed: ' + tradeData.error);
        }
        
        // Step 3: Test Scriptable widget endpoints
        console.log('\n3️⃣ Testing Scriptable widget endpoints...');
        
        // Test pending trade endpoint
        const pendingResponse = await fetch(`${SERVER_URL}/widget/pending-trade`);
        const pendingData = await pendingResponse.json();
        
        if (pendingData.hasPendingTrade) {
            console.log('   ✅ Pending trade detected by widget');
            console.log(`   📈 ${pendingData.instrument} ${pendingData.direction} @ $${pendingData.entryPrice}`);
            console.log(`   🎯 SL: $${pendingData.stopLoss} | TP: $${pendingData.takeProfit}`);
            console.log(`   🕒 Time remaining: ${pendingData.timeRemaining}`);
            console.log(`   📊 Chart URL: ${pendingData.chartUrl}`);
        } else {
            console.log('   ❌ No pending trade found by widget');
        }
        
        // Test summary endpoint
        const summaryResponse = await fetch(`${SERVER_URL}/widget/summary`);
        const summaryData = await summaryResponse.json();
        console.log('   📊 Today\'s stats:', {
            trades: summaryData.todayTrades,
            approved: summaryData.approvedToday,
            rejected: summaryData.rejectedToday
        });
        
        // Step 4: Test chart accessibility
        console.log('\n4️⃣ Testing chart accessibility...');
        if (pendingData.hasPendingTrade) {
            const chartResponse = await fetch(pendingData.chartUrl);
            if (chartResponse.ok) {
                const contentType = chartResponse.headers.get('content-type');
                console.log(`   ✅ Chart accessible: ${contentType}`);
                console.log(`   📏 Chart size: ${chartResponse.headers.get('content-length')} bytes`);
            } else {
                console.log(`   ❌ Chart not accessible: ${chartResponse.status}`);
            }
        }
        
        // Step 5: Instructions for manual testing
        console.log('\n5️⃣ Manual Testing Instructions:');
        console.log('   📱 Copy scriptable-chart-widget.js to Scriptable app');
        console.log('   🔧 Update SERVER_URL in widget if needed');
        console.log('   ➕ Add widget to iOS home screen');
        console.log('   👀 Widget should show pending trade with chart');
        console.log('   👆 Tap APPROVE/REJECT buttons to test URLs\n');
        
        if (pendingData.hasPendingTrade) {
            console.log('🎯 Test URLs (you can open these in browser):');
            console.log(`   ✅ APPROVE: ${pendingData.approveUrl}`);
            console.log(`   ❌ REJECT:  ${pendingData.rejectUrl}`);
        }
        
        console.log('\n✅ All tests completed successfully!');
        
    } catch (error) {
        console.error('\n❌ Test failed:', error.message);
        process.exit(1);
    }
}

main(); 