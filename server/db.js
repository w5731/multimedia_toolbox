const path = require('path');
const fs = require('fs');
const Database = require('better-sqlite3');
const bcrypt = require('bcryptjs');
const config = require('./config');

const dataDir = path.join(__dirname, 'data');
if (!fs.existsSync(dataDir)) fs.mkdirSync(dataDir, { recursive: true });

const db = new Database(path.join(dataDir, 'toolbox.db'));
db.pragma('journal_mode = WAL');
db.pragma('foreign_keys = ON');

db.exec(`
CREATE TABLE IF NOT EXISTS teachers (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT UNIQUE NOT NULL,
  password_hash TEXT NOT NULL,
  display_name TEXT DEFAULT '',
  is_admin INTEGER DEFAULT 0,
  created_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS sessions (
  token TEXT PRIMARY KEY,
  teacher_id INTEGER NOT NULL,
  expires_at TEXT NOT NULL,
  created_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS classes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT UNIQUE NOT NULL,
  created_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS clients (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  class_id INTEGER,
  name TEXT DEFAULT '',
  machine_code TEXT UNIQUE,
  token TEXT UNIQUE,
  version TEXT DEFAULT '',
  ip TEXT DEFAULT '',
  last_seen TEXT,
  created_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS pairing_codes (
  code TEXT PRIMARY KEY,
  class_id INTEGER NOT NULL,
  expires_at TEXT NOT NULL,
  used INTEGER DEFAULT 0,
  created_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  class_id INTEGER NOT NULL,
  title TEXT NOT NULL,
  start_time TEXT NOT NULL,
  end_time TEXT DEFAULT '',
  date_mode TEXT DEFAULT 'daily',
  date_start TEXT DEFAULT '',
  date_end TEXT DEFAULT '',
  weekdays TEXT DEFAULT '',
  enabled INTEGER DEFAULT 1,
  sort INTEGER DEFAULT 0,
  created_by INTEGER,
  created_at TEXT DEFAULT (datetime('now','localtime')),
  updated_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS calls (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  class_id INTEGER NOT NULL,
  numbers TEXT NOT NULL,
  destination TEXT DEFAULT '办公室',
  reason TEXT DEFAULT '',
  status TEXT DEFAULT 'pending',
  created_by INTEGER,
  created_at TEXT DEFAULT (datetime('now','localtime')),
  shown_at TEXT,
  closed_at TEXT
);

CREATE TABLE IF NOT EXISTS notices (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  class_id INTEGER UNIQUE NOT NULL,
  text TEXT DEFAULT '',
  enabled INTEGER DEFAULT 0,
  updated_by INTEGER,
  updated_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS settings (
  class_id INTEGER PRIMARY KEY,
  popup_seconds INTEGER DEFAULT 10,
  volume INTEGER DEFAULT 50,
  overlay_position TEXT DEFAULT 'right',
  font_scale REAL DEFAULT 1.0,
  data_version INTEGER DEFAULT 1,
  updated_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS audit_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  teacher_id INTEGER,
  teacher_name TEXT DEFAULT '',
  action TEXT,
  detail TEXT,
  created_at TEXT DEFAULT (datetime('now','localtime'))
);

CREATE INDEX IF NOT EXISTS idx_tasks_class ON tasks(class_id);
CREATE INDEX IF NOT EXISTS idx_calls_class ON calls(class_id, status);
CREATE INDEX IF NOT EXISTS idx_sessions_teacher ON sessions(teacher_id);
CREATE INDEX IF NOT EXISTS idx_audit_time ON audit_log(created_at);
`);

// 首次启动创建默认管理员
const teacherCount = db.prepare('SELECT COUNT(*) AS c FROM teachers').get().c;
if (teacherCount === 0) {
  db.prepare('INSERT INTO teachers (username, password_hash, display_name, is_admin) VALUES (?,?,?,1)')
    .run(config.defaultAdminUser, bcrypt.hashSync(config.defaultAdminPass, 10), '管理员');
  console.log(`[init] 已创建默认管理员账号: ${config.defaultAdminUser} / ${config.defaultAdminPass}  (请登录后立即修改密码)`);
}

// 定期清理过期会话与配对码
const { nowStr } = require('./util');
function cleanup() {
  const now = nowStr();
  db.prepare('DELETE FROM sessions WHERE expires_at < ?').run(now);
  db.prepare('DELETE FROM pairing_codes WHERE expires_at < ? OR used = 1').run(now);
}
cleanup();
setInterval(cleanup, 10 * 60 * 1000).unref();

module.exports = db;
