const express = require('express');
const fs = require('fs');
const http = require('http');
const WebSocket = require('ws');
const path = require('path');
const basicAuth = require('express-basic-auth');
const { v4: uuidv4 } = require('uuid');

const app = express();
const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

const PORT = process.env.PORT || 8080;

// Correct path for Azure App Service
const ROOT_PATH = path.join(__dirname, '..', 'wwwroot');
const INFO_PATH = path.join(ROOT_PATH, 'data', 'info.json');
const HTML_PATH = path.join(ROOT_PATH, 'public', 'index.html');
const ADMIN_PATH = path.join(ROOT_PATH, 'public', 'admin.html');

const wsConnections = [];

// Session management
const sessions = {};

// Middleware for JSON parsing
app.use(express.json());

// Serve static files
app.use(express.static(path.join(ROOT_PATH, 'public')));

// Basic authentication for admin
app.use('/admin', basicAuth({
    users: { '' : '' },
    challenge: true,
    unauthorizedResponse: 'Unauthorized'
}));

// Admin interface to edit files
app.get('/admin', (req, res) => {
    res.sendFile(path.join(ROOT_PATH, 'public', 'admin.html'));
});

app.post('/admin/api/save', express.json(), (req, res) => {
    const { filename, content } = req.body;

    const validFiles = {
        'index.html': HTML_PATH,
        'info.json': INFO_PATH,
        'admin.html': ADMIN_PATH
    };

    if (validFiles[filename]) {
        fs.writeFile(validFiles[filename], content, (err) => {
            if (err) {
                console.error('Failed to save file:', err.message);
                return res.status(500).send('Failed to save file.');
            }
            res.status(200).send('File saved successfully.');
        });
    } else {
        res.status(400).send('Invalid file.');
    }
});

app.get('/admin/api/files', (req, res) => {
    try {
        const indexContent = fs.readFileSync(HTML_PATH, 'utf8') || 'index.html not found';
        const infoContent = fs.readFileSync(INFO_PATH, 'utf8') || 'info.json not found';
        const adminContent = fs.readFileSync(ADMIN_PATH, 'utf8') || 'admin.html not found';

        const files = {
            index: indexContent,
            info: infoContent,
            admin: adminContent,
            logs: [
                `index.html content: ${indexContent.substring(0, 200)}...`,
                `info.json content: ${infoContent.substring(0, 200)}...`,
		`admin.html content: ${adminContent.substring(0, 200)}...`
            ]
        };
        res.json(files);
    } catch (err) {
        res.status(500).json({ error: 'Failed to load files.', details: err.message });
    }
});

// Serve info.json as raw JSON
app.get('/info.json', (req, res) => {
    res.sendFile(INFO_PATH, {
        headers: {
            'Content-Type': 'application/json',
            'Content-Disposition': 'inline'
        }
    });
});

// Serve index.html as raw HTML
app.get('/raw-index', (req, res) => {
    res.sendFile(HTML_PATH, {
        headers: {
            'Content-Type': 'text/html',
            'Content-Disposition': 'inline'
        }
    });
});

// Start a new session
app.post('/session/start', (req, res) => {
    const uuid = uuidv4();
    sessions[uuid] = { createdAt: Date.now() };
    res.json({ UUID: uuid });
});

// End an existing session
app.post('/session/end', (req, res) => {
    const { UUID } = req.body;
    if (sessions[UUID]) {
        delete sessions[UUID];
        res.json({ success: true });
    } else {
        res.status(404).json({ success: false, message: 'Session not found' });
    }
});

// Handle item spawning requests
app.get('/:UUID/spawn', (req, res) => {
    const { UUID } = req.params;
    if (!sessions[UUID]) {
        return res.status(403).json({ success: false, message: 'Invalid or expired session' });
    }

    const { item, quantity } = req.query;
    const qty = parseInt(quantity, 10) || 1;

    // Forward the item spawn request to the WebSocket client (mod) associated with this UUID
    const ws = wsConnections[UUID];
    if (ws) {
        const message = JSON.stringify({
            action: 'spawnItem',
            itemId: item,
            quantity: qty
        });

        ws.send(message); // Send the message to the mod WebSocket
        res.json({ success: true, message: `Item ${item} (x${qty}) spawned.` });
    } else {
        res.status(404).json({ success: false, message: 'No active WebSocket connection for this session.' });
    }
});

// WebSocket connection handler
wss.on('connection', (ws, req) => {
    const uuid = req.url.split('?UUID=')[1];
    if (uuid) {
        wsConnections[uuid] = ws; // Map the UUID to the WebSocket connection

        ws.on('message', (message) => {
            console.log(`Received message from ${uuid}: ${message}`);
        });

        ws.on('close', () => {
            delete wsConnections[uuid]; // Cleanup on disconnect
        });

        console.log(`WebSocket connected for session ${uuid}`);
    }
});

server.listen(PORT, () => {
    console.log(`Server running on http://localhost:${PORT}`);
});
