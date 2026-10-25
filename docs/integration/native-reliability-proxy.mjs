// WI-014 disposable loopback proxy: lose one accepted prompt response, count retries.
// Never log headers, credentials or prompt bodies. Use only with an isolated fixture.
import http from 'node:http';

const target = new URL(process.argv[2]);
if (target.protocol !== 'http:' || target.hostname !== '127.0.0.1') {
  throw new Error('The native reliability proxy requires an IPv4 loopback fixture.');
}
let dropNext = false;
let delayReply = false;
const prompts = [];
const server = http.createServer((request, response) => {
  if (request.url === '/__fixture/stats') {
    response.writeHead(200, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify({ prompts }));
    return;
  }
  if (request.url === '/__fixture/drop-next' && request.method === 'POST') {
    dropNext = true;
    response.writeHead(204).end();
    return;
  }
  if (request.url === '/__fixture/delay-next-reply' && request.method === 'POST') {
    delayReply = true;
    response.writeHead(204).end();
    return;
  }
  const delay = delayReply && request.method === 'POST' && /\/(permission|form)\/[^/]+\/reply$/.test(request.url);
  if (delay) delayReply = false;
  const prompt = request.method === 'POST' && /^\/api\/session\/[^/]+\/prompt$/.test(request.url);
  const drop = prompt && dropNext;
  if (prompt) {
    prompts.push({ path: request.url, responseDropped: drop });
    dropNext = false;
  }
  const upstream = http.request(new URL(request.url, target), {
    method: request.method, headers: { ...request.headers, host: target.host }
  }, incoming => {
    if (drop) {
      // Consume the upstream response before disconnecting, proving acceptance.
      incoming.resume();
      incoming.on('end', () => {
        prompts.at(-1).upstreamStatus = incoming.statusCode;
        response.destroy();
      });
      return;
    }
    response.writeHead(incoming.statusCode, incoming.headers);
    incoming.pipe(response);
  });
  upstream.on('error', () => {
    if (!response.headersSent) response.writeHead(503);
    response.end();
  });
  if (delay) setTimeout(() => request.pipe(upstream), 4000);
  else request.pipe(upstream);
  response.on('close', () => upstream.destroy());
});
server.listen(Number(process.argv[3] ?? 0), '127.0.0.1', () => console.log(`http://127.0.0.1:${server.address().port}`));
