from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse, JSONResponse
from pydantic import BaseModel
from datetime import datetime, timedelta
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
import requests
import os
import uuid
import asyncio
from typing import List, Optional
import json

app = FastAPI(title="NinjaTrader Pushcut Approval Service")

# In-memory storage for trades (production could use Redis)
trades = {}
TRADE_TIMEOUT_MINUTES = 5

# Pushcut configuration
PUSHCUT_URL = "https://api.pushcut.io/8a_iGKpg-bNQDqFVFQAON/notifications/Trade%20Approval"

class Bar(BaseModel):
    time: str
    open: float
    high: float
    low: float
    close: float
    volume: int

class Signal(BaseModel):
    direction: str  # "LONG" or "SHORT"
    entry_price: float
    risk_amount: float = 50
    target_amount: float = 150
    pattern_type: str = "signal"
    confidence: float = 0.85

class TradeRequest(BaseModel):
    instrument: str
    timeframe: str = "5min"
    bars: List[Bar]
    signal: Signal
    indicators: Optional[dict] = None

class TradeResponse(BaseModel):
    trade_id: str
    status: str
    chart_url: str

@app.post("/trade/request", response_model=TradeResponse)
async def request_trade_approval(trade_data: TradeRequest):
    """
    NinjaTrader calls this to request trade approval.
    Returns immediately with trade_id and chart_url.
    """
    trade_id = str(uuid.uuid4())
    
    # Store trade data
    trades[trade_id] = {
        "status": "pending",
        "data": trade_data.dict(),
        "timestamp": datetime.now(),
        "expires_at": datetime.now() + timedelta(minutes=TRADE_TIMEOUT_MINUTES)
    }
    
    # Generate chart and send notification asynchronously
    asyncio.create_task(process_trade_async(trade_id, trade_data))
    
    chart_url = f"https://nt-pushcut-approval.onrender.com/chart/{trade_id}"
    
    return TradeResponse(
        trade_id=trade_id,
        status="pending",
        chart_url=chart_url
    )

@app.get("/trade/status/{trade_id}")
async def get_trade_status(trade_id: str):
    """
    NinjaTrader polls this endpoint to check approval status.
    Returns: pending, approved, rejected, or timeout
    """
    trade = trades.get(trade_id)
    
    if not trade:
        raise HTTPException(status_code=404, detail="Trade not found")
    
    # Check for timeout
    if datetime.now() > trade["expires_at"] and trade["status"] == "pending":
        trade["status"] = "timeout"
        trade["decision_time"] = datetime.now()
    
    return {
        "trade_id": trade_id,
        "status": trade["status"],
        "timestamp": trade["timestamp"].isoformat(),
        "expires_at": trade["expires_at"].isoformat(),
        "decision_time": trade.get("decision_time", "").isoformat() if trade.get("decision_time") else None
    }

@app.post("/trade/approve/{trade_id}")
async def approve_trade(trade_id: str):
    """Called by Pushcut when user taps APPROVE"""
    trade = trades.get(trade_id)
    
    if not trade:
        raise HTTPException(status_code=404, detail="Trade not found")
    
    if trade["status"] == "pending":
        trade["status"] = "approved"
        trade["decision_time"] = datetime.now()
    
    return {"status": "approved", "trade_id": trade_id}

@app.post("/trade/reject/{trade_id}")
async def reject_trade(trade_id: str):
    """Called by Pushcut when user taps REJECT"""
    trade = trades.get(trade_id)
    
    if not trade:
        raise HTTPException(status_code=404, detail="Trade not found")
    
    if trade["status"] == "pending":
        trade["status"] = "rejected"
        trade["decision_time"] = datetime.now()
    
    return {"status": "rejected", "trade_id": trade_id}

@app.get("/chart/{trade_id}")
async def get_chart(trade_id: str):
    """Serve the generated chart image"""
    chart_path = f"charts/{trade_id}.png"
    
    if os.path.exists(chart_path):
        return FileResponse(
            chart_path, 
            media_type="image/png",
            headers={"Cache-Control": "max-age=3600"}  # Cache for 1 hour
        )
    
    # If chart doesn't exist yet, return a placeholder
    return JSONResponse(
        status_code=202,
        content={"message": "Chart generating..."}
    )

@app.get("/health")
async def health_check():
    """Health check endpoint for deployment monitoring"""
    return {
        "status": "healthy",
        "timestamp": datetime.now().isoformat(),
        "active_trades": len([t for t in trades.values() if t["status"] == "pending"])
    }

async def process_trade_async(trade_id: str, trade_data: TradeRequest):
    """
    Background task to generate chart and send Pushcut notification
    """
    try:
        # Generate chart
        await generate_chart(trade_id, trade_data)
        
        # Send Pushcut notification
        await send_pushcut_notification(trade_id, trade_data)
        
    except Exception as e:
        print(f"Error processing trade {trade_id}: {e}")
        # Mark trade as failed
        if trade_id in trades:
            trades[trade_id]["status"] = "error"
            trades[trade_id]["error"] = str(e)

async def generate_chart(trade_id: str, trade_data: TradeRequest):
    """Generate price chart with trade context"""
    os.makedirs("charts", exist_ok=True)
    
    bars = trade_data.bars
    times = [datetime.fromisoformat(bar.time.replace('Z', '+00:00')) for bar in bars]
    closes = [bar.close for bar in bars]
    highs = [bar.high for bar in bars]
    lows = [bar.low for bar in bars]
    
    # Create figure optimized for mobile viewing
    plt.figure(figsize=(12, 8))
    plt.style.use('dark_background')
    
    # Price line
    plt.plot(times, closes, linewidth=2, color='#00ff88', label='Price')
    plt.fill_between(times, lows, highs, alpha=0.3, color='#00ff88')
    
    # Entry level
    entry_price = trade_data.signal.entry_price
    plt.axhline(y=entry_price, color='#ff6b6b', linestyle='-', linewidth=3, 
                label=f'Entry: ${entry_price:.2f}')
    
    # Risk/Target levels
    if trade_data.signal.direction == "LONG":
        target = entry_price + (trade_data.signal.target_amount / 50) * (entry_price * 0.01)  # Rough calculation
        risk = entry_price - (trade_data.signal.risk_amount / 50) * (entry_price * 0.01)
        plt.axhline(y=target, color='#4ecdc4', linestyle='--', alpha=0.8, label=f'Target: ${target:.2f}')
        plt.axhline(y=risk, color='#ff9f43', linestyle='--', alpha=0.8, label=f'Stop: ${risk:.2f}')
    else:  # SHORT
        target = entry_price - (trade_data.signal.target_amount / 50) * (entry_price * 0.01)
        risk = entry_price + (trade_data.signal.risk_amount / 50) * (entry_price * 0.01)
        plt.axhline(y=target, color='#4ecdc4', linestyle='--', alpha=0.8, label=f'Target: ${target:.2f}')
        plt.axhline(y=risk, color='#ff9f43', linestyle='--', alpha=0.8, label=f'Stop: ${risk:.2f}')
    
    # Formatting for mobile
    plt.title(f'{trade_data.instrument} - {trade_data.signal.direction} Signal\n'
              f'Confidence: {trade_data.signal.confidence:.0%} | Pattern: {trade_data.signal.pattern_type}', 
              fontsize=16, color='white', pad=20)
    
    plt.xlabel('Time', fontsize=12, color='white')
    plt.ylabel('Price', fontsize=12, color='white')
    plt.legend(loc='upper left', fontsize=10)
    plt.grid(True, alpha=0.3, color='white')
    
    # Format x-axis for time
    plt.gca().xaxis.set_major_formatter(mdates.DateFormatter('%H:%M'))
    plt.gca().xaxis.set_major_locator(mdates.HourLocator(interval=1))
    plt.xticks(rotation=45)
    
    # Tight layout for mobile
    plt.tight_layout()
    
    # Save with high DPI for crisp mobile display
    plt.savefig(f"charts/{trade_id}.png", dpi=200, bbox_inches='tight', 
                facecolor='#2f3640', edgecolor='none')
    plt.close()

async def send_pushcut_notification(trade_id: str, trade_data: TradeRequest):
    """Send approval notification to iPhone via Pushcut"""
    
    chart_url = f"https://nt-pushcut-approval.onrender.com/chart/{trade_id}"
    
    # Calculate rough P&L for display
    risk_dollars = trade_data.signal.risk_amount
    target_dollars = trade_data.signal.target_amount
    
    payload = {
        "title": f"🔥 {trade_data.instrument} {trade_data.signal.direction}",
        "text": f"Entry: ${trade_data.signal.entry_price:.2f}\n"
                f"Risk: ${risk_dollars:.0f} | Target: ${target_dollars:.0f}\n"
                f"Pattern: {trade_data.signal.pattern_type} | Confidence: {trade_data.signal.confidence:.0%}\n"
                f"⏰ 5min timeout",
        "image": chart_url,
        "isTimeSensitive": True,
        "actions": [
            {
                "name": "APPROVE ✅",
                "url": f"https://nt-pushcut-approval.onrender.com/trade/approve/{trade_id}",
                "urlBackgroundOptions": {"httpMethod": "POST"}
            },
            {
                "name": "REJECT ❌",
                "url": f"https://nt-pushcut-approval.onrender.com/trade/reject/{trade_id}",
                "urlBackgroundOptions": {"httpMethod": "POST"}
            }
        ]
    }
    
    try:
        response = requests.post(PUSHCUT_URL, json=payload, timeout=10)
        response.raise_for_status()
        print(f"Pushcut notification sent for trade {trade_id}")
    except Exception as e:
        print(f"Failed to send Pushcut notification for trade {trade_id}: {e}")

# Cleanup old trades periodically
@app.on_event("startup")
async def startup_event():
    asyncio.create_task(cleanup_old_trades())

async def cleanup_old_trades():
    """Remove trades older than 1 hour to prevent memory buildup"""
    while True:
        try:
            cutoff = datetime.now() - timedelta(hours=1)
            to_remove = [
                trade_id for trade_id, trade in trades.items()
                if trade["timestamp"] < cutoff
            ]
            
            for trade_id in to_remove:
                # Clean up chart file
                chart_path = f"charts/{trade_id}.png"
                if os.path.exists(chart_path):
                    os.remove(chart_path)
                
                # Remove from memory
                del trades[trade_id]
            
            if to_remove:
                print(f"Cleaned up {len(to_remove)} old trades")
                
        except Exception as e:
            print(f"Error during cleanup: {e}")
        
        # Run cleanup every 30 minutes
        await asyncio.sleep(1800)

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000) 