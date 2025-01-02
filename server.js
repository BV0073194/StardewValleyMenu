
const express = require('express');
const fs = require('fs');
const http = require('http');
const WebSocket = require('ws');
const path = require('path');

const app = express();
const server = http.createServer(app);
const wss = new WebSocket.Server({ server });
const PORT = process.env.PORT || 8080;
const INFO_PATH = path.join(__dirname, 'data', 'info.json');

app.use(express.static(path.join(__dirname, 'public')));

app.get('/api/items', (req, res) => {
    fs.readFile(INFO_PATH, 'utf8', (err, data) => {
        if (err) return res.status(500).json({ error: 'Failed to read item list.' });
        res.json(JSON.parse(data));
    });
});

fs.watch(INFO_PATH, () => {
    fs.readFile(INFO_PATH, 'utf8', (err, data) => {
        if (!err) broadcast({ type: 'update', items: JSON.parse(data) });
    });
});

wss.on('connection', (ws) => {
    fs.readFile(INFO_PATH, 'utf8', (err, data) => {
        if (!err) ws.send(JSON.stringify({ type: 'update', items: JSON.parse(data) }));
    });
});

function broadcast(message) {
    wss.clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) client.send(JSON.stringify(message));
    });
}

server.listen(PORT, () => {
    console.log(`Server running on http://localhost:${PORT}`);
});
