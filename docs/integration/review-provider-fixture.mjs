// Deterministic loopback-only provider. Writes only the verifier-owned fixture root.
import http from 'node:http';

const root = process.env.WI016_FIXTURE_ROOT;
if (!root) throw new Error('WI016_FIXTURE_ROOT is required');
let turn = 0;
const server = http.createServer(async (request, response) => {
  if (request.url !== '/v1/chat/completions' || request.method !== 'POST') {
    response.writeHead(404).end(); return;
  }
  let raw = '';
  for await (const chunk of request) raw += chunk;
  const input = JSON.parse(raw);
  const writing = input.messages.at(-1)?.role !== 'tool';
  const delta = writing ? { role: 'assistant', tool_calls: [{ index: 0, id: `call_wi016_${++turn}`, type: 'function', function: {
    name: 'write', arguments: JSON.stringify({ path: 'modified.txt', content: `controlled turn ${turn}\n` })
  } }] } : { role: 'assistant', content: 'Controlled review fixture completed.' };
  response.writeHead(200, { 'Content-Type': 'text/event-stream' });
  for (const [part, finish] of [[delta, null], [{}, writing ? 'tool-calls' : 'stop']]) {
    response.write(`data: ${JSON.stringify({ id: 'chatcmpl-wi016', object: 'chat.completion.chunk', created: 1700000000,
      model: 'fixture', choices: [{ index: 0, delta: part, finish_reason: finish === 'tool-calls' ? 'tool_calls' : finish }] })}\n\n`);
  }
  response.end('data: [DONE]\n\n');
});
server.listen(0, '127.0.0.1', () => console.log(`http://127.0.0.1:${server.address().port}`));
