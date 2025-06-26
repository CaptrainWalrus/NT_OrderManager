// Scriptable Auto-Loader
// Copy this script ONCE into Scriptable
// It will automatically fetch and run the latest live monitor script

const SERVER_URL = "https://pushcut-server.onrender.com";

async function loadAndRunScript() {
    try {
        console.log("🔄 Loading latest script from server...");
        
        // Fetch the latest script from server
        let request = new Request(`${SERVER_URL}/scriptable/live-monitor`);
        let scriptCode = await request.loadString();
        
        console.log("✅ Script loaded successfully");
        console.log("🚀 Executing script...");
        
        // Wrap the script in an async function to handle top-level awaits
        let wrappedScript = `(async () => {\n${scriptCode}\n})()`;
        await eval(wrappedScript);
        
    } catch (error) {
        console.error("❌ Error loading script:", error.message);
        
        // Show user-friendly error
        let alert = new Alert();
        alert.title = "Script Load Error";
        alert.message = `Failed to load script from server:\n\n${error.message}\n\nCheck your internet connection and try again.`;
        alert.addAction("OK");
        await alert.presentAlert();
    }
}

// Run the loader
await loadAndRunScript(); 