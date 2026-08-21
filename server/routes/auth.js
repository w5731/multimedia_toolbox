// 教师登录 / 登出 / 修改密码
const express = require('express');
const bcrypt = require('bcryptjs');
const db = require('../db');
const config = require('../config');
const { nowStr, plusHours, plusMinutes, randomToken } = require('../util');
const { audit } = require('../helpers');

const router = express.Router();

// 简单登录限频:同一 IP+账号 连续失败后锁定
const fails = new Map(); // key -> { count, lockedUntil }
function failKey(req, username) { return `${req.ip}|${username}`; }
function isLocked(req, username) {
  const f = fails.get(failKey(req, username));
  return f && f.lockedUntil && f.lockedUntil > nowStr();
}
function recordFail(req, username) {
  const key = failKey(req, username);
  const f = fails.get(key) || { count: 0, lockedUntil: null };
  f.count += 1;
  if (f.count >= config.loginMaxFails) {
    f.lockedUntil = plusMinutes(config.loginLockMinutes);
    f.count = 0;
  }
  fails.set(key, f);
}
function clearFails(req, username) { fails.delete(failKey(req, username)); }

router.post('/login', (req, res) => {
  const { username, password } = req.body || {};
  if (!username || !password) return res.status(400).json({ ok: false, error: '请输入账号和密码' });
  if (isLocked(req, username)) {
    return res.status(429).json({ ok: false, error: `失败次数过多,请 ${config.loginLockMinutes} 分钟后再试` });
  }
  const teacher = db.prepare('SELECT * FROM teachers WHERE username = ?').get(String(username).trim());
  if (!teacher || !bcrypt.compareSync(String(password), teacher.password_hash)) {
    recordFail(req, username);
    return res.status(401).json({ ok: false, error: '账号或密码错误' });
  }
  clearFails(req, username);
  const token = randomToken();
  db.prepare('INSERT INTO sessions (token, teacher_id, expires_at) VALUES (?,?,?)')
    .run(token, teacher.id, plusHours(config.sessionTtlHours));
  res.setHeader('Set-Cookie',
    `mt_session=${token}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${config.sessionTtlHours * 3600}`);
  audit({ id: teacher.id, username: teacher.username, display_name: teacher.display_name }, '登录', '');
  res.json({
    ok: true,
    teacher: { id: teacher.id, username: teacher.username, display_name: teacher.display_name, is_admin: teacher.is_admin },
  });
});

router.post('/logout', (req, res) => {
  if (req.sessionToken) db.prepare('DELETE FROM sessions WHERE token = ?').run(req.sessionToken);
  res.setHeader('Set-Cookie', 'mt_session=; Path=/; HttpOnly; Max-Age=0');
  res.json({ ok: true });
});

router.get('/me', (req, res) => {
  if (!req.teacher) return res.status(401).json({ ok: false });
  res.json({ ok: true, teacher: req.teacher });
});

// 修改自己的密码
router.post('/change-password', require('../middleware/session').requireTeacher, (req, res) => {
  const { old_password, new_password } = req.body || {};
  if (!new_password || String(new_password).length < 6) {
    return res.status(400).json({ ok: false, error: '新密码至少 6 位' });
  }
  const row = db.prepare('SELECT * FROM teachers WHERE id = ?').get(req.teacher.id);
  if (!bcrypt.compareSync(String(old_password || ''), row.password_hash)) {
    return res.status(400).json({ ok: false, error: '原密码错误' });
  }
  db.prepare('UPDATE teachers SET password_hash = ? WHERE id = ?')
    .run(bcrypt.hashSync(String(new_password), 10), req.teacher.id);
  db.prepare('DELETE FROM sessions WHERE teacher_id = ? AND token != ?').run(req.teacher.id, req.sessionToken || '');
  audit(req.teacher, '修改密码', '');
  res.json({ ok: true });
});

module.exports = router;
