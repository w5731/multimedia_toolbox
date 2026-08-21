// 客户端 API:配对 / 心跳 / 数据拉取 / 叫号回执
const express = require('express');
const db = require('../db');
const config = require('../config');
const { nowStr, plusMinutes, randomToken } = require('../util');
const { getSettings } = require('../helpers');

const router = express.Router();

// 客户端身份校验
function clientAuth(req, res, next) {
  const params = Object.assign({}, req.query, req.body);
  const { client_id, token } = params;
  if (!client_id || !token) return res.status(401).json({ ok: false, error: '缺少客户端凭据' });
  const client = db.prepare('SELECT * FROM clients WHERE id = ? AND token = ?').get(Number(client_id), String(token));
  if (!client) return res.status(401).json({ ok: false, error: '客户端凭据无效,请重新配对' });
  req.client = client;
  next();
}

// 过期叫号:pending 超过 callExpireMinutes 分钟未接收 → expired
function expireOldCalls(classId) {
  const deadline = plusMinutes(-config.callExpireMinutes);
  db.prepare(`UPDATE calls SET status = 'expired' WHERE class_id = ? AND status = 'pending' AND created_at < ?`)
    .run(classId, deadline);
}

// 配对:客户端提交配对码 + 机器码,换取长期凭据
router.post('/pair', (req, res) => {
  const { code, machine_code, name } = req.body || {};
  if (!code || !machine_code) return res.status(400).json({ ok: false, error: '缺少配对码或机器码' });
  const pc = db.prepare('SELECT * FROM pairing_codes WHERE code = ?').get(String(code).trim());
  if (!pc || pc.used || pc.expires_at < nowStr()) {
    return res.status(400).json({ ok: false, error: '配对码无效或已过期,请在教师端重新生成' });
  }
  const cls = db.prepare('SELECT * FROM classes WHERE id = ?').get(pc.class_id);
  if (!cls) return res.status(400).json({ ok: false, error: '配对码对应班级不存在' });

  const token = randomToken();
  const mc = String(machine_code).slice(0, 128);
  const clientName = String(name || '').slice(0, 64);
  const existing = db.prepare('SELECT * FROM clients WHERE machine_code = ?').get(mc);
  let clientId;
  if (existing) {
    // 同一台机器重新配对:更新班级并换发新凭据
    db.prepare('UPDATE clients SET class_id = ?, token = ?, name = ? WHERE id = ?')
      .run(cls.id, token, clientName || existing.name, existing.id);
    clientId = existing.id;
  } else {
    const r = db.prepare('INSERT INTO clients (class_id, name, machine_code, token) VALUES (?,?,?,?)')
      .run(cls.id, clientName, mc, token);
    clientId = r.lastInsertRowid;
  }
  db.prepare('UPDATE pairing_codes SET used = 1 WHERE code = ?').run(pc.code);
  getSettings(cls.id); // 确保设置行存在
  res.json({ ok: true, client_id: clientId, token, class_name: cls.name, server_time: nowStr() });
});

// 心跳:客户端每 3 秒一次
router.post('/heartbeat', clientAuth, (req, res) => {
  const c = req.client;
  db.prepare('UPDATE clients SET last_seen = ?, ip = ?, version = ? WHERE id = ?')
    .run(nowStr(), String(req.ip || '').slice(0, 64), String(req.body.version || '').slice(0, 32), c.id);
  if (c.class_id) expireOldCalls(c.class_id);
  const call = c.class_id
    ? db.prepare(`SELECT id, numbers, destination, reason, created_at FROM calls
                  WHERE class_id = ? AND status = 'pending' ORDER BY id LIMIT 1`).get(c.class_id)
    : null;
  const s = c.class_id ? getSettings(c.class_id) : { data_version: 0 };
  res.json({ ok: true, server_time: nowStr(), data_version: s.data_version, pending_call: call || null });
});

// 全量数据:任务 + 通知 + 设置(版本号变化时客户端重新拉取)
router.get('/data', clientAuth, (req, res) => {
  const c = req.client;
  if (!c.class_id) return res.json({ ok: true, server_time: nowStr(), unbound: true, settings: null, notice: null, tasks: [], data_version: 0 });
  const tasks = db.prepare(
    `SELECT id, title, start_time, end_time, date_mode, date_start, date_end, weekdays
     FROM tasks WHERE class_id = ? AND enabled = 1 ORDER BY start_time, sort, id`).all(c.class_id);
  const notice = db.prepare('SELECT text, enabled FROM notices WHERE class_id = ?').get(c.class_id)
    || { text: '', enabled: 0 };
  const s = getSettings(c.class_id);
  res.json({
    ok: true,
    server_time: nowStr(),
    settings: {
      popup_seconds: s.popup_seconds,
      volume: s.volume,
      overlay_position: s.overlay_position,
      font_scale: s.font_scale,
    },
    notice,
    tasks,
    data_version: s.data_version,
  });
});

// 叫号回执:event = shown | closed
router.post('/call-ack', clientAuth, (req, res) => {
  const { call_id, event } = req.body || {};
  const call = db.prepare('SELECT * FROM calls WHERE id = ? AND class_id = ?')
    .get(Number(call_id), req.client.class_id);
  if (!call) return res.status(404).json({ ok: false, error: '叫号不存在' });
  if (event === 'shown' && call.status === 'pending') {
    db.prepare(`UPDATE calls SET status = 'shown', shown_at = ? WHERE id = ?`).run(nowStr(), call.id);
  } else if (event === 'closed' && (call.status === 'shown' || call.status === 'pending')) {
    db.prepare(`UPDATE calls SET status = 'closed', closed_at = ? WHERE id = ?`).run(nowStr(), call.id);
  }
  res.json({ ok: true });
});

module.exports = router;
