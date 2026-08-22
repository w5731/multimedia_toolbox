// 教师端管理 API
const express = require('express');
const bcrypt = require('bcryptjs');
const db = require('../db');
const config = require('../config');
const { requireTeacher, requireAdmin } = require('../middleware/session');
const { nowStr, todayStr, plusMinutes, randomPairingCode, taskAppliesOnDate, DATE_RE } = require('../util');
const { getSettings, bumpDataVersion, audit, validateTask } = require('../helpers');

const router = express.Router();
router.use(requireTeacher);

function onlineStatus(lastSeen) {
  if (!lastSeen) return false;
  return lastSeen >= plusMinutes(-config.onlineSeconds / 60);
}

// ---------- 总览 ----------
router.get('/overview', (req, res) => {
  const classes = db.prepare('SELECT * FROM classes ORDER BY id').all();
  const clients = db.prepare('SELECT * FROM clients ORDER BY id').all();
  const today = todayStr();
  const allTasks = db.prepare('SELECT * FROM tasks WHERE enabled = 1').all();
  const recentCalls = db.prepare(
    `SELECT c.*, t.display_name AS teacher_name, cl.name AS class_name
     FROM calls c LEFT JOIN teachers t ON t.id = c.created_by
     LEFT JOIN classes cl ON cl.id = c.class_id
     ORDER BY c.id DESC LIMIT 10`).all();
  const result = classes.map(cls => ({
    id: cls.id,
    name: cls.name,
    clients: clients.filter(c => c.class_id === cls.id).map(c => ({
      id: c.id, name: c.name, version: c.version, ip: c.ip,
      last_seen: c.last_seen, online: onlineStatus(c.last_seen),
    })),
    today_task_count: allTasks.filter(t => t.class_id === cls.id && taskAppliesOnDate(t, today)).length,
  }));
  res.json({ ok: true, server_time: nowStr(), classes: result, recent_calls: recentCalls });
});

// ---------- 班级 ----------
router.get('/classes', (req, res) => {
  const rows = db.prepare(
    `SELECT c.*, (SELECT COUNT(*) FROM clients WHERE class_id = c.id) AS client_count,
            (SELECT COUNT(*) FROM tasks WHERE class_id = c.id) AS task_count
     FROM classes c ORDER BY c.id`).all();
  res.json({ ok: true, classes: rows });
});

router.post('/classes', (req, res) => {
  const name = String((req.body || {}).name || '').trim().slice(0, 32);
  if (!name) return res.status(400).json({ ok: false, error: '班级名称不能为空' });
  try {
    const r = db.prepare('INSERT INTO classes (name) VALUES (?)').run(name);
    getSettings(r.lastInsertRowid);
    audit(req.teacher, '新建班级', name);
    res.json({ ok: true, id: r.lastInsertRowid });
  } catch (e) {
    res.status(400).json({ ok: false, error: '班级名称已存在' });
  }
});

router.put('/classes/:id', (req, res) => {
  const name = String((req.body || {}).name || '').trim().slice(0, 32);
  if (!name) return res.status(400).json({ ok: false, error: '班级名称不能为空' });
  const r = db.prepare('UPDATE classes SET name = ? WHERE id = ?').run(name, req.params.id);
  if (!r.changes) return res.status(404).json({ ok: false, error: '班级不存在' });
  audit(req.teacher, '重命名班级', name);
  res.json({ ok: true });
});

router.delete('/classes/:id', (req, res) => {
  const id = Number(req.params.id);
  const clientCount = db.prepare('SELECT COUNT(*) AS c FROM clients WHERE class_id = ?').get(id).c;
  if (clientCount > 0) return res.status(400).json({ ok: false, error: '该班级下还有已配对的客户端,请先解绑' });
  db.prepare('DELETE FROM tasks WHERE class_id = ?').run(id);
  db.prepare('DELETE FROM calls WHERE class_id = ?').run(id);
  db.prepare('DELETE FROM notices WHERE class_id = ?').run(id);
  db.prepare('DELETE FROM settings WHERE class_id = ?').run(id);
  db.prepare('DELETE FROM pairing_codes WHERE class_id = ?').run(id);
  const r = db.prepare('DELETE FROM classes WHERE id = ?').run(id);
  if (!r.changes) return res.status(404).json({ ok: false, error: '班级不存在' });
  audit(req.teacher, '删除班级', `id=${id}`);
  res.json({ ok: true });
});

// ---------- 配对码 ----------
router.post('/classes/:id/pairing-code', (req, res) => {
  const cls = db.prepare('SELECT * FROM classes WHERE id = ?').get(req.params.id);
  if (!cls) return res.status(404).json({ ok: false, error: '班级不存在' });
  const code = randomPairingCode();
  const expires = plusMinutes(config.pairingCodeMinutes);
  db.prepare('INSERT INTO pairing_codes (code, class_id, expires_at) VALUES (?,?,?)').run(code, cls.id, expires);
  audit(req.teacher, '生成配对码', `${cls.name} ${code}`);
  res.json({ ok: true, code, expires_at: expires });
});

// ---------- 客户端 ----------
router.get('/clients', (req, res) => {
  const rows = db.prepare(
    `SELECT c.*, cl.name AS class_name FROM clients c LEFT JOIN classes cl ON cl.id = c.class_id ORDER BY c.id`).all();
  res.json({
    ok: true,
    clients: rows.map(c => ({ ...c, token: undefined, online: onlineStatus(c.last_seen) })),
  });
});

router.put('/clients/:id', (req, res) => {
  const { name, class_id } = req.body || {};
  const c = db.prepare('SELECT * FROM clients WHERE id = ?').get(req.params.id);
  if (!c) return res.status(404).json({ ok: false, error: '客户端不存在' });
  if (class_id && !db.prepare('SELECT id FROM classes WHERE id = ?').get(class_id)) {
    return res.status(400).json({ ok: false, error: '目标班级不存在' });
  }
  db.prepare('UPDATE clients SET name = ?, class_id = ? WHERE id = ?')
    .run(String(name || '').slice(0, 64), class_id || c.class_id, c.id);
  if (class_id && class_id !== c.class_id) bumpDataVersion(class_id);
  audit(req.teacher, '修改客户端', `${name || c.name} (id=${c.id})`);
  res.json({ ok: true });
});

router.delete('/clients/:id', (req, res) => {
  const r = db.prepare('DELETE FROM clients WHERE id = ?').run(req.params.id);
  if (!r.changes) return res.status(404).json({ ok: false, error: '客户端不存在' });
  audit(req.teacher, '解绑客户端', `id=${req.params.id}`);
  res.json({ ok: true });
});

// ---------- 任务 ----------
router.get('/tasks', (req, res) => {
  const classId = Number(req.query.class_id);
  if (!classId) return res.status(400).json({ ok: false, error: '缺少 class_id' });
  const rows = db.prepare('SELECT * FROM tasks WHERE class_id = ? ORDER BY start_time, sort, id').all(classId);
  const date = req.query.date;
  if (date && DATE_RE.test(date)) {
    return res.json({ ok: true, tasks: rows.filter(t => t.enabled && taskAppliesOnDate(t, date)) });
  }
  res.json({ ok: true, tasks: rows });
});

router.post('/tasks', (req, res) => {
  const body = req.body || {};
  const classId = Number(body.class_id);
  if (!db.prepare('SELECT id FROM classes WHERE id = ?').get(classId)) {
    return res.status(400).json({ ok: false, error: '班级不存在' });
  }
  const err = validateTask(body);
  if (err) return res.status(400).json({ ok: false, error: err });
  const r = db.prepare(
    `INSERT INTO tasks (class_id, title, remark, start_time, end_time, date_mode, date_start, date_end, weekdays, enabled, sort, created_by)
     VALUES (?,?,?,?,?,?,?,?,?,?,?,?)`).run(
    classId, String(body.title).trim().slice(0, 100), String(body.remark || '').trim().slice(0, 300),
    body.start_time, body.end_time || '',
    body.date_mode || 'daily', body.date_start || '', body.date_end || '',
    body.weekdays || '', body.enabled === false ? 0 : 1, Number(body.sort) || 0, req.teacher.id);
  bumpDataVersion(classId);
  audit(req.teacher, '新建任务', `${body.start_time} ${body.title}`);
  res.json({ ok: true, id: r.lastInsertRowid });
});

router.put('/tasks/:id', (req, res) => {
  const body = req.body || {};
  const task = db.prepare('SELECT * FROM tasks WHERE id = ?').get(req.params.id);
  if (!task) return res.status(404).json({ ok: false, error: '任务不存在' });
  const merged = { ...task, ...body };
  const err = validateTask(merged);
  if (err) return res.status(400).json({ ok: false, error: err });
  db.prepare(
    `UPDATE tasks SET title=?, remark=?, start_time=?, end_time=?, date_mode=?, date_start=?, date_end=?,
     weekdays=?, enabled=?, sort=?, updated_at=? WHERE id=?`).run(
    String(merged.title).trim().slice(0, 100), String(merged.remark || '').trim().slice(0, 300),
    merged.start_time, merged.end_time || '',
    merged.date_mode, merged.date_start || '', merged.date_end || '', merged.weekdays || '',
    merged.enabled ? 1 : 0, Number(merged.sort) || 0, nowStr(), task.id);
  bumpDataVersion(task.class_id);
  audit(req.teacher, '修改任务', `#${task.id} ${merged.title}`);
  res.json({ ok: true });
});

router.delete('/tasks/:id', (req, res) => {
  const task = db.prepare('SELECT * FROM tasks WHERE id = ?').get(req.params.id);
  if (!task) return res.status(404).json({ ok: false, error: '任务不存在' });
  db.prepare('DELETE FROM tasks WHERE id = ?').run(task.id);
  bumpDataVersion(task.class_id);
  audit(req.teacher, '删除任务', `#${task.id} ${task.title}`);
  res.json({ ok: true });
});

// 把某一天实际生效的任务复制为另一些日期的单次任务
router.post('/tasks/copy-day', (req, res) => {
  const { class_id, from_date, to_dates } = req.body || {};
  const classId = Number(class_id);
  if (!DATE_RE.test(from_date || '') || !Array.isArray(to_dates) || !to_dates.length) {
    return res.status(400).json({ ok: false, error: '参数不完整' });
  }
  const validTargets = to_dates.filter(d => DATE_RE.test(d) && d !== from_date).slice(0, 60);
  if (!validTargets.length) return res.status(400).json({ ok: false, error: '目标日期无效' });
  const rows = db.prepare('SELECT * FROM tasks WHERE class_id = ? AND enabled = 1').all(classId);
  const source = rows.filter(t => taskAppliesOnDate(t, from_date));
  if (!source.length) return res.status(400).json({ ok: false, error: '该日期没有可复制的任务' });
  const insert = db.prepare(
    `INSERT INTO tasks (class_id, title, remark, start_time, end_time, date_mode, date_start, enabled, sort, created_by)
     VALUES (?,?,?,?,?,'once',?,1,?,?)`);
  let count = 0;
  const tx = db.transaction(() => {
    for (const d of validTargets) {
      for (const t of source) {
        insert.run(classId, t.title, t.remark || '', t.start_time, t.end_time, d, t.sort, req.teacher.id);
        count++;
      }
    }
  });
  tx();
  bumpDataVersion(classId);
  audit(req.teacher, '复制日程', `${from_date} → ${validTargets.join(',')}`);
  res.json({ ok: true, count });
});

// ---------- 叫号 ----------
router.post('/calls', (req, res) => {
  const { class_id, numbers, destination, reason } = req.body || {};
  const classId = Number(class_id);
  const cls = db.prepare('SELECT * FROM classes WHERE id = ?').get(classId);
  if (!cls) return res.status(400).json({ ok: false, error: '班级不存在' });
  const nums = String(numbers || '').trim().slice(0, 100);
  if (!nums) return res.status(400).json({ ok: false, error: '请填写号数' });
  const dest = String(destination || '办公室').trim().slice(0, 50) || '办公室';
  const r = db.prepare('INSERT INTO calls (class_id, numbers, destination, reason, created_by) VALUES (?,?,?,?,?)')
    .run(classId, nums, dest, String(reason || '').trim().slice(0, 200), req.teacher.id);
  audit(req.teacher, '叫号', `${cls.name}: ${nums} → ${dest}${reason ? ' (' + reason + ')' : ''}`);
  res.json({ ok: true, id: r.lastInsertRowid });
});

router.get('/calls', (req, res) => {
  const classId = Number(req.query.class_id) || 0;
  const limit = Math.min(Number(req.query.limit) || 50, 200);
  const rows = classId
    ? db.prepare(`SELECT c.*, t.display_name AS teacher_name FROM calls c
                  LEFT JOIN teachers t ON t.id = c.created_by
                  WHERE c.class_id = ? ORDER BY c.id DESC LIMIT ?`).all(classId, limit)
    : db.prepare(`SELECT c.*, t.display_name AS teacher_name, cl.name AS class_name FROM calls c
                  LEFT JOIN teachers t ON t.id = c.created_by
                  LEFT JOIN classes cl ON cl.id = c.class_id
                  ORDER BY c.id DESC LIMIT ?`).all(limit);
  res.json({ ok: true, calls: rows });
});

router.post('/calls/:id/cancel', (req, res) => {
  const call = db.prepare('SELECT * FROM calls WHERE id = ?').get(req.params.id);
  if (!call) return res.status(404).json({ ok: false, error: '叫号不存在' });
  if (call.status === 'pending') {
    db.prepare(`UPDATE calls SET status = 'closed', closed_at = ? WHERE id = ?`).run(nowStr(), call.id);
    audit(req.teacher, '撤销叫号', `#${call.id}`);
  }
  res.json({ ok: true });
});

// ---------- 通知栏 ----------
router.get('/notice', (req, res) => {
  const classId = Number(req.query.class_id);
  const row = db.prepare('SELECT * FROM notices WHERE class_id = ?').get(classId)
    || { text: '', enabled: 0 };
  res.json({ ok: true, notice: { text: row.text, enabled: !!row.enabled } });
});

router.put('/notice', (req, res) => {
  const { class_id, text, enabled } = req.body || {};
  const classId = Number(class_id);
  if (!db.prepare('SELECT id FROM classes WHERE id = ?').get(classId)) {
    return res.status(400).json({ ok: false, error: '班级不存在' });
  }
  db.prepare(
    `INSERT INTO notices (class_id, text, enabled, updated_by, updated_at) VALUES (?,?,?,?,?)
     ON CONFLICT(class_id) DO UPDATE SET text=excluded.text, enabled=excluded.enabled,
     updated_by=excluded.updated_by, updated_at=excluded.updated_at`)
    .run(classId, String(text || '').slice(0, 200), enabled ? 1 : 0, req.teacher.id, nowStr());
  bumpDataVersion(classId);
  audit(req.teacher, enabled ? '发布通知' : '关闭通知', String(text || '').slice(0, 50));
  res.json({ ok: true });
});

// ---------- 客户端远程设置 ----------
router.get('/settings', (req, res) => {
  const classId = Number(req.query.class_id);
  if (!classId) return res.status(400).json({ ok: false, error: '缺少 class_id' });
  const s = getSettings(classId);
  res.json({
    ok: true,
    settings: {
      popup_seconds: s.popup_seconds, volume: s.volume,
      overlay_position: s.overlay_position, font_scale: s.font_scale,
    },
  });
});

router.put('/settings', (req, res) => {
  const { class_id, popup_seconds, volume, overlay_position, font_scale } = req.body || {};
  const classId = Number(class_id);
  if (!db.prepare('SELECT id FROM classes WHERE id = ?').get(classId)) {
    return res.status(400).json({ ok: false, error: '班级不存在' });
  }
  const ps = Math.min(Math.max(parseInt(popup_seconds, 10) || 10, 3), 120);
  const vol = Math.min(Math.max(parseInt(volume, 10) || 50, 0), 100);
  const pos = ['right', 'left', 'top'].includes(overlay_position) ? overlay_position : 'right';
  const fs = Math.min(Math.max(parseFloat(font_scale) || 1, 0.6), 2);
  getSettings(classId);
  db.prepare(
    `UPDATE settings SET popup_seconds=?, volume=?, overlay_position=?, font_scale=?, updated_at=? WHERE class_id=?`)
    .run(ps, vol, pos, fs, nowStr(), classId);
  bumpDataVersion(classId);
  audit(req.teacher, '修改客户端设置', `弹窗${ps}s 音量${vol}% 位置${pos} 字号x${fs}`);
  res.json({ ok: true });
});

// ---------- 教师账号(仅管理员) ----------
router.get('/teachers', requireAdmin, (req, res) => {
  const rows = db.prepare('SELECT id, username, display_name, is_admin, created_at FROM teachers ORDER BY id').all();
  res.json({ ok: true, teachers: rows });
});

router.post('/teachers', requireAdmin, (req, res) => {
  const { username, password, display_name, is_admin } = req.body || {};
  if (!username || !password) return res.status(400).json({ ok: false, error: '账号和密码不能为空' });
  if (String(password).length < 6) return res.status(400).json({ ok: false, error: '密码至少 6 位' });
  try {
    const r = db.prepare('INSERT INTO teachers (username, password_hash, display_name, is_admin) VALUES (?,?,?,?)')
      .run(String(username).trim().slice(0, 32), bcrypt.hashSync(String(password), 10),
        String(display_name || '').slice(0, 32), is_admin ? 1 : 0);
    audit(req.teacher, '新建教师账号', username);
    res.json({ ok: true, id: r.lastInsertRowid });
  } catch (e) {
    res.status(400).json({ ok: false, error: '账号名已存在' });
  }
});

router.put('/teachers/:id', requireAdmin, (req, res) => {
  const t = db.prepare('SELECT * FROM teachers WHERE id = ?').get(req.params.id);
  if (!t) return res.status(404).json({ ok: false, error: '账号不存在' });
  const { display_name, is_admin, password } = req.body || {};
  db.prepare('UPDATE teachers SET display_name = ?, is_admin = ? WHERE id = ?')
    .run(String(display_name || '').slice(0, 32), is_admin ? 1 : 0, t.id);
  if (password) {
    if (String(password).length < 6) return res.status(400).json({ ok: false, error: '密码至少 6 位' });
    db.prepare('UPDATE teachers SET password_hash = ? WHERE id = ?')
      .run(bcrypt.hashSync(String(password), 10), t.id);
    db.prepare('DELETE FROM sessions WHERE teacher_id = ?').run(t.id);
  }
  audit(req.teacher, '修改教师账号', t.username);
  res.json({ ok: true });
});

router.delete('/teachers/:id', requireAdmin, (req, res) => {
  const id = Number(req.params.id);
  if (id === req.teacher.id) return res.status(400).json({ ok: false, error: '不能删除自己' });
  const admins = db.prepare('SELECT COUNT(*) AS c FROM teachers WHERE is_admin = 1').get().c;
  const t = db.prepare('SELECT * FROM teachers WHERE id = ?').get(id);
  if (!t) return res.status(404).json({ ok: false, error: '账号不存在' });
  if (t.is_admin && admins <= 1) return res.status(400).json({ ok: false, error: '至少保留一个管理员' });
  db.prepare('DELETE FROM sessions WHERE teacher_id = ?').run(id);
  db.prepare('DELETE FROM teachers WHERE id = ?').run(id);
  audit(req.teacher, '删除教师账号', t.username);
  res.json({ ok: true });
});

// ---------- 审计日志 ----------
router.get('/audit-log', (req, res) => {
  const limit = Math.min(Number(req.query.limit) || 100, 500);
  const rows = db.prepare('SELECT * FROM audit_log ORDER BY id DESC LIMIT ?').all(limit);
  res.json({ ok: true, logs: rows });
});

module.exports = router;
