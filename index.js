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

// Correct path for Azure App Service
const ROOT_PATH = path.join(__dirname, '..', 'wwwroot');
const INFO_PATH = path.join(ROOT_PATH, 'data', 'info.json');
const HTML_PATH = path.join(ROOT_PATH, 'public', 'index.html');

// Serve static files
app.use(express.static(path.join(ROOT_PATH, 'public')));

// Basic authentication for admin
app.use('/admin', basicAuth({
    users: { '': '' },
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
        'info.json': INFO_PATH
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
        // Force open the files
        const indexFd = fs.openSync(HTML_PATH, 'r');
        const infoFd = fs.openSync(INFO_PATH, 'r');

        const indexContent = fs.readFileSync(indexFd, 'utf8') || 'index.html not found';
        const infoContent = fs.readFileSync(infoFd, 'utf8') || 'info.json not found';

        fs.closeSync(indexFd);
        fs.closeSync(infoFd);

        console.log('HTML Path:', HTML_PATH);
        console.log('JSON Path:', INFO_PATH);

        const files = {
            index: indexContent,
            info: infoContent,
            logs: [
                `index.html content: ${indexContent.substring(0, 200)}...`,
                `info.json content: ${infoContent.substring(0, 200)}...`
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

// WebSocket broadcast for live updates
wss.on('connection', (ws) => {
    fs.readFile(INFO_PATH, 'utf8', (err, data) => {
        if (!err) {
            ws.send(JSON.stringify({ type: 'update', items: JSON.parse(data) }));
        }
    });
});

server.listen(PORT, () => {
    console.log(`Server running on http://localhost:${PORT}`);
});
