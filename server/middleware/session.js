// 会话中间件:解析 Cookie,挂载 req.teacher
const db = require('../db');
const { nowStr } = require('../util');

function parseCookies(req) {
  const header = req.headers.cookie || '';
  const out = {};
  header.split(';').forEach(part => {
    const idx = part.indexOf('=');
    if (idx > -1) out[part.slice(0, idx).trim()] = decodeURIComponent(part.slice(idx + 1).trim());
  });
  return out;
}

const getSessionStmt = () => db.prepare(
  `SELECT s.token, s.expires_at, t.id, t.username, t.display_name, t.is_admin
   FROM sessions s JOIN teachers t ON t.id = s.teacher_id WHERE s.token = ?`);

module.exports = function session(req, res, next) {
  req.cookies = parseCookies(req);
  req.teacher = null;
  const token = req.cookies['mt_session'];
  if (token) {
    try {
      const row = getSessionStmt().get(token);
      if (row && row.expires_at > nowStr()) {
        req.teacher = { id: row.id, username: row.username, display_name: row.display_name, is_admin: row.is_admin };
        req.sessionToken = token;
      }
    } catch (e) { /* 数据库异常时按未登录处理 */ }
  }
  next();
};

module.exports.requireTeacher = function (req, res, next) {
  if (!req.teacher) return res.status(401).json({ ok: false, error: '未登录或会话已过期' });
  next();
};

module.exports.requireAdmin = function (req, res, next) {
  if (!req.teacher) return res.status(401).json({ ok: false, error: '未登录或会话已过期' });
  if (!req.teacher.is_admin) return res.status(403).json({ ok: false, error: '需要管理员权限' });
  next();
};
