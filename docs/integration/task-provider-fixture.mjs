// WI-007 only: deterministic loopback Chat Completions fixture, never a real model.
import http from 'node:http';

let calls = [];
const server = http.createServer(async (request, response) => {
  if (request.url === '/stats') {
    response.setHeader('Content-Type', 'application/json');
    response.end(JSON.stringify(calls));
    return;
  }
  if (request.url !== '/v1/chat/completions' || request.method !== 'POST') {
    response.writeHead(404).end();
    return;
  }
  let raw = '';
  for await (const chunk of request) raw += chunk;
  const input = JSON.parse(raw);
  const user = input.messages.filter(x => x.role === 'user').at(-1);
  const text = JSON.stringify(user?.content ?? '');
  const mode = ['stall', 'fail', 'tool', 'question'].find(x => text.includes(`WI007:${x}`)) ?? 'complete';
  calls.push({ mode, tools: input.tools?.map(x => x.function.name) ?? [] });
  if (mode === 'fail') {
    response.writeHead(400, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify({ error: { message: 'WI007 controlled provider rejection', type: 'invalid_request_error' } }));
    return;
  }
  response.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache' });
  const send = (delta, finish = null) => response.write(`data: ${JSON.stringify({
    id: 'chatcmpl-wi007', object: 'chat.completion.chunk', created: 1700000000,
    model: input.model, choices: [{ index: 0, delta, finish_reason: finish }]
  })}\n\n`);
  send({ role: 'assistant', content: '' });
  if (mode === 'stall') {
    send({ content: 'WI007 waiting for interruption.' });
    const timer = setInterval(() => response.write(': fixture heartbeat\n\n'), 1000);
    response.on('close', () => clearInterval(timer));
    return;
  }
  const last = input.messages.at(-1);
  if (['tool', 'question'].includes(mode) && last.role !== 'tool') {
    send({ tool_calls: [{ index: 0, id: 'call_wi007', type: 'function', function: {
      name: mode === 'tool' ? 'read' : 'question',
      arguments: JSON.stringify(mode === 'tool' ? { path: 'fixture.txt' } : {
        questions: [{ question: 'Choose the fixture answer', header: 'Fixture', options: [
          { label: 'Alpha', description: 'First fixture option' }, { label: 'Beta', description: 'Second fixture option' }
        ] }]
      })
    } }] });
    send({}, 'tool_calls');
  } else {
    send({ content: 'WI007 fixture answer.\n```text\nverified fixture\n```' });
    send({}, 'stop');
  }
  response.end('data: [DONE]\n\n');
});
server.listen(0, '127.0.0.1', () => console.log(`http://127.0.0.1:${server.address().port}`));
