document.addEventListener('DOMContentLoaded', () => {
    // Enable :active pseudo-classes on iOS Safari
    document.body.addEventListener('touchstart', () => {}, {passive: true});

    const fuelCountEl = document.getElementById('fuel-count');
    const fuelRateEl = document.getElementById('fuel-rate');
    const uptimeEl = document.getElementById('uptime');
    const rateLabelEl = document.getElementById('rate-label');
    const iconMuted = document.getElementById('icon-muted');
    const iconUnmuted = document.getElementById('icon-unmuted');
    const logList = document.getElementById('log-list');

    let displayPerMinute = true;
    let initializedUnits = false;

    function addLog(msg) {
        const li = document.createElement('li');
        const time = new Date().toLocaleTimeString([], { hour12: false });
        li.textContent = `[${time}] ${msg}`;
        logList.prepend(li);
        if (logList.children.length > 20) logList.removeChild(logList.lastChild);
    }

    async function updateStatus() {
        try {
            const response = await fetch('/api/status');
            const data = await response.json();
            
            if (!initializedUnits && data.units) {
                displayPerMinute = (data.units === 'min');
                initializedUnits = true;
                updateRateUI();
            }

            fuelCountEl.textContent = data.count.toLocaleString();
            
            // Handle mute state
            if (data.muted !== undefined) {
                iconMuted.style.display = data.muted ? 'block' : 'none';
                iconUnmuted.style.display = data.muted ? 'none' : 'block';
            }

            // The API provides rate per minute
            const ratePerMin = data.rate;
            const rateToShow = displayPerMinute ? ratePerMin : (ratePerMin / 60.0);
            fuelRateEl.textContent = rateToShow.toFixed(displayPerMinute ? 1 : 2);
            
            uptimeEl.textContent = data.uptime;
            
            return true;
        } catch (err) {
            return false;
        }
    }

    function updateRateUI() {
        rateLabelEl.textContent = displayPerMinute ? 'RATE PER MIN' : 'RATE PER SEC';
    }

    function toggleUnits() {
        displayPerMinute = !displayPerMinute;
        updateRateUI();
        updateStatus();
    }

    document.getElementById('rate-toggle').addEventListener('click', toggleUnits);

    document.getElementById('btn-mute').addEventListener('click', () => {
        const isMuted = iconMuted.style.display === 'block';
        const nextMuteState = !isMuted;
        const logMsg = nextMuteState ? 'Audio Muted.' : 'Audio Unmuted.';
        postAction(`/api/setMute?mute=${nextMuteState}`, logMsg);
    });

    async function postAction(endpoint, actionName) {
        try {
            const response = await fetch(endpoint, { method: 'POST' });
            
            if (!response.ok) {
                 throw new Error(`Server returned ${response.status}`);
            }

            const data = await response.json();
            addLog(actionName);
            await updateStatus();
        } catch (err) {
            addLog(`Failed ${actionName}: ${err.message}`);
        }
    }

    document.getElementById('btn-reset-count').addEventListener('click', () => {
        postAction('/api/resetCount', 'Count reset triggered.');
    });

    document.getElementById('btn-reset-timer').addEventListener('click', () => {
        postAction('/api/resetTimer', 'Timer reset triggered.');
    });

    // Initial load
    updateStatus();
    addLog('System dashboard initialized.');

    // Auto-refresh every 1/2 second
    setInterval(updateStatus, 500);
});
