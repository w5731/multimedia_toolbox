// 班级共享辅助:设置读写、数据版本号、审计日志
const db = require('./db');
const { nowStr } = require('./util');

const DEFAULT_SETTINGS = {
  popup_seconds: 10,
  volume: 50,
  overlay_position: 'right',
  font_scale: 1.0,
};

// 获取班级设置(不存在则创建默认行)
function getSettings(classId) {
  let row = db.prepare('SELECT * FROM settings WHERE class_id = ?').get(classId);
  if (!row) {
    db.prepare('INSERT INTO settings (class_id) VALUES (?)').run(classId);
    row = db.prepare('SELECT * FROM settings WHERE class_id = ?').get(classId);
  }
  return row;
}

// 任务/通知/设置变更后调用:递增版本号,客户端心跳发现版本变化后重新拉取数据
function bumpDataVersion(classId) {
  getSettings(classId);
  db.prepare(`UPDATE settings SET data_version = data_version + 1, updated_at = ? WHERE class_id = ?`)
    .run(nowStr(), classId);
}

function audit(teacher, action, detail) {
  db.prepare('INSERT INTO audit_log (teacher_id, teacher_name, action, detail) VALUES (?,?,?,?)')
    .run(teacher ? teacher.id : null, teacher ? (teacher.display_name || teacher.username) : '', action, String(detail || '').slice(0, 500));
}

// 校验任务字段,返回错误消息或 null
function validateTask(body) {
  const { TIME_RE, DATE_RE } = require('./util');
  if (!body.title || !String(body.title).trim()) return '任务内容不能为空';
  if (!TIME_RE.test(body.start_time || '')) return '开始时间格式应为 HH:MM';
  if (body.end_time && !TIME_RE.test(body.end_time)) return '结束时间格式应为 HH:MM';
  if (body.end_time && body.end_time <= body.start_time) return '结束时间应晚于开始时间';
  const mode = body.date_mode || 'daily';
  if (!['once', 'range', 'weekly', 'daily'].includes(mode)) return '无效的日期模式';
  if (mode === 'once' && !DATE_RE.test(body.date_start || '')) return '单次任务需要选择日期';
  if (mode === 'range') {
    if (!DATE_RE.test(body.date_start || '') || !DATE_RE.test(body.date_end || '')) return '连续任务需要开始与结束日期';
    if (body.date_end < body.date_start) return '结束日期不能早于开始日期';
  }
  if (mode === 'weekly') {
    const days = (body.weekdays || '').split(',').filter(Boolean).map(Number);
    if (!days.length || days.some(d => !(d >= 1 && d <= 7))) return '每周任务需要至少选择一个星期';
  }
  return null;
}

module.exports = { DEFAULT_SETTINGS, getSettings, bumpDataVersion, audit, validateTask };
