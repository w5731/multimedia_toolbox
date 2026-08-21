// 通用工具函数
const crypto = require('crypto');

function pad(n) { return n < 10 ? '0' + n : '' + n; }

// 本地时间字符串 'YYYY-MM-DD HH:MM:SS',与 SQLite datetime('now','localtime') 格式一致,可直接字典序比较
function nowStr(d = new Date()) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function todayStr(d = new Date()) {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function plusMinutes(min, d = new Date()) {
  return nowStr(new Date(d.getTime() + min * 60000));
}

function plusHours(h, d = new Date()) {
  return nowStr(new Date(d.getTime() + h * 3600000));
}

function randomToken(bytes = 32) {
  return crypto.randomBytes(bytes).toString('hex');
}

// 6 位数字配对码
function randomPairingCode() {
  return String(crypto.randomInt(100000, 1000000));
}

// 星期几:1=周一 ... 7=周日
function weekdayOf(dateStr) {
  const d = new Date(dateStr + 'T00:00:00');
  const w = d.getDay();
  return w === 0 ? 7 : w;
}

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/;
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

// 判断任务在某日期是否生效
function taskAppliesOnDate(task, dateStr) {
  switch (task.date_mode) {
    case 'once':
      return task.date_start === dateStr;
    case 'range':
      return !!task.date_start && !!task.date_end &&
        task.date_start <= dateStr && dateStr <= task.date_end;
    case 'weekly':
      return (task.weekdays || '').split(',').filter(Boolean).map(Number)
        .includes(weekdayOf(dateStr));
    case 'daily':
      return true;
    default:
      return false;
  }
}

module.exports = {
  nowStr, todayStr, plusMinutes, plusHours,
  randomToken, randomPairingCode, weekdayOf,
  taskAppliesOnDate, TIME_RE, DATE_RE,
};
