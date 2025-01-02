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
  users: { 'B': 'Bsherry171' },
  challenge: true,
  unauthorizedResponse: 'Unauthorized'
}));

app.get('/', (req, res) => {
  res.sendFile(HTML_PATH);
});

// Admin interface to edit files
app.get('/admin', (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'admin.html'));
});

app.get('/admin/api/files', (req, res) => {
  const files = {
    index: fs.readFileSync(HTML_PATH, 'utf8'),
    info: fs.readFileSync(INFO_PATH, 'utf8')
  };
  res.json(files);
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
