const express = require('express');
const fs = require('fs');
const http = require('http');
const WebSocket = require('ws');
const path = require('path');
const basicAuth = require('express-basic-auth');

const app = express();
const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

const PORT = process.env.PORT || 8080;
const INFO_PATH = path.join(__dirname, 'data', 'info.json');
const HTML_PATH = path.join(__dirname, 'public', 'index.html');

// Serve static files
app.use(express.static(path.join(__dirname, 'public')));

// Basic authentication for admin
app.use('/admin', basicAuth({
    users: { 'x': 'xxxxxx' },
    challenge: true,
    unauthorizedResponse: 'Unauthorized'
}));

// Admin interface to edit files
app.get('/admin', (req, res) => {
    res.sendFile(path.join(__dirname, 'public', 'admin.html'));
});

app.get('/admin/api/files', (req, res) => {
    try {
        const files = {
            index: fs.readFileSync(HTML_PATH, 'utf8') || 'File not found',
            info: fs.readFileSync(INFO_PATH, 'utf8') || 'File not found'
        };
        res.json(files);
    } catch (err) {
        console.error('Error reading files:', err.message);
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

app.post('/admin/api/save', express.json(), (req, res) => {
    const { filename, content } = req.body;

    const validFiles = {
        'index.html': HTML_PATH,
        'info.json': INFO_PATH
    };

    if (validFiles[filename]) {
        fs.writeFile(validFiles[filename], content, (err) => {
            if (err) return res.status(500).send('Failed to save file.');
            broadcast({ type: 'update', items: JSON.parse(content) });
            res.status(200).send('File saved successfully.');
        });
    } else {
        res.status(400).send('Invalid file.');
    }
});

// WebSocket broadcast for live updates
wss.on('connection', (ws) => {
    fs.readFile(INFO_PATH, 'utf8', (err, data) => {
        if (!err) {
            ws.send(JSON.stringify({ type: 'update', items: JSON.parse(data) }));
        }
    });
});

function broadcast(message) {
    wss.clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(JSON.stringify(message));
        }
    });
}

server.listen(PORT, () => {
    console.log(`Server running on http://localhost:${PORT}`);
});