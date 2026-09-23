// WhatsApp gateway for the AutoParts ERP assistant.
//
// Transport only: receives WhatsApp text messages, forwards them to the ERP API over the private Docker network, and sends back the
// reply the API returns. It has no database access, no business logic and no knowledge of who is allowed: the API decides whether to
// answer (unknown numbers get no reply). The WhatsApp session (linked-device keys) is kept in /data, a Docker volume.
//
// Pairing: the API's assistant screen shows the QR this process reports (also printed to the container log).
import makeWASocket, { DisconnectReason, fetchLatestBaileysVersion, useMultiFileAuthState } from '@whiskeysockets/baileys';
import pino from 'pino';

const API_URL = (process.env.API_URL || 'http://api:8080').replace(/\/$/, '');
const SECRET = process.env.GATEWAY_SECRET || '';
const AUTH_DIR = process.env.AUTH_DIR || '/data/auth';
const log = pino({ level: process.env.LOG_LEVEL || 'info' });

if (SECRET.length < 24) {
  log.error('GATEWAY_SECRET is missing or too short (24+ characters); refusing to start.');
  process.exit(1);
}

const headers = { 'Content-Type': 'application/json', 'X-Gateway-Secret': SECRET };
let status = { state: 'starting', qr: null, account: null };

async function report(next) {
  status = { ...status, ...next };
  try {
    await fetch(`${API_URL}/internal/assistant/gateway/status`, { method: 'POST', headers, body: JSON.stringify(status), signal: AbortSignal.timeout(10000) });
  } catch (err) {
    log.warn({ err: err.message }, 'could not report status to the API');
  }
}
setInterval(() => report({}), 60_000); // heartbeat: the admin screen shows "offline" when reports stop

async function ask(msg) {
  const res = await fetch(`${API_URL}/internal/assistant/whatsapp/inbound`, { method: 'POST', headers, body: JSON.stringify(msg), signal: AbortSignal.timeout(45000) });
  if (!res.ok) throw new Error(`API ${res.status}`);
  return (await res.json()).reply ?? null;
}

function textOf(m) {
  const c = m.message || {};
  return c.conversation || c.extendedTextMessage?.text || c.ephemeralMessage?.message?.extendedTextMessage?.text || c.ephemeralMessage?.message?.conversation || '';
}

async function start() {
  const { state, saveCreds } = await useMultiFileAuthState(AUTH_DIR);
  const { version } = await fetchLatestBaileysVersion().catch(() => ({ version: undefined }));
  const sock = makeWASocket({ auth: state, version, logger: log.child({ module: 'baileys' }, { level: 'warn' }), markOnlineOnConnect: false, syncFullHistory: false });

  sock.ev.on('creds.update', saveCreds);

  sock.ev.on('connection.update', async ({ connection, lastDisconnect, qr }) => {
    if (qr) {
      log.info('Scan the QR from the ERP assistant screen (Settings → WhatsApp assistant) to pair this gateway.');
      await report({ state: 'qr', qr, account: null });
    }
    if (connection === 'open') {
      log.info({ account: sock.user?.id }, 'connected');
      await report({ state: 'open', qr: null, account: sock.user?.id?.split(':')[0] ?? null });
    }
    if (connection === 'close') {
      const code = lastDisconnect?.error?.output?.statusCode;
      if (code === DisconnectReason.loggedOut) {
        // Unlinked from the phone: the stored session is useless; start over with a new QR.
        log.warn('logged out from the phone; clearing the session and waiting for a new pairing');
        const { rm } = await import('node:fs/promises');
        await rm(AUTH_DIR, { recursive: true, force: true });
      }
      await report({ state: 'close', qr: null });
      setTimeout(() => start().catch((err) => log.error({ err }, 'restart failed')), code === DisconnectReason.loggedOut ? 1000 : 5000);
    }
  });

  sock.ev.on('messages.upsert', async ({ messages, type }) => {
    if (type !== 'notify') return;
    for (const m of messages) {
      const jid = m.key?.remoteJid || '';
      // One-to-one chats only: no groups, broadcasts, status updates or our own messages.
      if (m.key?.fromMe || !jid || jid.endsWith('@g.us') || jid.endsWith('@broadcast') || jid === 'status@broadcast' || jid.endsWith('@newsletter')) continue;
      const text = textOf(m).trim();
      if (!text) continue;
      // When WhatsApp addresses the chat by a private id (@lid), the phone-number id may come as an alternative.
      const alt = m.key.remoteJidAlt || m.key.senderPn || null;
      // Nothing visible (read receipt, typing, error text) unless the API answers: unknown senders must not learn a bot is here.
      try {
        const reply = await ask({ messageId: m.key.id, from: jid, fromAlt: alt, text });
        if (reply) {
          await sock.readMessages([m.key]);
          await sock.sendMessage(jid, { text: reply }, { quoted: m });
        }
      } catch (err) {
        log.error({ err: err.message }, 'could not answer a message (API unreachable?)');
      }
    }
  });
}

start().catch((err) => {
  log.error({ err }, 'gateway failed to start');
  process.exit(1);
});
