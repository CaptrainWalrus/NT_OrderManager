# 📱 Scriptable Trading Approval Setup Guide

This guide walks you through setting up **Option 1**: Server-rendered charts displayed in Scriptable for trade approval.

## 🎯 Architecture Overview

```
NinjaTrader → Node.js Server → Chart Generation → Scriptable Widget → iOS Approval
     ↓              ↓                ↓               ↓              ↓
  C# Service   Chart Rendering   PNG Files    Chart Display   Tap Buttons
```

## ⚙️ Server Setup (Already Complete)

✅ **Server deployed**: `https://pushcut-server.onrender.com`  
✅ **Chart generation**: Server-side with Chart.js  
✅ **Widget endpoints**: `/widget/pending-trade` and `/widget/summary`  
✅ **C# integration**: Updated PushcutService.cs  

## 📱 iOS Scriptable Setup

### Step 1: Install Scriptable App
1. Download **Scriptable** from iOS App Store
2. Open the app and create a new script

### Step 2: Add Trading Widget Script
1. In Scriptable, tap **"+"** to create new script
2. Name it: **"Trading Approval"**
3. **Copy the entire contents** of `scriptable-chart-widget.js` into the script
4. **Save** the script

### Step 3: Add Widget to Home Screen
1. **Long-press** on iOS home screen
2. Tap **"+"** in top-left corner
3. Search for **"Scriptable"**
4. Select **"Medium"** widget size
5. **Add Widget** to home screen
6. **Tap the widget** to configure
7. Select **"Trading Approval"** script
8. Tap **"Done"**

### Step 4: Test Widget Connectivity
1. The widget should show: **"📈 Trading Monitor"**
2. Status should be: **"✅ No pending trades"**
3. If you see connection errors, check internet connectivity

## 🧪 Testing the Complete Flow

### Test 1: Server Connectivity
```bash
# Run the test script
node test_scriptable_integration.js
```

Expected output:
```
✅ Server health: healthy
✅ Trade notification sent successfully
✅ Pending trade detected by widget
📊 Chart accessible: image/png
```

### Test 2: Simulate Live Trade
1. **Run test script** to create a pending trade
2. **Check your widget** - should show:
   - 🚨 **TRADE APPROVAL NEEDED**
   - **ES LONG @ $5105.25**
   - **Chart image**
   - **✅ APPROVE** and **❌ REJECT** buttons

### Test 3: Approve/Reject Flow
1. **Tap APPROVE button** in widget
2. Should open browser with: **"✅ Trade Approved!"**
3. **Return to home screen** - widget should show: **"✅ No pending trades"**

## 🔗 C# NinjaTrader Integration

### Usage in Strategy
```csharp
// In your strategy's OnBarUpdate or signal logic:
public async void OnSignalDetected()
{
    // Use the enhanced entry function with approval
    bool success = await EntryLimitFunctionLiteWithApproval(
        quantity: 1,
        orderAction: OrderAction.Buy,
        signalPackage: mySignal,
        description: "Long ES Signal",
        bar: 0,
        orderType: OrderType.Market,
        builtSignal: patternResponse
    );
    
    if (success)
    {
        Print("Trade executed after approval");
    }
    else
    {
        Print("Trade rejected or timeout");
    }
}
```

### Configuration Properties
- **Enable Pushcut Approval**: `true/false`
- **Pushcut Server URL**: `https://pushcut-server.onrender.com`
- **Approval Timeout**: `5 minutes` (1-10 min range)
- **Timeout Behavior**: `reject` or `approve`

## 📊 Widget Behavior Guide

### When No Trades Pending
```
📈 Trading Monitor
✅ No pending trades

📊 Today's Activity
2 trades submitted
✅ 1 approved | ❌ 1 rejected

Updated: 2:34 PM
```

### When Trade Pending
```
🚨 TRADE APPROVAL NEEDED
ES LONG @ $5105.25
SL: $5095.00 | TP: $5115.50

[CHART IMAGE DISPLAYS HERE]

Confidence: 85% | ⏰ 4:23

✅ APPROVE    ❌ REJECT
```

## 🔧 Troubleshooting

### Widget Shows "Connection Error"
- **Check internet connection**
- **Verify server URL**: https://pushcut-server.onrender.com/health
- **Try refreshing widget** (pull down on home screen)

### No Chart Displays
- Chart generation might have failed
- **Tap widget** to open full-size chart URL
- Server may be experiencing load issues

### Buttons Don't Work
- **Ensure cellular/WiFi connection**
- **Try tapping widget** to refresh
- Check if trade has expired (5-minute timeout)

### NinjaTrader Not Connecting
- **Verify server URL** in strategy properties
- **Check firewall settings**
- **Test connection** using `TestPushcutConnection()` method

## 📈 Performance Expectations

- **Chart generation**: 2-3 seconds
- **Widget refresh**: Every 15 seconds when visible
- **Response time**: Immediate button taps
- **Timeout**: 5 minutes automatic rejection
- **Server uptime**: 99%+ on Render.com

## 🚀 Advanced Features

### Custom Chart Timeframes
Update `BuildTradeRequest()` to include more bars:
```csharp
int barCount = Math.Min(100, CurrentBars[0]); // More context
```

### Enhanced Notifications
Widget automatically:
- ✅ **Shows confidence scores**
- ⏰ **Displays countdown timer**
- 📊 **Tracks daily statistics**
- 🎯 **Displays risk/reward levels**

### Multi-Instrument Support
Server handles any instrument:
- **ES** (E-mini S&P 500)
- **NQ** (E-mini Nasdaq)
- **YM** (E-mini Dow)
- **RTY** (E-mini Russell)

## 🎯 Success Metrics

After setup, you should see:
- ✅ **Widget loads successfully**
- ✅ **Charts display clearly**
- ✅ **Buttons respond immediately**
- ✅ **NinjaTrader waits for approval**
- ✅ **Timeouts work correctly**

## 📞 Support

If you encounter issues:
1. **Run test script**: `node test_scriptable_integration.js`
2. **Check server logs**: https://pushcut-server.onrender.com/
3. **Verify widget code**: Ensure complete copy from `scriptable-chart-widget.js`
4. **Test connectivity**: Open server URL in browser

---

## 🏁 Quick Start Checklist

- [ ] Download Scriptable app
- [ ] Copy widget code from `scriptable-chart-widget.js`
- [ ] Create new Scriptable script
- [ ] Add medium widget to home screen
- [ ] Run test script to verify connectivity
- [ ] Test approval/rejection flow
- [ ] Update NinjaTrader strategy to use `EntryLimitFunctionLiteWithApproval()`
- [ ] Configure strategy properties (server URL, timeout, etc.)
- [ ] Execute live trade test

**🎉 You're ready for server-rendered chart approvals via Scriptable!** 