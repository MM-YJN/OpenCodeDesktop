// Loopback-only deterministic fixture; captures synthetic provider input without inference.
import http from 'node:http';
import fs from 'node:fs';
const capture = process.env.WI017_CAPTURE;
if (!capture) throw new Error('WI017_CAPTURE is required');
const server = http.createServer(async (request, response) => {
  if (request.url !== '/v1/chat/completions' || request.method !== 'POST') {
    response.writeHead(404).end(); return;
  }
  let raw = '';
  for await (const chunk of request) raw += chunk;
  fs.writeFileSync(capture, JSON.stringify(JSON.parse(raw), null, 2));
  response.writeHead(200, { 'Content-Type': 'text/event-stream' });
  for (const [delta, finish] of [[{ role: 'assistant', content: 'Attachment fixture completed.' }, null], [{}, 'stop']]) {
    response.write(`data: ${JSON.stringify({ id: 'chatcmpl-wi017', object: 'chat.completion.chunk', created: 1700000000,
      model: 'fixture', choices: [{ index: 0, delta, finish_reason: finish }] })}\n\n`);
  }
  response.end('data: [DONE]\n\n');
});
server.listen(0, '127.0.0.1', () => console.log(`http://127.0.0.1:${server.address().port}`));
