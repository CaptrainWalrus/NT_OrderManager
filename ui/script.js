// Update status every 5 seconds
let updateTimer = setInterval(updateStatus, 5000);
updateStatus(); // Initial status check

async function updateStatus() {
    try {
        const response = await fetch('/api/status');
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        const data = await response.json();
        
        for (const [service, info] of Object.entries(data)) {
            const statusElement = document.getElementById(`${service}-status`);
            if (statusElement) {
                statusElement.textContent = `Status: ${info.status} (Port: ${info.port})`;
                statusElement.className = info.status === 'running' ? 'running' : 'stopped';
            }
        }
    } catch (error) {
        console.error('Error updating status:', error);
        clearInterval(updateTimer); // Stop polling on error
        for (const service of ['redis', 'rust', 'python']) {
            const statusElement = document.getElementById(`${service}-status`);
            if (statusElement) {
                statusElement.textContent = 'Status: Error checking status';
                statusElement.className = 'error';
            }
        }
    }
}

async function startService(service) {
    try {
        const endpoint = service === 'redis' ? '/api/start_redis' : 
                        service === 'rust' ? '/api/start_rust' : '/api/start';
        
        const response = await fetch(endpoint, { 
            method: 'POST',
            headers: {
                'Accept': 'application/json'
            }
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const data = await response.json();
        if (data.status === 'started' || data.status === 'already running') {
            await updateStatus();
        }
    } catch (error) {
        console.error(`Error starting ${service}:`, error);
        const statusElement = document.getElementById(`${service}-status`);
        if (statusElement) {
            statusElement.textContent = `Status: Error starting service`;
            statusElement.className = 'error';
        }
    }
}

async function stopAll() {
    try {
        const response = await fetch('/api/stop_all', { 
            method: 'POST',
            headers: {
                'Accept': 'application/json'
            }
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        await updateStatus();
    } catch (error) {
        console.error('Error stopping services:', error);
        for (const service of ['redis', 'rust', 'python']) {
            const statusElement = document.getElementById(`${service}-status`);
            if (statusElement) {
                statusElement.textContent = 'Status: Error stopping service';
                statusElement.className = 'error';
            }
        }
    }
} 