# Pattern Performance Timeline - C# Implementation

## Overview

This document outlines the C# implementation of the Pattern Performance Timeline feature in the OrderManager project. The implementation allows for tracking and recording trade outcomes related to pattern signals.

## Core Changes

### 1. CurvesV2Response Class
- Added 'signalContextId' property to store the context identifier from the API.
- This ID is used to link pattern signals with trade outcomes.

### 2. OrderRecordMasterLite Class
- Added 'SignalContextId' property to store and propagate the signal context.
- This allows tracking which pattern signal led to a particular trade.

### 3. SignalPackage Class
- Added 'SignalContextId' property to pass context from signals to orders.
- This ensures the context is preserved through the entire order lifecycle.

## New API Methods in CurvesV2Service

### 1. StoreSignalContextMapping
`csharp
public void StoreSignalContextMapping(string signalContextId, string entryUuid)
``n- Stores a mapping between the signal context ID and the entry order UUID.
- Called when an entry order is created.

### 2. RecordPatternPerformanceAsync
`csharp
public async Task<bool> RecordPatternPerformanceAsync(PatternPerformanceRecord record)
``n- Records a pattern performance outcome to the timeline API.
- Called when an exit order is processed.

## Integration Points

### 1. Entry Order Creation
- In **HandleEntryOrder** method (OrderManagement.cs)
- If a SignalContextId is available, it's stored in the context mapping.
- This mapping will be used later during exit handling.

### 2. Exit Order Processing
- In **ExitFollowUpAction** method (OrderManagement.cs)
- Calculates outcome score: +1 for profit, 0 for breakeven, -1 for loss.
- Creates a PatternPerformanceRecord with the outcome.
- Submits the record using RecordPatternPerformanceAsync.

### 3. Signal Processing
- In **getOrderFlowSignalPackage** method (MainStrategy.cs)
- Assigns the signal context ID to the signalPackage.
- Uses the session ID as a fallback when no specific context ID is available.

## Pattern Performance Record Structure
`csharp
public class PatternPerformanceRecord
{
    public string signalContextId { get; set; }
    public long timestamp_ms { get; set; }
    public int outcome_score { get; set; } // +1, 0, or -1
    public double pnl { get; set; }
}
`"
Add-Content -Path pattern_timeline_implementation_c_sharp.md -Value 
