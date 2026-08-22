// 教师端主逻辑
'use strict';

// ---------- 基础工具 ----------
const $ = id => document.getElementById(id);

async function api(path, method = 'GET', body) {
  const opt = { method, headers: { 'Content-Type': 'application/json' } };
  if (body !== undefined) opt.body = JSON.stringify(body);
  const r = await fetch('/api' + path, opt);
  if (r.status === 401) { location.href = '/login.html'; throw new Error('未登录'); }
  const d = await r.json();
  if (!d.ok) throw new Error(d.error || '操作失败');
  return d;
}

let toastTimer = null;
function toast(msg, type = 'ok') {
  const t = $('toast');
  t.textContent = msg;
  t.className = 'toast show ' + type;
  t.style.display = 'block';
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { t.style.display = 'none'; }, 2600);
}

function esc(s) {
  return String(s == null ? '' : s).replace(/[&<>"']/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function todayStr() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const WEEK_NAMES = ['', '一', '二', '三', '四', '五', '六', '日'];
const MODE_NAMES = { daily: '每天', weekly: '每周', once: '单次', range: '连续' };
const CALL_STATUS = {
  pending: ['待接收', 'orange'], shown: ['已显示', 'blue'],
  closed: ['已关闭', 'gray'], expired: ['已过期', 'gray'],
};

// 所有表格包一层可横向滚动的容器,窄屏(手机)上列多时可左右滑动
document.querySelectorAll('table').forEach(t => {
  const w = document.createElement('div');
  w.className = 'table-wrap';
  t.parentNode.insertBefore(w, t);
  w.appendChild(t);
});

function taskTimeText(t) {
  return t.end_time ? `${t.start_time} - ${t.end_time}` : t.start_time;
}

// 内容单元格:标题 + 可选的备注小字
function taskTitleHtml(t) {
  const remark = (t.remark || '').trim();
  return esc(t.title) + (remark ? `<div class="cell-remark">备注:${esc(remark)}</div>` : '');
}

function taskDateText(t) {
  switch (t.date_mode) {
    case 'daily': return '每天';
    case 'weekly': return '每周' + (t.weekdays || '').split(',').filter(Boolean)
      .map(d => WEEK_NAMES[Number(d)]).join('、');
    case 'once': return t.date_start;
    case 'range': return `${t.date_start} ~ ${t.date_end}`;
    default: return t.date_mode;
  }
}

// ---------- 全局状态 ----------
let me = null;
let classes = [];
let editingTaskId = null;
let editingClientId = null;
let editingTeacherId = null;

// ---------- 页面切换 ----------
document.querySelectorAll('.nav-item').forEach(el => {
  el.addEventListener('click', () => {
    document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
    document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
    el.classList.add('active');
    $('page-' + el.dataset.page).classList.add('active');
    loadPage(el.dataset.page);
  });
});

function loadPage(page) {
  switch (page) {
    case 'dashboard': loadDashboard(); break;
    case 'tasks': loadTaskPage(); break;
    case 'call': loadCallPage(); break;
    case 'clients': loadClientPage(); break;
    case 'notice': loadNoticePage(); break;
    case 'account': loadAccountPage(); break;
    case 'logs': loadLogs(); break;
  }
}

// ---------- 班级下拉统一刷新 ----------
function fillClassSelect(sel, keepValue = true) {
  const old = keepValue ? sel.value : '';
  sel.innerHTML = classes.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join('');
  if (old && classes.some(c => String(c.id) === old)) sel.value = old;
}

async function refreshClasses() {
  const d = await api('/admin/classes');
  classes = d.classes;
  ['task-class', 'call-class', 'set-class', 'notice-class', 'cm-class'].forEach(id => fillClassSelect($(id)));
}

// ---------- 仪表盘 ----------
async function loadDashboard() {
  const d = await api('/admin/overview');
  const box = $('dash-classes');
  if (!d.classes.length) {
    box.innerHTML = '<div class="card empty">还没有班级,请先到「客户端」页新建班级</div>';
  } else {
    box.innerHTML = d.classes.map(c => {
      const clients = c.clients.length
        ? c.clients.map(cl =>
          `<div><span class="dot ${cl.online ? 'online' : 'offline'}"></span>${esc(cl.name || '未命名')}
           <span class="badge ${cl.online ? 'green' : 'gray'}">${cl.online ? '在线' : '离线'}</span>
           <span style="color:#999;font-size:12px">${esc(cl.last_seen || '从未连接')}</span></div>`).join('')
        : '<div style="color:#999">暂无客户端,请生成配对码后在教室电脑上配对</div>';
      return `<div class="dash-card">
        <h3>${esc(c.name)} <span class="badge blue">今日 ${c.today_task_count} 项任务</span></h3>
        <div class="meta">${clients}</div>
      </div>`;
    }).join('');
  }
  const tbody = $('dash-calls').querySelector('tbody');
  tbody.innerHTML = d.recent_calls.length ? d.recent_calls.map(callRowHtml).join('')
    : '<tr><td colspan="7" class="empty">暂无叫号记录</td></tr>';
}

function callRowHtml(c) {
  const [txt, color] = CALL_STATUS[c.status] || [c.status, 'gray'];
  return `<tr>
    <td style="white-space:nowrap">${esc(c.created_at)}</td>
    <td>${esc(c.class_name || '')}</td>
    <td><b>${esc(c.numbers)}</b></td>
    <td>${esc(c.destination)}</td>
    <td>${esc(c.reason || '-')}</td>
    <td><span class="badge ${color}">${txt}</span></td>
    <td>${esc(c.teacher_name || '')}</td>
  </tr>`;
}

// ---------- 任务管理 ----------
async function loadTaskPage() {
  await refreshClasses();
  if (!$('task-preview-date').value) $('task-preview-date').value = todayStr();
  await loadTasks();
}

async function loadTasks() {
  const classId = $('task-class').value;
  if (!classId) {
    $('task-table').querySelector('tbody').innerHTML = '<tr><td colspan="5" class="empty">请先新建班级</td></tr>';
    $('task-preview-table').querySelector('tbody').innerHTML = '<tr><td colspan="2" class="empty">-</td></tr>';
    return;
  }
  const date = $('task-preview-date').value || todayStr();
  const [all, today] = await Promise.all([
    api(`/admin/tasks?class_id=${classId}`),
    api(`/admin/tasks?class_id=${classId}&date=${date}`),
  ]);
  $('task-preview-title').textContent = `${date} 生效任务(共 ${today.tasks.length} 项)`;
  $('task-preview-table').querySelector('tbody').innerHTML = today.tasks.length
    ? today.tasks.map(t => `<tr><td class="time-tag">${taskTimeText(t)}</td><td class="title-cell">${taskTitleHtml(t)}</td></tr>`).join('')
    : '<tr><td colspan="2" class="empty">这一天没有任务</td></tr>';
  $('task-table').querySelector('tbody').innerHTML = all.tasks.length
    ? all.tasks.map(t => `<tr class="${t.enabled ? '' : 'row-disabled'}">
        <td class="time-tag">${taskTimeText(t)}</td>
        <td class="title-cell">${taskTitleHtml(t)}</td>
        <td>${taskDateText(t)}</td>
        <td><span class="badge ${t.enabled ? 'green' : 'gray'}">${t.enabled ? '启用' : '停用'}</span></td>
        <td style="white-space:nowrap">
          <button class="btn sm outline" onclick="editTask(${t.id})">编辑</button>
          <button class="btn sm ${t.enabled ? 'gray' : 'green'}" onclick="toggleTask(${t.id},${t.enabled ? 0 : 1})">${t.enabled ? '停用' : '启用'}</button>
          <button class="btn sm red" onclick="delTask(${t.id})">删除</button>
        </td></tr>`).join('')
    : '<tr><td colspan="5" class="empty">还没有任务,点击右上角「新建任务」</td></tr>';
  window._tasks = all.tasks;
}

function openTaskModal(t) {
  editingTaskId = t ? t.id : null;
  $('task-modal-title').textContent = t ? '编辑任务' : '新建任务';
  $('f-title').value = t ? t.title : '';
  $('f-remark').value = t ? (t.remark || '') : '';
  $('f-start').value = t ? t.start_time : '';
  $('f-end').value = t ? (t.end_time || '') : '';
  $('f-mode').value = t ? t.date_mode : 'daily';
  $('f-date-start').value = t ? (t.date_start || '') : todayStr();
  $('f-date-end').value = t ? (t.date_end || '') : todayStr();
  document.querySelectorAll('#f-weekdays input').forEach(cb => {
    cb.checked = t ? (t.weekdays || '').split(',').includes(cb.value) : ['1', '2', '3', '4', '5'].includes(cb.value);
  });
  updateTaskModeUI();
  $('task-modal').classList.add('show');
}

function updateTaskModeUI() {
  const mode = $('f-mode').value;
  $('f-weekdays-row').style.display = mode === 'weekly' ? '' : 'none';
  $('f-date-row').style.display = (mode === 'once' || mode === 'range') ? '' : 'none';
  $('f-date-end-row').style.display = mode === 'range' ? '' : 'none';
  $('f-date-start-label').textContent = mode === 'range' ? '开始日期' : '日期';
}

async function saveTask() {
  const mode = $('f-mode').value;
  const body = {
    title: $('f-title').value.trim(),
    remark: $('f-remark').value.trim(),
    start_time: $('f-start').value,
    end_time: $('f-end').value || '',
    date_mode: mode,
    date_start: (mode === 'once' || mode === 'range') ? $('f-date-start').value : '',
    date_end: mode === 'range' ? $('f-date-end').value : '',
    weekdays: mode === 'weekly'
      ? Array.from(document.querySelectorAll('#f-weekdays input:checked')).map(c => c.value).join(',') : '',
  };
  try {
    if (editingTaskId) {
      await api('/admin/tasks/' + editingTaskId, 'PUT', body);
    } else {
      body.class_id = Number($('task-class').value);
      if (!body.class_id) { toast('请先新建班级', 'err'); return; }
      await api('/admin/tasks', 'POST', body);
    }
    $('task-modal').classList.remove('show');
    toast('已保存,客户端约 3 秒内更新');
    loadTasks();
  } catch (e) { toast(e.message, 'err'); }
}

window.editTask = id => openTaskModal((window._tasks || []).find(t => t.id === id));
window.toggleTask = async (id, enable) => {
  try {
    await api('/admin/tasks/' + id, 'PUT', { enabled: !!enable });
    loadTasks();
  } catch (e) { toast(e.message, 'err'); }
};
window.delTask = async id => {
  if (!confirm('确定删除该任务?')) return;
  try { await api('/admin/tasks/' + id, 'DELETE'); loadTasks(); }
  catch (e) { toast(e.message, 'err'); }
};

// ---------- 叫号 ----------
async function loadCallPage() {
  await refreshClasses();
  loadCalls();
}

async function loadCalls() {
  const classId = $('call-class').value;
  const d = await api('/admin/calls' + (classId ? `?class_id=${classId}` : ''));
  $('call-table').querySelector('tbody').innerHTML = d.calls.length
    ? d.calls.map(c => {
      const [txt, color] = CALL_STATUS[c.status] || [c.status, 'gray'];
      const cancel = c.status === 'pending'
        ? `<button class="btn sm gray" onclick="cancelCall(${c.id})">撤销</button>` : '';
      return `<tr>
        <td style="white-space:nowrap">${esc(c.created_at)}</td>
        <td>${esc(c.class_name || $('call-class').selectedOptions[0]?.textContent || '')}</td>
        <td><b>${esc(c.numbers)}</b></td>
        <td>${esc(c.destination)}</td>
        <td>${esc(c.reason || '-')}</td>
        <td><span class="badge ${color}">${txt}</span></td>
        <td>${cancel}</td></tr>`;
    }).join('')
    : '<tr><td colspan="7" class="empty">暂无叫号记录</td></tr>';
}

window.cancelCall = async id => {
  try { await api(`/admin/calls/${id}/cancel`, 'POST'); loadCalls(); }
  catch (e) { toast(e.message, 'err'); }
};

// ---------- 客户端管理 ----------
async function loadClientPage() {
  await refreshClasses();
  const [cls, clients] = await Promise.all([api('/admin/classes'), api('/admin/clients')]);
  $('class-table').querySelector('tbody').innerHTML = cls.classes.length
    ? cls.classes.map(c => `<tr>
        <td><b>${esc(c.name)}</b></td><td>${c.client_count}</td><td>${c.task_count}</td>
        <td style="white-space:nowrap">
          <button class="btn sm primary" onclick="genPairCode(${c.id},'${esc(c.name)}')">生成配对码</button>
          <button class="btn sm outline" onclick="renameClass(${c.id},'${esc(c.name)}')">重命名</button>
          <button class="btn sm red" onclick="delClass(${c.id})">删除</button>
        </td></tr>`).join('')
    : '<tr><td colspan="4" class="empty">还没有班级</td></tr>';
  $('client-table').querySelector('tbody').innerHTML = clients.clients.length
    ? clients.clients.map(c => `<tr>
        <td><span class="dot ${c.online ? 'online' : 'offline'}"></span>${c.online ? '在线' : '离线'}</td>
        <td>${esc(c.name || '-')}</td>
        <td>${esc(c.class_name || '未绑定')}</td>
        <td>${esc(c.version || '-')}</td>
        <td>${esc(c.ip || '-')}</td>
        <td style="white-space:nowrap">${esc(c.last_seen || '从未连接')}</td>
        <td style="white-space:nowrap">
          <button class="btn sm outline" onclick="editClient(${c.id},'${esc(c.name || '')}',${c.class_id || 0})">编辑</button>
          <button class="btn sm red" onclick="delClient(${c.id})">解绑</button>
        </td></tr>`).join('')
    : '<tr><td colspan="7" class="empty">暂无客户端,先为班级生成配对码</td></tr>';
  loadSettings();
}

window.genPairCode = async (classId, className) => {
  try {
    const d = await api(`/admin/classes/${classId}/pairing-code`, 'POST');
    $('pair-code').textContent = d.code;
    $('pair-expire').textContent = `班级「${className}」 · 有效期至 ${d.expires_at}`;
    $('pair-modal').classList.add('show');
  } catch (e) { toast(e.message, 'err'); }
};

window.renameClass = async (id, oldName) => {
  const name = prompt('新的班级名称:', oldName);
  if (!name || name === oldName) return;
  try { await api('/admin/classes/' + id, 'PUT', { name }); loadClientPage(); }
  catch (e) { toast(e.message, 'err'); }
};

window.delClass = async id => {
  if (!confirm('确定删除该班级?其任务、通知、设置都会被删除。')) return;
  try { await api('/admin/classes/' + id, 'DELETE'); loadClientPage(); }
  catch (e) { toast(e.message, 'err'); }
};

window.editClient = (id, name, classId) => {
  editingClientId = id;
  $('cm-name').value = name;
  if (classId) $('cm-class').value = classId;
  $('client-modal').classList.add('show');
};

window.delClient = async id => {
  if (!confirm('确定解绑该客户端?解绑后需重新配对才能使用。')) return;
  try { await api('/admin/clients/' + id, 'DELETE'); loadClientPage(); }
  catch (e) { toast(e.message, 'err'); }
};

async function loadSettings() {
  const classId = $('set-class').value;
  if (!classId) return;
  const d = await api(`/admin/settings?class_id=${classId}`);
  $('set-popup').value = d.settings.popup_seconds;
  $('set-volume').value = d.settings.volume;
  $('set-position').value = d.settings.overlay_position;
  $('set-fontscale').value = String(d.settings.font_scale);
}

// ---------- 通知 ----------
async function loadNoticePage() {
  await refreshClasses();
  loadNotice();
}

async function loadNotice() {
  const classId = $('notice-class').value;
  if (!classId) return;
  const d = await api(`/admin/notice?class_id=${classId}`);
  $('notice-text').value = d.notice.text || '';
  $('notice-enabled').checked = !!d.notice.enabled;
}

// ---------- 账号 ----------
async function loadAccountPage() {
  if (me && me.is_admin) {
    $('teacher-card').style.display = '';
    const d = await api('/admin/teachers');
    $('teacher-table').querySelector('tbody').innerHTML = d.teachers.map(t => `<tr>
      <td>${esc(t.username)}</td>
      <td>${esc(t.display_name || '-')}</td>
      <td>${t.is_admin ? '<span class="badge blue">管理员</span>' : '<span class="badge gray">教师</span>'}</td>
      <td>${esc(t.created_at)}</td>
      <td style="white-space:nowrap">
        <button class="btn sm outline" onclick="editTeacher(${t.id},'${esc(t.display_name || '')}',${t.is_admin})">编辑</button>
        <button class="btn sm red" onclick="delTeacher(${t.id})">删除</button>
      </td></tr>`).join('');
  } else {
    $('teacher-card').style.display = 'none';
  }
}

window.editTeacher = (id, display, isAdmin) => {
  editingTeacherId = id;
  $('tm-display').value = display;
  $('tm-admin').checked = !!isAdmin;
  $('tm-password').value = '';
  $('teacher-modal').classList.add('show');
};

window.delTeacher = async id => {
  if (!confirm('确定删除该账号?')) return;
  try { await api('/admin/teachers/' + id, 'DELETE'); loadAccountPage(); }
  catch (e) { toast(e.message, 'err'); }
};

// ---------- 日志 ----------
async function loadLogs() {
  const d = await api('/admin/audit-log');
  $('log-table').querySelector('tbody').innerHTML = d.logs.length
    ? d.logs.map(l => `<tr>
        <td style="white-space:nowrap">${esc(l.created_at)}</td>
        <td>${esc(l.teacher_name)}</td>
        <td>${esc(l.action)}</td>
        <td>${esc(l.detail)}</td></tr>`).join('')
    : '<tr><td colspan="4" class="empty">暂无日志</td></tr>';
}

// ---------- 事件绑定 ----------
function bindEvents() {
  $('logout').addEventListener('click', async () => {
    try { await api('/auth/logout', 'POST'); } catch (e) { }
    location.href = '/login.html';
  });

  // 任务
  $('task-class').addEventListener('change', loadTasks);
  $('task-preview-date').addEventListener('change', loadTasks);
  $('btn-add-task').addEventListener('click', () => openTaskModal(null));
  $('f-mode').addEventListener('change', updateTaskModeUI);
  $('task-save').addEventListener('click', saveTask);
  $('task-cancel').addEventListener('click', () => $('task-modal').classList.remove('show'));
  $('btn-copy-day').addEventListener('click', () => {
    $('cp-from').value = $('task-preview-date').value || todayStr();
    $('cp-to').value = '';
    $('copy-modal').classList.add('show');
  });
  $('copy-cancel').addEventListener('click', () => $('copy-modal').classList.remove('show'));
  $('copy-save').addEventListener('click', async () => {
    try {
      const toDates = $('cp-to').value.split(/[,,\s]+/).filter(Boolean);
      const d = await api('/admin/tasks/copy-day', 'POST', {
        class_id: Number($('task-class').value),
        from_date: $('cp-from').value, to_dates: toDates,
      });
      $('copy-modal').classList.remove('show');
      toast(`已复制 ${d.count} 条任务`);
      loadTasks();
    } catch (e) { toast(e.message, 'err'); }
  });

  // 叫号
  $('call-class').addEventListener('change', loadCalls);
  $('call-form').addEventListener('submit', async e => {
    e.preventDefault();
    try {
      await api('/admin/calls', 'POST', {
        class_id: Number($('call-class').value),
        numbers: $('call-numbers').value,
        destination: $('call-dest').value,
        reason: $('call-reason').value,
      });
      toast('叫号已发出,客户端约 3 秒内弹窗');
      $('call-numbers').value = '';
      $('call-reason').value = '';
      loadCalls();
    } catch (e2) { toast(e2.message, 'err'); }
  });
  const QUICK_DEST = ['办公室', '教务处', '医务室', '门卫室', '多功能厅'];
  $('quick-dest').innerHTML = QUICK_DEST.map(d =>
    `<button type="button" class="btn sm" data-dest="${d}">${d}</button>`).join('');
  $('quick-dest').addEventListener('click', e => {
    if (e.target.dataset.dest) $('call-dest').value = e.target.dataset.dest;
  });

  // 客户端
  $('btn-add-class').addEventListener('click', async () => {
    const name = $('new-class-name').value.trim();
    if (!name) { toast('请输入班级名称', 'err'); return; }
    try {
      await api('/admin/classes', 'POST', { name });
      $('new-class-name').value = '';
      toast('班级已创建');
      loadClientPage();
    } catch (e) { toast(e.message, 'err'); }
  });
  $('pair-close').addEventListener('click', () => $('pair-modal').classList.remove('show'));
  $('client-cancel').addEventListener('click', () => $('client-modal').classList.remove('show'));
  $('client-save').addEventListener('click', async () => {
    try {
      await api('/admin/clients/' + editingClientId, 'PUT', {
        name: $('cm-name').value, class_id: Number($('cm-class').value) || undefined,
      });
      $('client-modal').classList.remove('show');
      loadClientPage();
    } catch (e) { toast(e.message, 'err'); }
  });
  $('set-class').addEventListener('change', loadSettings);
  $('btn-save-settings').addEventListener('click', async () => {
    try {
      await api('/admin/settings', 'PUT', {
        class_id: Number($('set-class').value),
        popup_seconds: Number($('set-popup').value),
        volume: Number($('set-volume').value),
        overlay_position: $('set-position').value,
        font_scale: Number($('set-fontscale').value),
      });
      toast('设置已下发,客户端约 3 秒内生效');
    } catch (e) { toast(e.message, 'err'); }
  });

  // 通知
  $('notice-class').addEventListener('change', loadNotice);
  $('btn-save-notice').addEventListener('click', async () => {
    try {
      await api('/admin/notice', 'PUT', {
        class_id: Number($('notice-class').value),
        text: $('notice-text').value,
        enabled: $('notice-enabled').checked,
      });
      toast('通知已下发');
    } catch (e) { toast(e.message, 'err'); }
  });

  // 账号
  $('btn-change-pwd').addEventListener('click', async () => {
    try {
      await api('/auth/change-password', 'POST', {
        old_password: $('pwd-old').value, new_password: $('pwd-new').value,
      });
      $('pwd-old').value = ''; $('pwd-new').value = '';
      toast('密码已修改');
    } catch (e) { toast(e.message, 'err'); }
  });
  $('btn-add-teacher').addEventListener('click', async () => {
    try {
      await api('/admin/teachers', 'POST', {
        username: $('t-username').value, display_name: $('t-display').value,
        password: $('t-password').value, is_admin: $('t-admin').checked,
      });
      $('t-username').value = ''; $('t-display').value = '';
      $('t-password').value = ''; $('t-admin').checked = false;
      toast('账号已创建');
      loadAccountPage();
    } catch (e) { toast(e.message, 'err'); }
  });
  $('teacher-cancel').addEventListener('click', () => $('teacher-modal').classList.remove('show'));
  $('teacher-save').addEventListener('click', async () => {
    try {
      await api('/admin/teachers/' + editingTeacherId, 'PUT', {
        display_name: $('tm-display').value,
        is_admin: $('tm-admin').checked,
        password: $('tm-password').value || undefined,
      });
      $('teacher-modal').classList.remove('show');
      loadAccountPage();
    } catch (e) { toast(e.message, 'err'); }
  });
}

// ---------- 启动 ----------
(async function init() {
  try {
    const d = await api('/auth/me');
    me = d.teacher;
    $('me-name').textContent = (me.display_name || me.username) + (me.is_admin ? '(管理员)' : '');
    bindEvents();
    loadDashboard();
    // 仪表盘与叫号页定时刷新
    setInterval(() => {
      if ($('page-dashboard').classList.contains('active')) loadDashboard();
      if ($('page-call').classList.contains('active')) loadCalls();
    }, 5000);
  } catch (e) { /* api 已处理跳转登录 */ }
})();
